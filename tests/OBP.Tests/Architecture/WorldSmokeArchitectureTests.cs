namespace OBP.Tests;

/// <summary>
/// Fail closed when source-specific smoke entry points drift back into ordinary
/// destination loading or lose their explicit first-world / game gates.
/// </summary>
public sealed class WorldSmokeArchitectureTests
{
    [Fact]
    public void DestinationLoaderDelegatesSmokeSelectionToDiagnosticHarness()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string loader = File.ReadAllText(Path.Combine(scripts, "Application/ApplicationNavigationCoordinator.cs"));
        string entry = File.ReadAllText(Path.Combine(scripts, "OBPGame.cs"));
        string harness = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "game", "diagnostics", "Framework/WorldSmokeHarness.cs"));
        string dispatch = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "game", "diagnostics", "Fixtures/GodotWorldSmokeDispatch.cs"));

        Assert.Contains("_ports.EnterWorld(destination.DestinationId", loader, StringComparison.Ordinal);
        Assert.Contains("GodotWorldSmokeDispatch.Start(new(", entry, StringComparison.Ordinal);
        Assert.Contains("new WorldSmokeHarness.Factories(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldSmokeHarness.Create(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticScenarioDispatch.Start(", entry, StringComparison.Ordinal);
        Assert.Contains("WorldSmokeHarness.Create(", dispatch, StringComparison.Ordinal);
        Assert.Contains("WorldSmokeHarness.Bind(ports.Factories)", dispatch, StringComparison.Ordinal);
        Assert.Contains("DiagnosticScenarioDispatch.Start(scenarios, ports.Operations, runner, ports.Quit, ports.LogError)", dispatch, StringComparison.Ordinal);
        Assert.Contains("internal static Entrypoints Bind(", harness, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", harness, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", dispatch, StringComparison.Ordinal);
        Assert.Contains("Capture.Verification.StartForWorldEntry(nativeDestination, _args.VerifyHash, _worldSwitches)", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private void StartRequestedSourceVerification(", entry, StringComparison.Ordinal);
        foreach (string flag in new[]
        {
            "_args.Rac1VeldinPlaySmoke",
            "_args.Rac1MovementContactSmoke",
            "_args.Rac1CombatContractSmoke",
            "_args.Rac1CampaignSmoke",
            "_args.MovementSmoke",
        })
            Assert.DoesNotContain(flag, loader, StringComparison.Ordinal);

    }

    [Fact]
    public void GodotDispatchUsesCommittedWorldAndLiveSourceBuildSnapshots()
    {
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        string dispatch = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "diagnostics", "Fixtures/GodotWorldSmokeDispatch.cs"));

        Assert.Contains("WorldSmokeHarness.Create(", dispatch, StringComparison.Ordinal);
        Assert.Contains("WorldSmokeHarness.Bind(ports.Factories)", dispatch, StringComparison.Ordinal);
        Assert.Contains("if (scenarios.Length == 0) return;", dispatch, StringComparison.Ordinal);
        Assert.Contains("ports.Sources.Attached.Values.Select(source =>", dispatch, StringComparison.Ordinal);
        Assert.Contains("new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true)", dispatch, StringComparison.Ordinal);
        Assert.Contains("new GodotDiagnosticFrameClock(ports.Host)", dispatch, StringComparison.Ordinal);
        Assert.Contains("new ExistingDiagnosticWorld(ports.ReadWorld, ports.Operations.Token)", dispatch, StringComparison.Ordinal);

        // The prior world must be committed before any scenario observes it.
        // Hash verification remains after smoke dispatch, before profile save.
        string navigation = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Application/ApplicationNavigationCoordinator.cs"));
        int commit = navigation.IndexOf("if (!prepareAndCommit()) return false;", StringComparison.Ordinal);
        int completed = navigation.IndexOf("completed();", commit, StringComparison.Ordinal);
        // The start of the completion callback passed to EnterPreparedWorld: dispatch must follow it,
        // so moving it into the prepare-and-commit lambda would fail this check.
        int completion = entry.IndexOf("_lastCommittedProfileDestination = destinationId;", StringComparison.Ordinal);
        int start = entry.IndexOf("GodotWorldSmokeDispatch.Start(new(", StringComparison.Ordinal);
        int verify = entry.IndexOf("Capture.Verification.StartForWorldEntry(nativeDestination,", StringComparison.Ordinal);
        int save = entry.IndexOf("SaveSharedProfile(\"world transition\");", StringComparison.Ordinal);
        Assert.True(commit >= 0 && completed > commit && completion >= 0 && start > completion && start < verify && verify < save,
            "World diagnostics must observe the committed lease before source verification and profile save.");
    }

    [Fact]
    public void WorldSmokeBindingsPreserveCreatorSpecificSlots()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "diagnostics", "Framework/WorldSmokeHarness.cs"));
        string bind = source.Split("internal static Entrypoints Bind(Factories factories) => new(", StringSplitOptions.None)[1]
            .Split("private sealed record Entry(", StringSplitOptions.None)[0];
        // Slot order matches Entrypoints. Detect swaps between creators on the same harness;
        // the runtime registry test independently verifies the scenario IDs and owner factories.
        string[] expected =
        [
            "definition => factories.Encounter().CreateVeldin(definition)",
            "definition => factories.Encounter().CreateMovementContact(definition)",
            "definition => factories.Encounter().CreateCombat(definition)",
            "definition => factories.Campaign().CreateCampaign(definition)",
            "definition => factories.Movement().CreateMovement(definition)",
            "definition => factories.Loot().CreateCrates(definition)",
            "definition => factories.Loot().CreatePlacedBolts(definition)",
            "definition => factories.Weapons().CreateWrench(definition)",
            "definition => factories.Encounter().CreateEnemies(definition)",
            "definition => factories.Capture().CreateMovement(definition)",
            "definition => factories.Weapons().CreateBomb(definition)",
            "definition => factories.Movement().CreateSwim(definition)",
            "definition => factories.Encounter().CreatePeckbot(definition)",
            "definition => factories.Encounter().CreateBirdbot(definition)",
            "definition => factories.Routes().CreateMusic(definition)",
            "definition => factories.Weapons().CreateWeapons(definition)",
            "definition => factories.Player().CreateAudio(definition)",
            "definition => factories.Routes().CreateTaxi(definition)",
            "definition => factories.Campaign().CreateMission(definition)",
            "definition => factories.Routes().CreateRoute(definition)",
            "definition => factories.Encounter().CreateFrogs(definition)",
            "factories.Rac3SeedAudit",
        ];
        var actual = System.Text.RegularExpressions.Regex.Matches(bind,
            @"definition => factories\.\w+\(\)\.\w+\(definition\)|factories\.Rac3SeedAudit",
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Value).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GameSpecificSmokeImplementationsLiveOnlyUnderDiagnostics()
    {
        string root = Path.Combine(RepoPaths.Root, "game");
        foreach (string name in new[]
        {
            "Rac1/Rac1CombatContractScenario.cs",
            "Rac1/Rac1VeldinScenario.cs",
            "Movement/MovementScenario.cs",
            "Rac1/Rac1CampaignScenario.cs",
            "Rac1/Rac1MissionPlayScenario.cs",
            "Rac1/Rac1CrateScenario.cs",
            "Rac1/Rac1PlacedBoltScenario.cs",
            "Rac1/Rac1NovalisRouteScenario.cs",
            "Rac1/Rac1NovalisMusicScenario.cs",
            "Rac1/Rac1MetropolisTaxiScenario.cs",
            "Rac1/Rac1EnvironmentalDeathRegression.cs",
            "Startup/StartupVisibilityScenario.cs",
            "Rac1/Rac1WrenchScenario.cs",
            "Rac1/Rac1BombGloveScenario.cs",
            "Rac1/Rac1WeaponsScenario.cs",
            "Rac1/Rac1ArsenalScenario.cs",
            "Framework/FusionWeaponsScenario.cs",
            "Movement/MovementStateScenario.cs",
            "CaptureReplay/PlaytestRecordingScenario.cs",
            "CaptureReplay/ReconciliationCaptureScenario.cs",
            "CaptureReplay/BugReporterScenario.cs",
            "Player/TrilogyGameplayScenario.cs",
        })
        {
            Assert.True(File.Exists(Path.Combine(root, "diagnostics", name)), name);
            Assert.False(File.Exists(Path.Combine(root, "scripts", name)), name);
        }

        string play = File.ReadAllText(Path.Combine(
            root, "diagnostics", "Rac1/Rac1VeldinScenario.cs"));
        string contract = File.ReadAllText(Path.Combine(
            root, "diagnostics", "Rac1/Rac1CombatContractScenario.cs"));
        Assert.Contains("new Rac1EnvironmentalDeathRegression(", play, StringComparison.Ordinal);
        Assert.Contains("Rac1SmokeDriveToward(", play, StringComparison.Ordinal);
        Assert.Contains("synthetic", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", play, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredComparisonViewerIsNotShipped()
    {
        foreach (string path in new[] { "game/scenes/CompositionLab.tscn", "game/scripts/CompositionLab.cs", "game/scripts/CompositionWorldLoader.cs", "src/OBP.Godot/CompositionView.cs", "src/OBP.Fusion/WorldComposition.cs", "src/OBP.Fusion/CompositionJson.cs" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, path)), "Retired comparison viewer returned: " + path);
    }
}

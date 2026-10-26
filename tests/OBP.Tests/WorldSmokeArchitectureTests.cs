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
        string loader = File.ReadAllText(Path.Combine(scripts, "OBPGame.Destinations.cs"));
        string entry = File.ReadAllText(Path.Combine(scripts, "OBPGame.WorldEntry.cs"));
        string harness = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "game", "diagnostics", "WorldSmokeHarness.cs"));

        Assert.Contains("EnterWorld(destination.DestinationId", loader, StringComparison.Ordinal);
        Assert.Contains("WorldSmokeHarness.Create(", entry, StringComparison.Ordinal);
        Assert.Contains("DiagnosticScenarioDispatch.Start(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", harness, StringComparison.Ordinal);
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
    public void GameSpecificSmokeImplementationsLiveOnlyUnderDiagnostics()
    {
        string root = Path.Combine(RepoPaths.Root, "game");
        foreach (string name in new[]
        {
            "OBPGame.Rac1CombatContractSmoke.cs",
            "OBPGame.Rac1VeldinPlaySmoke.cs",
            "OBPGame.MovementSmoke.cs",
            "OBPGame.Rac1CampaignSmoke.cs",
            "OBPGame.Rac1EnvironmentalDeathSmoke.cs",
            "OBPGame.Rac1StartupVisibilitySmoke.cs",
            "OBPGame.Rac1WrenchSmoke.cs",
            "OBPGame.Rac1BombGloveSmoke.cs",
            "OBPGame.MovementStateSmoke.cs",
        })
        {
            Assert.True(File.Exists(Path.Combine(root, "diagnostics", name)), name);
            Assert.False(File.Exists(Path.Combine(root, "scripts", name)), name);
        }

        string play = File.ReadAllText(Path.Combine(
            root, "diagnostics", "OBPGame.Rac1VeldinPlaySmoke.cs"));
        string contract = File.ReadAllText(Path.Combine(
            root, "diagnostics", "OBPGame.Rac1CombatContractSmoke.cs"));
        Assert.Contains("RunRac1NaturalVeldinFallRespawnSmokeAsync(", play, StringComparison.Ordinal);
        Assert.Contains("Rac1SmokeDriveToward(", play, StringComparison.Ordinal);
        Assert.Contains("synthetic", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredComparisonViewerIsNotShipped()
    {
        foreach (string path in new[] { "game/scenes/CompositionLab.tscn", "game/scripts/CompositionLab.cs", "game/scripts/CompositionWorldLoader.cs", "src/OBP.Godot/CompositionView.cs", "src/OBP.Fusion/WorldComposition.cs", "src/OBP.Fusion/CompositionJson.cs" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, path)), "Retired comparison viewer returned: " + path);
    }
}

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
        string harness = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "game", "diagnostics", "OBPGame.SmokeDispatch.cs"));

        Assert.Contains("DispatchRequestedWorldSmokes(destination);", loader, StringComparison.Ordinal);
        foreach (string flag in new[]
        {
            "_args.Rac1VeldinPlaySmoke",
            "_args.Rac1MovementContactSmoke",
            "_args.Rac1CombatContractSmoke",
            "_args.Rac1CampaignSmoke",
            "_args.MovementSmoke",
        })
            Assert.DoesNotContain(flag, loader, StringComparison.Ordinal);

        Assert.Contains("if (worldSwitches != 1)", harness, StringComparison.Ordinal);
        Assert.Contains("destination.Game == ObpSourceGame.Rac1", harness, StringComparison.Ordinal);
        Assert.Contains("if (args.MovementSmoke)", harness, StringComparison.Ordinal);
        Assert.Contains("RunRac1VeldinPlaySmokeAsync,", harness, StringComparison.Ordinal);
        Assert.Contains("RunRac1MovementContactSmokeAsync,", harness, StringComparison.Ordinal);
        Assert.Contains("RunRac1CombatContractSmokeAsync,", harness, StringComparison.Ordinal);
        Assert.Contains("RunRac1CampaignSmokeAsync,", harness, StringComparison.Ordinal);
        Assert.Contains("RunMovementSmokeAsync));", harness, StringComparison.Ordinal);
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
    public void CompositionLabRetainsAnIndependentGodotScene()
    {
        string scene = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "game", "scenes", "CompositionLab.tscn"));
        string lab = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "game", "scripts", "CompositionLab.cs"));
        Assert.Contains("res://scripts/CompositionLab.cs", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame.cs", scene, StringComparison.Ordinal);
        Assert.Contains("StartupArgs ?? CommandLineArgs.Parse(OS.GetCmdlineUserArgs())", lab, StringComparison.Ordinal);
    }
}

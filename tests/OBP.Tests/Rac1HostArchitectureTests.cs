namespace OBP.Tests;

public sealed class Rac1HostArchitectureTests
{
    [Fact]
    public void Rac1DeathHasNoManualRKeyRespawnBypass()
    {
        string player = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "PlayerHost.cs"));
        string developmentControls = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "PlayerDevelopmentControls.cs"));
        string gameplay = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1Gameplay.cs"));

        Assert.DoesNotContain("Rac1RespawnRequested", player, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1RespawnRequested", gameplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.R", player, StringComparison.Ordinal);
        Assert.Contains(
            "key.Keycode == Key.R && !_host.UseRac1Gameplay",
            developmentControls,
            StringComparison.Ordinal);
        Assert.Contains(
            "_rac1Nanotech.HasPendingRecoveredEnvironmentalRestart",
            gameplay,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryCompleteRac1EnvironmentalRestart(automatic: true)",
            gameplay,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryVeldinSmokeCannotUseDevelopmentOrDirectConsequenceShortcuts()
    {
        string smoke = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1VeldinPlaySmoke.cs"));

        string[] forbidden =
        [
            "ResetToSpawn(",
            "TryCompleteRac1EnvironmentalRestart(",
            "ApplyEnvironmentalDeathReset(",
            "ApplyTerminalStatus(",
            "SetRac1HostedPresence(",
            "TapPhysicalKeyAsync(Key.R)",
            "TapPhysicalKeyAsync(Key.F)",
            ".Root.GlobalPosition =",
            "_player.GlobalPosition =",
        ];

        foreach (string shortcut in forbidden)
            Assert.DoesNotContain(shortcut, smoke, StringComparison.Ordinal);

        Assert.Contains(
            "Input.ActionPress(RawGamepadInput.Action)",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains(
            "Rac1SmokeDriveToward(",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains(
            "RunRac1NaturalVeldinFallRespawnSmokeAsync",
            smoke,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerDevelopmentControlsStayOutOfProductionInputBindings()
    {
        string player = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "PlayerHost.cs"));
        string developmentControls = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "PlayerDevelopmentControls.cs"));

        foreach (string devKey in new[] { "Key.Tab", "Key.F8", "Key.F9", "Key.F", "Key.R" })
            Assert.DoesNotContain(devKey, player, StringComparison.Ordinal);

        Assert.Contains("Key.Tab", developmentControls, StringComparison.Ordinal);
        Assert.Contains("Key.F8", developmentControls, StringComparison.Ordinal);
        Assert.Contains("Key.F9", developmentControls, StringComparison.Ordinal);
        Assert.Contains("Key.F", developmentControls, StringComparison.Ordinal);
        Assert.Contains("Key.R", developmentControls, StringComparison.Ordinal);
    }

    [Fact]
    public void Rac1HudDoesNotAdvertiseManualEnvironmentalRespawn()
    {
        string gameplay = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1Gameplay.cs"));

        Assert.DoesNotContain(
            "R recovered environmental respawn",
            gameplay,
            StringComparison.Ordinal);
        Assert.Contains(
            "automatic recovered environmental respawn pending",
            gameplay,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rac1GameplayDoesNotInventClass749TerminalStateSelector()
    {
        string gameplay = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1Gameplay.cs"));

        Assert.DoesNotContain(
            "Rac1Class749Hostile.TerminalNativeStateFd",
            gameplay,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Rac1Class749Hostile.TerminalNativeStateFe",
            gameplay,
            StringComparison.Ordinal);
        Assert.Contains(
            "_rac1Hostiles.CompleteRecoveredDamageReaction(hostile.Source)",
            gameplay,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rac1GameplayActivityDoesNotDependOnRenderVisibility()
    {
        string gameplay = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1Gameplay.cs"));

        Assert.DoesNotContain(".Root.Visible", gameplay, StringComparison.Ordinal);
        Assert.Contains(
            "_rac1MobyRuntime.Require(node.Source).IsActive",
            gameplay,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerHostResponsibilitiesRemainSplit()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string core = File.ReadAllText(Path.Combine(scripts, "PlayerHost.cs"));

        Assert.True(File.ReadLines(Path.Combine(scripts, "PlayerHost.cs")).Count() < 700);
        Assert.DoesNotContain("private void EnsureRac1CameraInitialized(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void StepRetailDerivedMovement(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private string BuildInputDiagnostics(", core, StringComparison.Ordinal);

        Assert.Contains(
            "EnsureRac1CameraInitialized(",
            File.ReadAllText(Path.Combine(scripts, "PlayerHost.Camera.cs")),
            StringComparison.Ordinal);
        Assert.Contains(
            "StepRetailDerivedMovement(",
            File.ReadAllText(Path.Combine(scripts, "PlayerHost.Rac1Movement.cs")),
            StringComparison.Ordinal);
        Assert.Contains(
            "UpdateAnimationState(",
            File.ReadAllText(Path.Combine(scripts, "PlayerHost.Animation.cs")),
            StringComparison.Ordinal);
        Assert.Contains(
            "BuildInputDiagnostics(",
            File.ReadAllText(Path.Combine(scripts, "PlayerHost.Diagnostics.cs")),
            StringComparison.Ordinal);
    }
}

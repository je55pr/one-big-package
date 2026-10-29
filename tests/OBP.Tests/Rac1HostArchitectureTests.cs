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
            "Rac1PlayerGameplayBinding.cs"));

        Assert.DoesNotContain("Rac1RespawnRequested", player, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1RespawnRequested", gameplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.R", player, StringComparison.Ordinal);
        Assert.Contains(
            "key.Keycode == Key.R && !_host.CanSaveMovementState",
            developmentControls,
            StringComparison.Ordinal);
        Assert.Contains(
            "health.HasPendingRecoveredEnvironmentalRestart",
            gameplay,
            StringComparison.Ordinal);
        Assert.Contains(
            "ports.EnvironmentalRestart()",
            gameplay,
            StringComparison.Ordinal);
        Assert.Contains(
            "() => TryCompleteRac1EnvironmentalRestart(automatic: true)",
            File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryVeldinSmokeCannotUseDevelopmentOrDirectConsequenceShortcuts()
    {
        string smoke = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "diagnostics",
            "Rac1VeldinScenario.cs"));

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
            "Combat.Stage",
            "Combat.SetPresence(",
            "Combat.PlacePlayerFacing(",
            "Actors.Stage",
            "Player.ResetAt(",
            "Player.SetPosition(",
            "Driver.SetKey(DiagnosticPhysicalKey.R",
            "Driver.SetKey(DiagnosticPhysicalKey.F",
        ];

        foreach (string shortcut in forbidden)
            Assert.DoesNotContain(shortcut, smoke, StringComparison.Ordinal);

        string input = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "diagnostics", "Rac1EncounterInput.cs"));
        Assert.Contains("context.Driver.SetAction(action, true)", input, StringComparison.Ordinal);
        Assert.Contains("MovementDiagnosticDriver.Traversal : MovementDiagnosticDriver.Action", input, StringComparison.Ordinal);
        Assert.Contains("Rac1EncounterInput.TapPrimaryAsync(", smoke, StringComparison.Ordinal);
        Assert.Contains(
            "Rac1SmokeDriveToward(",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Rac1EnvironmentalDeathRegression(",
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
            "Rac1GameplayHudPresenter.cs"));

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
        string combat = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1Combat.cs"));

        Assert.DoesNotContain(
            "Rac1Class749Hostile.TerminalNativeStateFd",
            combat,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Rac1Class749Hostile.TerminalNativeStateFe",
            combat,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CompleteRecoveredDamageReaction",
            combat,
            StringComparison.Ordinal);
        Assert.Contains("ApplyRac1EnemyWeaponDamage", combat, StringComparison.Ordinal);
    }

    [Fact]
    public void Rac1GameplayActivityDoesNotDependOnRenderVisibility()
    {
        string hostiles = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "Rac1EnemyHost.Queries.cs"));

        Assert.DoesNotContain(".Root.Visible", hostiles, StringComparison.Ordinal);
        Assert.Contains(
            "TryOwnedRuntime(node, out var runtime) && runtime!.IsActive",
            hostiles,
            StringComparison.Ordinal);
        string enemies = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "Rac1EnemyHost.cs"));
        Assert.DoesNotContain(".Root.Visible", enemies, StringComparison.Ordinal);
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

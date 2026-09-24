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
            "DebugPlayer.cs"));
        string gameplay = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "OBPGame.Rac1Gameplay.cs"));

        Assert.DoesNotContain("Rac1RespawnRequested", player, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1RespawnRequested", gameplay, StringComparison.Ordinal);
        Assert.Contains(
            "key.Keycode == Key.R && !UseRac1Gameplay",
            player,
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
    public void Rac1DebugHudDoesNotAdvertiseDisabledRKeyRespawn()
    {
        string player = File.ReadAllText(Path.Combine(
            RepoPaths.Root,
            "game",
            "scripts",
            "DebugPlayer.cs"));

        Assert.Contains(
            "string developmentControls = UseRac1Gameplay",
            player,
            StringComparison.Ordinal);
        Assert.Contains(
            "? \"mouse / right stick debug camera / F fly / F8 diagnostics",
            player,
            StringComparison.Ordinal);
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
}

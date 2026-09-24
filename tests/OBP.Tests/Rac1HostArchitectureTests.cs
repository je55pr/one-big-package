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

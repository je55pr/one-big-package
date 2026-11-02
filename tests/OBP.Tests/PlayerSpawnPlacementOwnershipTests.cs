namespace OBP.Tests;

public sealed class PlayerSpawnPlacementOwnershipTests
{
    [Fact]
    public void RootOnlyBindsTheSelectedPlayerAfterASeparatePlacementDecision()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string placement = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/GodotPlayerSpawnPlacement.cs"));

        Assert.Contains("GodotPlayerSpawnPlacement.Resolve(world, _worldPlayerEntry", root, StringComparison.Ordinal);
        Assert.Contains("Scripted = scripted", root, StringComparison.Ordinal);
        Assert.Contains("InitialGroundSnapUpwardReach = _worldEntrySnapReach", root, StringComparison.Ordinal);
        Assert.Contains("Rac1CurrentYaw = -placement.Yaw", root, StringComparison.Ordinal);
        Assert.Contains("Position = placement.Position", root, StringComparison.Ordinal);
        Assert.Contains("player.RotateY(placement.Yaw)", root, StringComparison.Ordinal);
        Assert.Contains("PlayerLife.BindPlayer(player)", root, StringComparison.Ordinal);

        Assert.DoesNotContain("nativeStartUsable", root, StringComparison.Ordinal);
        Assert.DoesNotContain("MobySequence showcase", root, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeSpawnSceneAdapter.ToScenePose", root, StringComparison.Ordinal);
        Assert.DoesNotContain("new PlayerHost", placement, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerLife.", placement, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginalPriorityAndCaptureFallbackArePreservedByPlacementOwner()
    {
        string placement = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/GodotPlayerSpawnPlacement.cs"));
        int native = placement.IndexOf("if (nativeStartUsable && preferredStart", StringComparison.Ordinal);
        int animation = placement.IndexOf("if (animFocus &&", StringComparison.Ordinal);
        int crate = placement.IndexOf("if (crateFocus() is", StringComparison.Ordinal);
        int capture = placement.IndexOf("if (scripted && !nativeStartUsable)", StringComparison.Ordinal);

        Assert.True(native >= 0 && native < animation && animation < crate && crate < capture);
        Assert.Contains("stagedStart ?? world.PreferredPlayerStart", placement, StringComparison.Ordinal);
        Assert.Contains("explicitPlayerStart = stagedStart is not null || world.PlayerStart is not null", placement, StringComparison.Ordinal);
        Assert.Contains("b.Max.Y + 6.0", placement, StringComparison.Ordinal);
        Assert.Contains("spawn = focus.Position", placement, StringComparison.Ordinal);
        Assert.Contains("yaw = focus.Yaw", placement, StringComparison.Ordinal);
        Assert.Contains("return new(spawn, yaw)", placement, StringComparison.Ordinal);
    }
}

using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Game;
using OBP.Godot;
using OBP.Runtime;

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
        int animation = placement.IndexOf("if (animFocus)", StringComparison.Ordinal);
        int crate = placement.IndexOf("if (crateFocus() is", StringComparison.Ordinal);
        int capture = placement.IndexOf("if (scripted && !nativeStartUsable)", StringComparison.Ordinal);

        Assert.True(native >= 0 && animation >= 0 && crate >= 0 && capture >= 0 &&
            native < animation && animation < crate && crate < capture);
        Assert.Contains("stagedStart ?? world.PreferredPlayerStart", placement, StringComparison.Ordinal);
        Assert.Contains("explicitPlayerStart = stagedStart is not null || world.PlayerStart is not null", placement, StringComparison.Ordinal);
        Assert.Contains("b.Max.Y + 6.0", placement, StringComparison.Ordinal);
        Assert.Contains("spawn = focus.Position", placement, StringComparison.Ordinal);
        Assert.Contains("yaw = focus.Yaw", placement, StringComparison.Ordinal);
        Assert.Contains("return new(spawn, yaw)", placement, StringComparison.Ordinal);
    }

    private static RuntimeWorld TestWorld() => new(
        ObpSourceGame.Rac1, "test-rac1", 1, "Test Planet", null,
        [], [], 0, [], new ObpBounds(new Vec3(-10, -5, -20), new Vec3(10, 5, 20)),
        null, null);

    [Fact]
    public void StagedStartUsesAuthoredPoseWithGroundClearanceAndReflectedYaw()
    {
        var selected = new RuntimeSpawn(1, 2, 3, 0.5);
        var placement = GodotPlayerSpawnPlacement.Resolve(TestWorld(), selected,
            animFocus: false, scripted: false, crateFocus: () => null, log: _ => { });

        Assert.Equal(new Vector3(-1, 5, 3), placement.Position);
        Assert.Equal(-0.5f, placement.Yaw);
    }

    [Fact]
    public void MissingShipFallsBackToBoundsCentreAndSixUnitsOfClearance()
    {
        var placement = GodotPlayerSpawnPlacement.Resolve(TestWorld(), null,
            animFocus: false, scripted: false, crateFocus: () => null, log: _ => { });

        Assert.Equal(new Vector3(0, 11, 0), placement.Position);
        Assert.Equal(0f, placement.Yaw);
    }

    [Fact]
    public void CrateFocusOverridesPlacementAndCaptureStillFacesWorldCentre()
    {
        var focus = new GodotSelectedPlayerSpawnPose(new Vector3(10, 6, 0), 0.7f);
        var ordinary = GodotPlayerSpawnPlacement.Resolve(TestWorld(), null,
            animFocus: false, scripted: false, crateFocus: () => focus, log: _ => { });
        Assert.Equal(focus, ordinary);

        var capture = GodotPlayerSpawnPlacement.Resolve(TestWorld(), null,
            animFocus: false, scripted: true, crateFocus: () => focus, log: _ => { });
        Assert.Equal(focus.Position, capture.Position);
        Assert.Equal(Mathf.Pi / 2, capture.Yaw, 4);
    }

    private static RuntimeWorld AnimatedWorld(params double[][] frames) => TestWorld() with
    {
        AnimatedMeshes = [new RuntimeAnimatedMesh("moby_i0_t0", "moby", -1, [], [], [], frames, 30f)],
    };

    [Fact]
    public void AnimFocusSpawnFacesTheMeasuredInstanceCentre()
    {
        var logs = new List<string>();
        var placement = GodotPlayerSpawnPlacement.Resolve(AnimatedWorld([0, 0, 0, 4, 2, 6]), null,
            animFocus: true, scripted: false, crateFocus: () => null, log: logs.Add);

        var centre = RuntimeWorldScene.ToScene(2, 1, 3);
        Assert.Equal(centre + new Vector3(0f, 2.5f, 9f), placement.Position);
        Assert.Contains(logs, l => l.Contains("anim-focus spawn near moby_i0"));
    }

    [Theory]
    [MemberData(nameof(InvalidAnimatedFrames))]
    public void AnimFocusWithInvalidFramesReportsFailureAndKeepsBaseSpawn(double[][] frames)
    {
        var logs = new List<string>();
        var baseline = GodotPlayerSpawnPlacement.Resolve(TestWorld(), null,
            animFocus: false, scripted: false, crateFocus: () => null, log: _ => { });
        var placement = GodotPlayerSpawnPlacement.Resolve(AnimatedWorld(frames), null,
            animFocus: true, scripted: false, crateFocus: () => null, log: logs.Add);

        Assert.Equal(baseline, placement);
        Assert.Contains(logs, l => l.Contains("anim-focus spawn not applied"));
        Assert.True(float.IsFinite(placement.Position.X) && float.IsFinite(placement.Position.Y) && float.IsFinite(placement.Position.Z));
    }

    public static TheoryData<double[][]> InvalidAnimatedFrames() => new()
    {
        new double[][] { [] },
        new double[][] { [1, 2] },
        new double[][] { [double.NaN, 0, 0] },
        new double[][] { },
    };
}

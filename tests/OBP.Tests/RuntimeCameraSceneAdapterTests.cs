using OBP.Core.Math;
using OBP.Godot.Camera;
using OBP.Runtime.Camera;

namespace OBP.Tests;

public sealed class RuntimeCameraSceneAdapterTests
{
    [Fact]
    public void RuntimeCameraPose_UnmirrorsEyeAndForwardForGodot()
    {
        var state = new RuntimeCameraState(
            new Vec3(12d, 3d, -7d),
            new Vec3(1d, 0d, 0d),
            0d,
            6d,
            4d);

        var pose = RuntimeCameraSceneAdapter.ToScenePose(state);

        Assert.Equal(-12f, pose.Eye.X);
        Assert.Equal(3f, pose.Eye.Y);
        Assert.Equal(-7f, pose.Eye.Z);
        Assert.Equal(-1f, pose.Forward.X);
        Assert.Equal(0f, pose.Forward.Y);
        Assert.Equal(0f, pose.Forward.Z);
    }

    [Fact]
    public void RuntimeCameraPose_RejectsZeroForwardVector()
    {
        var state = new RuntimeCameraState(
            new Vec3(0d, 0d, 0d),
            new Vec3(0d, 0d, 0d),
            0d,
            6d,
            6d);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RuntimeCameraSceneAdapter.ToScenePose(state));
    }
}

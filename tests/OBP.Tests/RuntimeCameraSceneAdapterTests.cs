using OBP.Core.Math;
using OBP.Godot.Camera;
using OBP.RAC1.Camera;
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
    public void OrdinaryRac1VerticalInput_MovesGodotEyeAlongRecoveredOrbit()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(10d, 20d, 30d);
        camera.Reset(player, 0d);
        Rac1CameraState neutral = camera.Step(new Rac1OrdinaryCameraController.Input(
            player, 0d, 0d, default));

        Rac1CameraState orbit = neutral;
        for (int i = 0; i < 36; i++)
        {
            orbit = camera.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 1d, default));
        }

        var neutralPose = RuntimeCameraSceneAdapter.ToScenePose(neutral.ToRuntimeState());
        var orbitPose = RuntimeCameraSceneAdapter.ToScenePose(orbit.ToRuntimeState());

        Assert.True(orbitPose.Eye.Y < neutralPose.Eye.Y);
        Assert.NotEqual(neutralPose.Eye.X, orbitPose.Eye.X);
        Assert.Equal(neutralPose.Eye.Z, orbitPose.Eye.Z, 5);
        Assert.Equal(0d, orbit.Control.ControlHeading, 12);
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

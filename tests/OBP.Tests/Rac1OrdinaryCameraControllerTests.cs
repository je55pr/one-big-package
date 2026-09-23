using OBP.Core.Math;
using OBP.RAC1.Camera;
using OBP.Runtime.Camera;

namespace OBP.Tests;

public sealed class Rac1OrdinaryCameraControllerTests
{
    [Fact]
    public void NeutralOrdinaryCamera_DoesNotInventAlwaysOnRecenter()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(10d, 20d, 30d);
        camera.Reset(player, 1.25d);

        for (int i = 0; i < 600; i++)
            camera.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 0d, default));

        Assert.Equal(1.25d, camera.ControlHeading, 12);
    }

    [Fact]
    public void FixedAuthorityFraming_ReproducesRetainedEyeWitness()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(
            154.7710418701172d,
            120.58262634277344d,
            29.484375d);
        camera.Reset(player, -2.073779582977295d);

        Rac1CameraState state = camera.Step(new Rac1OrdinaryCameraController.Input(
            player, 0d, 0d, default));

        Assert.Equal(157.66326904296875d, state.Framing.EyeNativeZUp.X, 4);
        Assert.Equal(125.8394775390625d, state.Framing.EyeNativeZUp.Y, 4);
        Assert.InRange(
            state.Framing.EyeNativeZUp.Z,
            31.4796240234375d,
            31.4996240234375d);
    }

    [Fact]
    public void ManualHorizontalInput_HasRecoveredDirectionAndReleaseInertia()
    {
        var left = new Rac1OrdinaryCameraController();
        var player = new Vec3(0d, 0d, 0d);
        left.Reset(player, 0d);
        for (int i = 0; i < 36; i++)
            left.Step(new Rac1OrdinaryCameraController.Input(
                player, -1d, 0d, default));

        double afterPulse = left.ControlHeading;
        for (int i = 0; i < 10; i++)
            left.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 0d, default));

        Assert.True(afterPulse > 0d);
        Assert.True(left.ControlHeading > afterPulse);

        var right = new Rac1OrdinaryCameraController();
        right.Reset(player, 0d);
        for (int i = 0; i < 36; i++)
            right.Step(new Rac1OrdinaryCameraController.Input(
                player, 1d, 0d, default));

        Assert.True(right.ControlHeading < 0d);
    }

    [Fact]
    public void ManualHorizontalPulseAndRelease_MatchesRetainedHeadingTrace()
    {
        const double startHeading = -2.073779582977295d;
        var player = new Vec3(154.7710418701172d, 120.58262634277344d, 29.484375d);

        var left = new Rac1OrdinaryCameraController();
        left.Reset(player, startHeading);
        for (int i = 0; i < 30; i++)
            left.Step(new Rac1OrdinaryCameraController.Input(player, 0d, 0d, default));
        for (int i = 0; i < 36; i++)
            left.Step(new Rac1OrdinaryCameraController.Input(player, -1d, 0d, default));
        for (int i = 0; i < 100; i++)
            left.Step(new Rac1OrdinaryCameraController.Input(player, 0d, 0d, default));
        Assert.InRange(left.ControlHeading, -1.2579d, -1.2559d);

        var right = new Rac1OrdinaryCameraController();
        right.Reset(player, startHeading);
        for (int i = 0; i < 30; i++)
            right.Step(new Rac1OrdinaryCameraController.Input(player, 0d, 0d, default));
        for (int i = 0; i < 36; i++)
            right.Step(new Rac1OrdinaryCameraController.Input(player, 1d, 0d, default));
        for (int i = 0; i < 100; i++)
            right.Step(new Rac1OrdinaryCameraController.Input(player, 0d, 0d, default));
        Assert.InRange(right.ControlHeading, -2.8910d, -2.8890d);
    }

    [Theory]
    [InlineData(0.30d, 0d)]
    [InlineData(-0.30d, 0d)]
    [InlineData(1d, 1d)]
    [InlineData(-1d, -1d)]
    [InlineData(0.65d, 0.5d)]
    public void VerticalManualInput_UsesRecoveredSecondaryDeadzone(
        double input,
        double expected)
    {
        Assert.Equal(
            expected,
            Rac1OrdinaryCameraController.RemapVerticalInput(input),
            7);
    }

    [Fact]
    public void VerticalManualInput_OrbitsEyeAroundAnchorAtConstantRadius()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(10d, 20d, 30d);
        camera.Reset(player, 0.75d);
        Rac1CameraState neutral = camera.Step(new Rac1OrdinaryCameraController.Input(
            player, 0d, 0d, default));

        Rac1CameraState state = neutral;
        for (int i = 0; i < 36; i++)
        {
            state = camera.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 1d, default));
        }

        Vec3 radial = state.Framing.RadialOffsetNativeZUp;
        double radius = Math.Sqrt(
            (radial.X * radial.X) +
            (radial.Y * radial.Y) +
            (radial.Z * radial.Z));
        Assert.Equal(camera.CurrentRadius, radius, 10);
        Assert.Equal(0.75d, camera.ControlHeading, 12);
        Assert.True(radial.Z < -3d);
        Assert.NotEqual(neutral.Framing.EyeNativeZUp.Z, state.Framing.EyeNativeZUp.Z);
        Assert.Equal(
            state.Framing.EyeAnchorNativeZUp + radial,
            state.Framing.EyeNativeZUp);

        double planarRadius = Math.Sqrt(
            (radial.X * radial.X) +
            (radial.Y * radial.Y));
        double lookTargetZ =
            state.Follow.FilteredTargetNativeZUp.Z +
            Rac1OrdinaryCameraController.OrdinaryLookHeight;
        double expectedPitch = Math.Atan2(
            state.Framing.EyeNativeZUp.Z - lookTargetZ,
            planarRadius);
        Assert.Equal(expectedPitch, state.Control.Pitch, 12);
    }

    [Fact]
    public void VerticalManualPulse_NeutralReleaseHoldsPlayerChosenOrbit()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(10d, 20d, 30d);
        camera.Reset(player, 0.75d);

        Rac1CameraState state = null!;
        for (int i = 0; i < 24; i++)
        {
            state = camera.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 0.8d, default));
        }

        double pitchAtRelease = camera.ManualPitchState;
        Vec3 eyeAtRelease = state.Framing.EyeNativeZUp;

        for (int i = 0; i < 180; i++)
        {
            state = camera.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 0d, default));
        }

        Assert.True(pitchAtRelease > 0.4d);
        Assert.Equal(pitchAtRelease, camera.ManualPitchState, 12);
        Assert.Equal(
            -pitchAtRelease * Rac1OrdinaryCameraController.VerticalSpanRadians,
            camera.VerticalOrbitRadians,
            12);
        Assert.Equal(eyeAtRelease, state.Framing.EyeNativeZUp);
    }

    [Fact]
    public void FullVerticalInput_MatchesRecoveredOrbitGeometry()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(0d, 0d, 0d);
        camera.Reset(player, 0d);

        Rac1CameraState state = null!;
        for (int i = 0; i < 36; i++)
        {
            state = camera.Step(new Rac1OrdinaryCameraController.Input(
                player, 0d, 1d, default));
        }

        Assert.Equal(1d, camera.ManualPitchState, 12);
        Assert.Equal(-Rac1OrdinaryCameraController.VerticalSpanRadians,
            camera.VerticalOrbitRadians, 12);
        Assert.Equal(-4.596244089776054d,
            state.Framing.RadialOffsetNativeZUp.X, 10);
        Assert.Equal(0d, state.Framing.RadialOffsetNativeZUp.Y, 12);
        Assert.Equal(-3.8567065614623854d,
            state.Framing.RadialOffsetNativeZUp.Z, 10);
        Assert.Equal(-0.630784939287828d, state.Control.Pitch, 10);
    }

    [Fact]
    public void ObstructionContact_PullsInAndClearLineUsesNativeRelease()
    {
        var camera = new Rac1OrdinaryCameraController();
        var player = new Vec3(0d, 0d, 0d);
        camera.Reset(player, 0d);

        camera.Step(new Rac1OrdinaryCameraController.Input(
            player,
            0d,
            0d,
            new RuntimeCameraObstructionFacts(true, 0d)));

        Assert.Equal(
            Rac1CameraRecurrence.ObstructionPullInStep,
            camera.ObstructionCorrection,
            12);
        Assert.Equal(
            Rac1CameraRecurrence.ObstructionContactTimerTicks,
            camera.ObstructionReleaseTicks);

        double correction = camera.ObstructionCorrection;
        camera.Step(new Rac1OrdinaryCameraController.Input(
            player, 0d, 0d, default));

        Assert.True(camera.ObstructionCorrection < correction);
        Assert.Equal(
            Rac1CameraRecurrence.ObstructionContactTimerTicks - 1,
            camera.ObstructionReleaseTicks);
    }

    [Fact]
    public void RadialFollow_ExposesRecoveredCoefficientSelection()
    {
        Assert.Equal(
            (0.02d, 0.2d),
            Rac1OrdinaryCameraController.RadialCoefficients(
                Rac1CameraRadialFollowProfile.Baseline));
        Assert.Equal(
            (0.04d, 0.3d),
            Rac1OrdinaryCameraController.RadialCoefficients(
                Rac1CameraRadialFollowProfile.Fast));
    }
}

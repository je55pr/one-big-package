using OBP.Core.Math;
using OBP.RAC1.Camera;
using OBP.Runtime.Camera;

namespace OBP.Tests;

public sealed class Rac1CameraContractTests
{
    [Fact]
    public void AuthoritySnapshot_ProjectsNativePoseIntoNeutralYUpState()
    {
        Rac1CameraState state = AuthoritySnapshot();

        RuntimeCameraState runtime = state.ToRuntimeState();

        Assert.Equal(157.66326904296875d, runtime.Eye.X, 12);
        Assert.Equal(31.4896240234375d, runtime.Eye.Y, 12);
        Assert.Equal(125.8394775390625d, runtime.Eye.Z, 12);
        Assert.Equal(-2.073779582977295d, runtime.ControlHeadingRadians, 12);
        Assert.Equal(5.999970436096191d, runtime.PreferredDistance, 12);
        Assert.Equal(runtime.PreferredDistance, runtime.EffectiveDistance, 12);

        double horizontal = Math.Sqrt(
            (runtime.Forward.X * runtime.Forward.X) +
            (runtime.Forward.Z * runtime.Forward.Z));
        Assert.Equal(Math.Cos(0.08400285243988037d), horizontal, 12);
        Assert.Equal(-Math.Sin(0.08400285243988037d), runtime.Forward.Y, 12);
    }

    [Fact]
    public void DirectionalHeadingStage_UsesRecoveredIncrementSignAndRelease()
    {
        var positive = Rac1CameraRecurrence.AdvanceDirectionalHeading(
            0d, 0d, 0x00002000);
        Assert.Equal(Rac1CameraRecurrence.HeadingStepIncrement, positive.HeadingStep, 15);
        Assert.Equal(-Rac1CameraRecurrence.HeadingStepIncrement, positive.ControlHeading, 15);

        var negative = Rac1CameraRecurrence.AdvanceDirectionalHeading(
            0d, 0d, Rac1CameraRecurrence.NegativeDirectionBit);
        Assert.Equal(-Rac1CameraRecurrence.HeadingStepIncrement, negative.HeadingStep, 15);
        Assert.Equal(Rac1CameraRecurrence.HeadingStepIncrement, negative.ControlHeading, 15);

        var released = Rac1CameraRecurrence.AdvanceDirectionalHeading(
            0d, Rac1CameraRecurrence.HeadingStepClamp, 0u);
        Assert.Equal(
            Rac1CameraRecurrence.HeadingStepClamp /
            Rac1CameraRecurrence.HeadingReleaseDivisor,
            released.HeadingStep,
            15);
        Assert.Equal(-released.HeadingStep, released.ControlHeading, 15);
    }

    [Fact]
    public void DirectionalHeadingStage_ClampsRecoveredStepAtFourHundredths()
    {
        var step = new Rac1CameraRecurrence.HeadingResult(0d, 0d);
        for (int i = 0; i < 40; i++)
            step = Rac1CameraRecurrence.AdvanceDirectionalHeading(
                step.ControlHeading, step.HeadingStep, 0x00002000);

        Assert.Equal(Rac1CameraRecurrence.HeadingStepClamp, step.HeadingStep, 15);
    }

    [Fact]
    public void OrdinaryVerticalFollow_UsesRecoveredDampedStep()
    {
        var first = Rac1CameraRecurrence.AdvanceOrdinaryFollowZ(
            current: 0d,
            velocity: 0d,
            target: 1d);
        Assert.Equal(Rac1CameraRecurrence.FollowZAcceleration, first.Velocity, 15);
        Assert.Equal(first.Velocity, first.Value, 15);

        var second = Rac1CameraRecurrence.AdvanceOrdinaryFollowZ(
            first.Value,
            first.Velocity,
            1d);
        double expectedVelocity =
            first.Velocity +
            (Rac1CameraRecurrence.FollowZAcceleration * (1d - first.Value)) -
            (Rac1CameraRecurrence.FollowZDamping * first.Velocity);
        Assert.Equal(expectedVelocity, second.Velocity, 15);
        Assert.Equal(first.Value + expectedVelocity, second.Value, 15);
    }

    [Fact]
    public void DampedStep_ClampsOvershootAndLandsExactlyOnTarget()
    {
        var step = Rac1CameraRecurrence.AdvanceDampedScalar(
            current: 0.99d,
            velocity: 0.2d,
            target: 1d,
            acceleration: Rac1CameraRecurrence.EyeHeightAcceleration,
            damping: Rac1CameraRecurrence.EyeHeightDamping);

        Assert.Equal(0.01d, step.Velocity, 12);
        Assert.Equal(1d, step.Value, 12);
    }

    [Fact]
    public void EyeHeight_UsesRecoveredFinalHeightCoefficients()
    {
        var step = Rac1CameraRecurrence.AdvanceEyeHeight(
            current: 1.5d,
            velocity: 0d,
            preferredHeight: 2d);

        Assert.Equal(
            Rac1CameraRecurrence.EyeHeightAcceleration * 0.5d,
            step.Velocity,
            15);
        Assert.Equal(1.5d + step.Velocity, step.Value, 15);
    }

    [Fact]
    public void ObstructionPullIn_UsesRecoveredStepAndInnerFloor()
    {
        double pulled = Rac1CameraRecurrence.PullInWorkingRadius(6d);
        Assert.Equal(
            6d - Rac1CameraRecurrence.ObstructionPullInStep,
            pulled,
            15);

        double floored = Rac1CameraRecurrence.PullInWorkingRadius(0.21d);
        Assert.Equal(Rac1CameraRecurrence.ObstructionInnerRadiusFloor, floored, 15);
    }

    [Theory]
    [InlineData(0.7500001d, 1)]
    [InlineData(0.75d, 0)]
    [InlineData(0d, 0)]
    [InlineData(-0.75d, 0)]
    [InlineData(-0.7500001d, -1)]
    public void ObstructionSideClassification_UsesStrictRecoveredThresholds(
        double dot,
        int expected)
    {
        Assert.Equal(expected, Rac1CameraRecurrence.ClassifyLateralSide(dot));
        Assert.Equal(
            expected * Rac1CameraRecurrence.ObstructionLateralStepRadians,
            Rac1CameraRecurrence.LateralCorrectionRadians(expected),
            15);
    }

    [Fact]
    public void ClearLineRecovery_IsRecursiveCosineEaseOverRemainingTimer()
    {
        const double correction = 2d;
        var first = Rac1CameraRecurrence.AdvanceClearLineRecovery(
            correction,
            Rac1CameraRecurrence.ObstructionMinimumRadiusTimerTicks);

        double t = 1999d / 2000d;
        double expected =
            correction * 0.5d * (1d - Math.Cos(Math.PI * t));
        Assert.Equal(expected, first.RadialCorrection, 15);
        Assert.Equal(1999, first.ReleaseTimerTicks);

        var second = Rac1CameraRecurrence.AdvanceClearLineRecovery(
            first.RadialCorrection,
            first.ReleaseTimerTicks);

        double secondT = 1998d / 2000d;
        double expectedSecond =
            first.RadialCorrection * 0.5d *
            (1d - Math.Cos(Math.PI * secondT));
        Assert.Equal(expectedSecond, second.RadialCorrection, 15);
        Assert.Equal(1998, second.ReleaseTimerTicks);
    }

    [Fact]
    public void ClearLineRecovery_RejectsBareCorrectionWithoutNativeTimerState()
    {
        var recovered = Rac1CameraRecurrence.AdvanceClearLineRecovery(
            radialCorrection: 2d,
            releaseTimerTicks: 0);

        Assert.Equal(0d, recovered.RadialCorrection, 12);
        Assert.Equal(0, recovered.ReleaseTimerTicks);
    }

    [Fact]
    public void FinalRadiusGuard_EnforcesOnePointFiveAndArmsTwoThousandTicks()
    {
        var result = Rac1CameraRecurrence.EnforceFinalEffectiveRadiusFloor(
            preferredRadius: 6d,
            radialCorrection: 5d,
            releaseTimerTicks: Rac1CameraRecurrence.ObstructionContactTimerTicks);

        Assert.True(result.WasClamped);
        Assert.Equal(4.5d, result.RadialCorrection, 12);
        Assert.Equal(
            Rac1CameraRecurrence.ObstructionMinimumRadiusTimerTicks,
            result.ReleaseTimerTicks);
    }

    [Fact]
    public void NeutralObstructionFacts_DoNotChooseCollisionGeometry()
    {
        var facts = new RuntimeCameraObstructionFacts(true, 0.9d).Validate();
        var probe = new RuntimeCameraObstructionProbe(
            new Vec3(1d, 2d, 3d),
            new Vec3(4d, 5d, 6d));

        Assert.True(facts.HasContact);
        Assert.Equal(1, Rac1CameraRecurrence.ClassifyLateralSide(facts.LateralDot));
        Assert.Equal(0.9d, facts.LateralDot, 12);
        Assert.NotEqual(probe.From, probe.To);
    }

    [Fact]
    public void CameraState_RejectsInvalidPersistentObstructionEncoding()
    {
        Rac1CameraState state = AuthoritySnapshot() with
        {
            Obstruction = new Rac1CameraObstructionState(0d, 0, 2),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Validate());
    }

    private static Rac1CameraState AuthoritySnapshot() =>
        new(
            new Rac1CameraControlState(
                -2.073779582977295d,
                0d,
                0.08400285243988037d),
            new Rac1CameraFollowState(
                new Vec3(
                    154.7710418701172d,
                    120.58262634277344d,
                    29.48434066772461d),
                29.484375d,
                0d),
            new Rac1CameraFramingState(
                new Vec3(
                    157.66326904296875d,
                    125.8394775390625d,
                    31.4896240234375d),
                new Vec3(
                    154.7710418701172d,
                    120.58262634277344d,
                    31.48436737060547d),
                new Vec3(
                    2.8922369480133057d,
                    5.2568511962890625d,
                    0.0052544670179486275d),
                9.059903050001594e-07d,
                5.999970436096191d,
                4.434585889612208e-07d,
                Rac1CameraRecurrence.ProfileTransitionAcceleration,
                1.5000041723251343d,
                -4.7683720083568915e-08d,
                1.999987244606018d,
                1.0728837906981425e-07d,
                1.9999926090240479d,
                1.108646472403052e-07d,
                Rac1CameraRecurrence.ProfileTransitionAcceleration),
            new Rac1CameraObstructionState(
                0d,
                0,
                0));
}

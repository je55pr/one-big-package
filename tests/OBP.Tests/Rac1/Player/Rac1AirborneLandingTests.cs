using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneLandingTests
{
    private static readonly Rac1AirborneLandingSession.StepInput Neutral = new(
        0, 0, false, 0, new(0.07, 0, 0));

    [Fact]
    public void LandingAdmissionRequiresSupportedPostApexMotionAndPreservesEarlyBoostGuard()
    {
        var input = new Rac1AirborneLandingSession.AdmissionInput(7, 9, 5, 0, true, 10);
        Assert.True(Rac1AirborneLandingSession.CanBegin(input));
        Assert.False(Rac1AirborneLandingSession.CanBegin(input with { NativeActionAge = 5 }));
        Assert.False(Rac1AirborneLandingSession.CanBegin(input with { HasApexed = false }));
        Assert.False(Rac1AirborneLandingSession.CanBegin(input with { UnsupportedUpdates = 1 }));
        Assert.False(Rac1AirborneLandingSession.CanBegin(input with { NativeAction = 0x0e, AuthoredAnimationTime = 34.999f }));
        Assert.True(Rac1AirborneLandingSession.CanBegin(input with { NativeAction = 0x0e, AuthoredAnimationTime = 35 }));
    }

    [Fact]
    public void NeutralFlipRetainsItsLandingActionAndScalesRequestWithoutMultiplyingStoredScalar()
    {
        var session = new Rac1AirborneLandingSession(0x0b, 0.07f);
        var first = session.Step(Neutral);
        Assert.Equal(0.07f, first.Request.X);
        Assert.Equal(0.07f, first.ControlledScalar);
        Assert.Null(first.GroundHandoff);
        var second = session.Step(Neutral);
        Assert.Equal(0.07f * 0.4f, second.Request.X);
        Assert.Equal(0.07f - (11f * (1f / 3600f)), second.ControlledScalar);
        Assert.Null(second.GroundHandoff);
        var third = session.Step(Neutral);
        Assert.Equal(second.ControlledScalar * 0.4f, third.Request.X);
        Assert.Null(third.GroundHandoff);
        for (int counter = 5; counter <= 12; counter++)
            Assert.Null(session.Step(Neutral).GroundHandoff);
        var timedOut = session.Step(Neutral).GroundHandoff!.Value;
        Assert.Equal(0, timedOut.NextAction);
        Assert.Null(timedOut.Sequence);
        Assert.Null(timedOut.EntryFrame);
        Assert.Equal(0, timedOut.BlendTicks);
        Assert.True(session.Finished);
    }

    [Fact]
    public void LandingCrouchRunAndAnimationExitHaveStrictNativePriorityAndTiming()
    {
        static Rac1AirborneLandingSession.Handoff Exit(Rac1AirborneLandingSession.StepInput input)
        {
            var session = new Rac1AirborneLandingSession(7, 0.07f);
            return session.Step(input).GroundHandoff!.Value;
        }
        var crouch = Exit(Neutral with { CrouchHeld = true, ConditionedMagnitude = 1 });
        Assert.Equal(4, crouch.NextAction);
        Assert.Equal(13, crouch.Sequence);
        Assert.Equal(2, crouch.EntryFrame);
        Assert.Equal(-2, crouch.BlendTicks);
        var run = Exit(Neutral with { ConditionedMagnitude = MathF.BitIncrement(0.3f) });
        Assert.Equal(2, run.NextAction);
        Assert.Equal(4, run.Sequence);
        Assert.Equal(8, run.BlendTicks);
        var completed = Exit(Neutral with { NativeAnimationExitRequested = true });
        Assert.Equal(0, completed.NextAction);
        Assert.Equal(0, completed.Sequence);
        Assert.Equal(0, completed.EntryFrame);
        Assert.Equal(-2, completed.BlendTicks);
        var boundary = new Rac1AirborneLandingSession(7, 0.07f);
        Assert.Null(boundary.Step(Neutral with { ConditionedMagnitude = 0.3f }).GroundHandoff);
        Assert.Null(boundary.Step(Neutral with { ConditionedMagnitude = 1, ControlLocked = true }).GroundHandoff);
    }

    [Fact]
    public void ThrusterStretchTransfersClampedContactCarryRatherThanFullAirScalar()
    {
        var session = new Rac1AirborneLandingSession(0x10, 0.19f);
        var input = Neutral with { ContactMotion = new(0.19, 0, 0) };
        var result = session.Step(input);
        Assert.Equal(0.19f, result.Request.X);
        Assert.Equal(0.19f, result.ControlledScalar);
        var handoff = result.GroundHandoff!.Value;
        Assert.Equal(3, handoff.NextAction);
        Assert.Equal(5, handoff.Sequence);
        Assert.Equal(-1, handoff.BlendTicks);
        Assert.Equal((double)Rac1AirborneLandingSession.StopCarryLimit, handoff.ContactCarry.X, 7);
    }

    [Fact]
    public void ForwardMovingLandingUsesStopOnlyAfterCounterThreeAndHeliStopsImmediately()
    {
        var input = Neutral with { ForwardRequestStep = MathF.BitIncrement(3f * (1f / 60f)) };
        var flip = new Rac1AirborneLandingSession(0x0b, 0.07f);
        for (int counter = 1; counter <= 2; counter++)
            Assert.Null(flip.Step(input).GroundHandoff);
        Assert.Equal(3, flip.Step(input).GroundHandoff!.Value.NextAction);
        var heli = new Rac1AirborneLandingSession(0x0a, 0.15f);
        var stopped = heli.Step(input with { ConditionedMagnitude = 1, CrouchHeld = true });
        Assert.Equal(3, stopped.GroundHandoff!.Value.NextAction);
        Assert.Equal(-2, stopped.GroundHandoff.Value.BlendTicks);
        Assert.Equal(0.07 * (double)0.8f, stopped.GroundHandoff.Value.ContactCarry.X);
    }

    [Fact]
    public void LandingCarryBrakesSeparatelyAndIsNotAddedAgainAsControlledScalar()
    {
        var session = new Rac1AirborneLandingSession(7, 0, new(0.03f, 0.04f));
        var result = session.Step(Neutral);
        float originalLength = MathF.Sqrt((0.03f * 0.03f) + (0.04f * 0.04f));
        float expected = originalLength - (11f * (1f / 3600f));
        Assert.Equal(expected, result.Carry.Magnitude, 6);
        Assert.Equal(result.Carry, result.Request);
        Assert.Equal(0, result.ControlledScalar);
    }

    [Fact]
    public void IdleAndStopOwnContactCarryWithTheirSeparateSourceBrakes()
    {
        var idle = new Rac1AirborneLandingSession(7, 0.07f);
        var exit = idle.Step(Neutral with { NativeAnimationExitRequested = true });
        Assert.Equal(Neutral.ContactMotion, exit.GroundHandoff!.Value.ContactCarry);
        var carry = new Rac1AirbornePlanarMotion.PlanarStep(0.03f, 0);
        Assert.Equal(0.03f - Rac1AirborneLandingSession.IdleCarryDeceleration,
            Rac1AirborneLandingSession.BrakeGroundCarry(0, carry).X);
        Assert.Equal(0.03f - Rac1AirborneLandingSession.StopCarryDeceleration,
            Rac1AirborneLandingSession.BrakeGroundCarry(3, carry).X);
        Assert.Equal(0.03f - Rac1AirborneLandingSession.StopCarryDeceleration * 0.5f,
            Rac1AirborneLandingSession.BrakeGroundCarry(3, carry, -0.5f).X);
        Assert.Equal(default, Rac1AirborneLandingSession.BrakeGroundCarry(0, new(0.0009f, 0)));
    }
}

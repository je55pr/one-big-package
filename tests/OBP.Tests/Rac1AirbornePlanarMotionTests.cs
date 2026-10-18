using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirbornePlanarMotionTests
{
    [Fact]
    public void DirectionChangesUseAngleDependentConvergenceRatherThanConstantAcceleration()
    {
        var aligned = Rac1AirbornePlanarMotion.Converge(new(0.05f, 0), new(0.1f, 0), 0.005f);
        var opposed = Rac1AirbornePlanarMotion.Converge(new(0.05f, 0), new(-0.1f, 0), 0.005f);
        var neutral = Rac1AirbornePlanarMotion.Converge(new(0.05f, 0), new(0, 0), 0.005f);
        Assert.Equal(0.055f, aligned.X, 6);
        Assert.Equal(0.035f, opposed.X, 6);
        Assert.Equal(0.0425f, neutral.X, 6);
        Assert.Equal(new(0.05f, 0), Rac1AirbornePlanarMotion.Converge(new(0.05f, 0), new(0.05f, 0), 1));
    }

    [Fact]
    public void NeutralReleaseAndForwardAccelerationReproduceEarlierBoundedWitnessRates()
    {
        float neutralAmount = Rac1AirbornePlanarMotion.OrdinaryBaseAmount(0, false);
        var neutral = Rac1AirbornePlanarMotion.Converge(new(0.05f, 0), new(0, 0), neutralAmount);
        Assert.Equal(1f / 1200f, 0.05f - neutral.X, 7);
        float fullAmount = Rac1AirbornePlanarMotion.OrdinaryBaseAmount(0.095f, false);
        var active = Rac1AirbornePlanarMotion.Converge(new(0.05f, 0), new(0.095f, 0), fullAmount);
        Assert.Equal(1f / 180f, active.X - 0.05f, 7);
        Assert.Equal(6f * (1f / 3600f), Rac1AirbornePlanarMotion.OrdinaryBaseAmount(0, true));
    }

    [Fact]
    public void PackHighAndAerialBoostTargetsHaveDifferentCaps()
    {
        Assert.Equal(1.9f * (1f / 60f), Rac1AirbornePlanarMotion.MaximumTargetStep(0x0d));
        Assert.Equal(3.3f * (1f / 60f), Rac1AirbornePlanarMotion.MaximumTargetStep(0x0f));
        Assert.Equal(3.5f * (1f / 60f), Rac1AirbornePlanarMotion.MaximumTargetStep(0x0e));
    }

    [Fact]
    public void ThrusterLongLocksHeadingAndChangesTargetAfterNativeAge54()
    {
        var fast = Rac1AirbornePlanarMotion.StepThrusterLong(0.18f, 54, 0, 0);
        var slow = Rac1AirbornePlanarMotion.StepThrusterLong(0.18f, 55, 0, 0);
        Assert.True(fast.X > 0.18f);
        Assert.Equal(0.18f - (45f * (1f / 3600f)), slow.X);
        Assert.Equal(0, fast.Y);
        var high = Rac1AirbornePlanarMotion.StepThrusterLong(0.12f, 1, 2, 0);
        Assert.Equal(0.12f - (45f * (1f / 3600f)), high.X);
    }

    [Fact]
    public void FlipUsesEntryInputAndOlderInputWithSineOfTheirAbsoluteAngle()
    {
        var aligned = new Rac1AirborneFlipPlanarSession(0, 0, 0.09f, 0);
        var sideways = new Rac1AirborneFlipPlanarSession(0, MathF.PI / 2, 0.09f, 0);
        var a = aligned.Step(false, 20);
        var b = sideways.Step(false, 20);
        Assert.Equal(a.X, b.X, 6);
        Assert.Equal(0, a.Y);
        Assert.Equal(0.09f, b.Y, 6);
        var oppositeSide = new Rac1AirborneFlipPlanarSession(0, -MathF.PI / 2, 0.09f, 1);
        Assert.Equal(-0.09f, oppositeSide.Step(false, 20).Y, 6);
        Assert.Equal(0, aligned.FacingTargetYaw);
    }

    [Fact]
    public void FlipControlledScalarIsIndependentOfDirectionalRequestAndBackKeepsProjectionField()
    {
        var stationary = new Rac1AirborneFlipPlanarSession(MathF.PI / 2, 0, 0, 0);
        Assert.True(stationary.Step(false, 20).Magnitude > 0);
        Assert.Equal(0, stationary.ControlledScalar);
        var running = new Rac1AirborneFlipPlanarSession(MathF.PI / 2, 0, 0.09f, 0);
        Assert.True(running.Step(false, 20).Magnitude > running.ControlledScalar);
        Assert.Equal(0.09f, running.ControlledScalar);
        var back = new Rac1AirborneFlipPlanarSession(MathF.PI, MathF.PI / 2, 0.09f, 3);
        var request = back.Step(false, 20);
        Assert.Equal(0, back.ControlledScalar);
        Assert.Equal(0.09f, request.Y, 6); // P454 remains available even though P194 is zero.
    }

    [Fact]
    public void FlipBrakesAfterApexOnlyWhenPredictedLandingIsStrictlyLessThanTwelveUpdates()
    {
        static Rac1AirborneFlipPlanarSession Warm()
        {
            var flip = new Rac1AirborneFlipPlanarSession(0, 0, 0, 0);
            for (int tick = 0; tick < 8; tick++)
                flip.Step(false, 20);
            return flip;
        }
        float prior = Warm().Step(false, 20).X - (8f * (1f / 3600f));
        Assert.Equal(prior - (8f * (1f / 3600f)), Warm().Step(true, 11).X, 6);
        Assert.Equal(prior + (8f * (1f / 3600f)), Warm().Step(true, 12).X, 6);
        Assert.Equal(prior + (8f * (1f / 3600f)), Warm().Step(true, 13).X, 6);
        Assert.Equal(prior + (8f * (1f / 3600f)), Warm().Step(false, 11).X, 6);
    }
}

using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirbornePowerSlamTests
{
    [Fact]
    public void PowerSlamRequiresThrusterPackRecentCrouchAndItsNativeAirborneWindow()
    {
        var input = new Rac1AirbornePowerSlamSession.AdmissionInput(7, 14, 5, false, 0, 0, true, 3, false);
        Assert.True(Rac1AirbornePowerSlamSession.CanEnter(input));
        Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { NativeActionAge = 13 }));
        Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { ConditionedMagnitude = 0.3f }));
        Assert.True(Rac1AirbornePowerSlamSession.CanEnter(input with { ConditionedMagnitude = MathF.BitDecrement(0.3f) }));
        Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { CrouchPressBufferedNineUpdates = false }));
        Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { ActivePackId = 2 }));
        Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { PackDisabled = true }));
        Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { HasApexed = true, PredictedLandingTicks = 10 }));
        Assert.True(Rac1AirbornePowerSlamSession.CanEnter(input with { HasApexed = true, PredictedLandingTicks = 11 }));
        foreach (int action in new[] { 7, 9, 0x0d, 0x0e, 0x0f })
            Assert.True(Rac1AirbornePowerSlamSession.CanEnter(input with { CurrentAction = action }));
        foreach (int action in new[] { 6, 8, 0x0a, 0x0b, 0x10, 0x11, 0x22 })
            Assert.False(Rac1AirbornePowerSlamSession.CanEnter(input with { CurrentAction = action }));
    }

    [Fact]
    public void EntryPreservesRequestThenBrakesPlanarAndSeparatesRiseHoldDiveBoundaries()
    {
        var session = new Rac1AirbornePowerSlamSession(0.12f, 0, -0.02f, 0.12f);
        Assert.Equal(-0.02f, session.VerticalRequest);
        var rising = session.Step(new(11, 1));
        Assert.Equal(-0.02f + Rac1AirbornePowerSlamSession.RiseAccelerationPerUpdate, rising.Vertical);
        Assert.Equal(0.12f - Rac1AirbornePowerSlamSession.PlanarDecayPerUpdate, rising.PlanarX);
        Assert.Equal(rising.Vertical, session.Step(new(12, 1)).Vertical);
        Assert.Equal(rising.Vertical, session.Step(new(33, 1)).Vertical);
        var dive = session.Step(new(34, 1));
        Assert.Equal(rising.Vertical - Rac1AirbornePowerSlamSession.DiveAccelerationPerUpdate, dive.Vertical);
        Assert.True(dive.IsDiving);
        Assert.True(dive.RequestsDiveDamageProbe);
        Assert.False(dive.JustImpacted);
        Assert.False(dive.Finished);
    }

    [Fact]
    public void GroundImpactIsNotTheNativeAnimationExitGateAndDiveHasNoOrdinaryTerminalCap()
    {
        var session = new Rac1AirbornePowerSlamSession(0, 0, -1, 0);
        var impact = session.Step(new(34, 0));
        Assert.True(impact.JustImpacted);
        Assert.Equal(1, impact.ImpactUpdates);
        Assert.False(impact.RequestsDiveDamageProbe);
        Assert.False(impact.Finished);
        var next = session.Step(new(35, 0));
        Assert.False(next.JustImpacted);
        Assert.Equal(2, next.ImpactUpdates);
        Assert.True(next.Vertical < -1);
        var exited = session.Step(new(36, 0, NativeAnimationExitRequested: true));
        Assert.True(exited.Finished);
        Assert.Equal(next.Vertical, exited.Vertical);
    }

    [Fact]
    public void InitialRiseCanDecelerateFasterAscentWhileTheScalarNeverBecomesNegative()
    {
        var session = new Rac1AirbornePowerSlamSession(0, 0.001f, 0.2f, 0.001f);
        var step = session.Step(new(0, 1));
        Assert.Equal(0.2f - Rac1AirbornePowerSlamSession.RiseAccelerationPerUpdate, step.Vertical);
        Assert.Equal(0, step.PlanarY);
        Assert.Equal(0, session.PlanarScalar);
    }
}

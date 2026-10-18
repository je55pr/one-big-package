using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneBoostTests
{
    [Fact]
    public void RecentSecondCrossHasARecoveredBoostRouteWithoutInventingAFlip()
    {
        var input = new Rac1AirborneBoostSession.AdmissionInput(7, 15, true, true, false, false, false, 2, false, 0.1f);
        Assert.True(Rac1AirborneBoostSession.CanEnter(input));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { NativeActionAge = 14 }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { BoostAllowed = false }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { AnimationGuard = true }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { LandingStarted = true }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { CrossBuffered = false }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { CurrentAction = 0x0e }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { SpecialSurfaceFlag = true, GroundClearance = 0.39f }));
        Assert.True(Rac1AirborneBoostSession.CanEnter(input with { SpecialSurfaceFlag = true, GroundClearance = 0.4f }));
        Assert.False(Rac1AirborneBoostSession.CanEnter(input with { HasApexed = true, CurrentVerticalStep = 0.2f }));
        Assert.True(Rac1AirborneBoostSession.CanEnter(input with { HasApexed = true, CurrentVerticalStep = 0 }));
        Assert.Equal(2, Rac1AirborneBoostSession.CrossBufferWindow(0));
        Assert.Equal(11, Rac1AirborneBoostSession.CrossBufferWindow(15));
        Assert.Equal(30, Rac1AirborneBoostSession.CrossBufferWindow(100));
    }

    [Fact]
    public void BoostApproachesTheHeightDependentTargetForEightUpdatesThenOnlyGravity()
    {
        var session = new Rac1AirborneBoostSession(0);
        float prior = 0;
        for (int age = 0; age < 8; age++)
            prior = session.Step(new(age, prior, 0, 0, false, false));
        Assert.Equal((7f * (1f / 60f)) - Rac1AirborneBoostSession.GravityPerUpdate, prior);
        float next = session.Step(new(8, prior, 0, 0, false, false));
        Assert.Equal(prior - Rac1AirborneBoostSession.GravityPerUpdate, next);
    }

    [Fact]
    public void OriginalLaunchRiseAndThrusterEquipmentChangeBoostTarget()
    {
        static float Settle(float rise, int pack, bool available = true)
        {
            var session = new Rac1AirborneBoostSession(0);
            float prior = 0;
            for (int age = 0; age < 8; age++)
                prior = session.Step(new(age, prior, rise, pack, available, false));
            return prior;
        }
        float gravity = Rac1AirborneBoostSession.GravityPerUpdate;
        float basis = 7f * (1f / 60f);
        Assert.Equal(basis - gravity, Settle(1.7f, 2));
        Assert.Equal((basis * 0.7f) - gravity, Settle(MathF.BitIncrement(1.7f), 2));
        Assert.Equal((basis * 0.45f) - gravity, Settle(MathF.BitIncrement(2.1f), 2));
        Assert.Equal((basis * 1.25f) - gravity, Settle(2.1f, 3));
        Assert.Equal(((basis * 0.7f) * 1.25f) - gravity, Settle(MathF.BitIncrement(2.1f), 3));
        Assert.Equal(((basis * 0.47f) * 1.25f) - gravity, Settle(MathF.BitIncrement(2.7f), 3));
        Assert.Equal(((basis * 0.45f) * 1.25f) - gravity, Settle(2.5f, 3, false));
    }

    [Fact]
    public void BoostNeverClampsExistingFasterAscentToItsTargetAndCeilingCancelsFurtherBoost()
    {
        var session = new Rac1AirborneBoostSession(0.2f);
        Assert.Equal(0.2f - Rac1AirborneBoostSession.GravityPerUpdate,
            session.Step(new(0, 0.2f, 0, 0, false, false)));
        float hit = session.Step(new(1, 0, 0, 0, false, false, HitCeiling: true));
        Assert.True(hit < 0);
        float falling = session.Step(new(2, hit, 0, 0, false, false));
        Assert.True(falling < hit);
        Assert.Equal(0, session.Step(new(3, 0, 0, 0, false, false, Landed: true)));
        Assert.True(session.Finished);
    }
}

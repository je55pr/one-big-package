using OBP.Game;

namespace OBP.Tests;

public sealed class Rac2WorldLeaseClockTests
{
    [Fact]
    public void SourceInvalidationLatchesAndCannotBeRestoredByReattachingTheSource()
    {
        bool sourceValid = true;
        int checkedSource = 0;
        var lease = new Rac2WorldLeaseClock(() => true, () =>
        {
            checkedSource++;
            return sourceValid;
        }, sourceRequired: true);

        Assert.True(lease.Current);
        sourceValid = false;
        Assert.False(lease.Current);
        sourceValid = true;
        Assert.False(lease.Current);
        Assert.Equal(2, checkedSource);
    }

    [Fact]
    public void UnrelatedWorldDoesNotRequireRac2SourceAndRetirementIsPermanent()
    {
        var lease = new Rac2WorldLeaseClock(() => true,
            () => throw new InvalidOperationException("Non-R&C2 world probed R&C2 source."),
            sourceRequired: false);

        Assert.True(lease.Current);
        lease.Retire();
        Assert.False(lease.Current);
        lease.Retire();
    }

    [Fact]
    public void NativeTicksPreserveFractionAndDoNotDoubleTickOrdinaryActors()
    {
        bool ordinary = true;
        var lease = new Rac2WorldLeaseClock(() => true, () => true, sourceRequired: true);
        var trace = new List<string>();
        Action lift = () => trace.Add("lift");
        Action door = () => trace.Add("door");
        Action hostiles = () => trace.Add("hostiles");
        lease.Advance(0.5 / 60, () => ordinary, lift, door, hostiles);
        Assert.Empty(trace);

        lease.Advance(2.5 / 60 + 1e-8, () => ordinary, lift, door, hostiles);
        Assert.Equal(new[] { "lift", "door", "lift", "door", "lift", "door" }, trace);
        ordinary = false;
        lease.Advance(1d / 60 + 1e-8, () => ordinary, lift, door, hostiles);
        Assert.Equal(new[] { "lift", "door", "hostiles" }, trace.TakeLast(3));
    }

    [Fact]
    public void CatchUpIsBoundedToTwelveTicksAndRetainsRemainingFraction()
    {
        var lease = new Rac2WorldLeaseClock(() => true, () => true, sourceRequired: false);
        int steps = 0;
        lease.Advance(10, () => true, () => steps++, () => { }, () => throw new Exception("No ordinary hostile tick."));
        Assert.Equal(12, steps);
        lease.Advance(0, () => true, () => steps++, () => { }, () => { });
        Assert.InRange(steps, 14, 15); // Binary rounding can leave a fraction just short of one step.
        lease.Advance(1e-5, () => true, () => steps++, () => { }, () => { });
        Assert.Equal(15, steps);
    }

    [Fact]
    public void InvalidatedOrReplacedWorldCannotTickOrResumeAnOldActorClock()
    {
        bool oldCurrent = true;
        bool oldSource = true;
        var oldLease = new Rac2WorldLeaseClock(() => oldCurrent, () => oldSource, sourceRequired: true);
        int oldTicks = 0;
        oldLease.Advance(1d / 60, () => false, () => oldTicks++, () => { }, () => { });
        Assert.Equal(1, oldTicks);

        oldCurrent = false;
        var newLease = new Rac2WorldLeaseClock(() => true, () => true, sourceRequired: true);
        int newTicks = 0;
        oldLease.Advance(1d / 60, () => false, () => oldTicks++, () => { }, () => { });
        newLease.Advance(1d / 60, () => false, () => newTicks++, () => { }, () => { });
        Assert.Equal(1, oldTicks);
        Assert.Equal(1, newTicks);

        oldCurrent = true;
        oldSource = false;
        Assert.False(oldLease.Current);
        oldSource = true;
        oldLease.Advance(1d / 60, () => false, () => oldTicks++, () => { }, () => { });
        Assert.Equal(1, oldTicks);
    }

    [Fact]
    public void LeaseRetirementStopsPendingTicksAndAllDelegatedGameplay()
    {
        var lease = new Rac2WorldLeaseClock(() => true, () => true, sourceRequired: true);
        lease.Advance(0.5 / 60, () => true, () => { }, () => { }, () => { });
        lease.Retire();
        Assert.False(lease.Current);
        lease.Advance(2d / 60, () => throw new Exception("Retired clock was evaluated."),
            () => throw new Exception("Retired lift tick."),
            () => throw new Exception("Retired door tick."),
            () => throw new Exception("Retired hostile tick."));
    }
}

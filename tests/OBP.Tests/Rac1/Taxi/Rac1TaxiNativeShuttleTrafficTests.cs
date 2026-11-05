using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeShuttleTrafficTests
{
    private static readonly Rac1TaxiPoint Source = new(0, 0, 0);
    private static readonly Rac1TaxiPoint Far = new(10, 0, 0);

    private static Rac1TaxiNativeShuttleTraffic NewTraffic(int initialSide)
    {
        var dock = new Rac1TaxiNativeShuttleDock(
            [Far, new(5, 0, 0), Source],
            [Source, new(5, 0, 0), Far],
            initialSide, -1);
        return new Rac1TaxiNativeShuttleTraffic(dock);
    }

    private static void FinishReposition(Rac1TaxiNativeShuttleTraffic traffic)
    {
        for (int i = 0; i < 36000 && traffic.NativeState == 3; i++)
            traffic.Advance(1d / 60);
        Assert.Equal(1, traffic.NativeState);
    }

    [Fact]
    public void NativeSideZeroStartsAtFarDockAndRepositionsTowardSource()
    {
        var traffic = NewTraffic(initialSide: 0);
        Assert.Equal(Far, traffic.Position);
        Assert.Equal(0, traffic.CurrentSide);
        Assert.False(traffic.TryStartUnoccupiedReposition(Far));
        Assert.True(traffic.TryStartUnoccupiedReposition(Source));
        Assert.Equal(3, traffic.NativeState);
        Assert.Equal(0, traffic.CurrentSide);
        Assert.Equal(Far, traffic.Position);
        Assert.False(traffic.TryStartUnoccupiedReposition(Far));
        traffic.Advance(1d / 60);
        Assert.NotEqual(Far, traffic.Position);
        FinishReposition(traffic);
        Assert.Equal(Source, traffic.Position);
        Assert.Equal(.25, traffic.FocusDelayRemaining, 10);
        traffic.Advance(14d / 60);
        Assert.True(traffic.FocusDelayRemaining > 0);
        traffic.Advance(1d / 60);
        Assert.Equal(0, traffic.FocusDelayRemaining, 10);
    }

    [Fact]
    public void NativeSideOneMovesToFarDockOnlyWhenRatchetIsThere()
    {
        var traffic = NewTraffic(initialSide: 1);
        Assert.Equal(Source, traffic.Position);
        Assert.False(traffic.TryStartUnoccupiedReposition(Source));
        Assert.True(traffic.TryStartUnoccupiedReposition(Far));
        Assert.Equal(1, traffic.CurrentSide);
        Assert.Equal(3, traffic.NativeState);
        FinishReposition(traffic);
        Assert.Equal(Far, traffic.Position);
        Assert.True(traffic.TryStartUnoccupiedReposition(Source));
        Assert.Equal(0, traffic.CurrentSide);
        FinishReposition(traffic);
        Assert.Equal(Source, traffic.Position);
    }

    [Fact]
    public void NativeRouteAndAngularIndependentMotionAreHostFrameRateInvariant()
    {
        var slow = NewTraffic(0);
        var fast = NewTraffic(0);
        var native = NewTraffic(0);
        Assert.True(slow.TryStartUnoccupiedReposition(Source));
        Assert.True(fast.TryStartUnoccupiedReposition(Source));
        Assert.True(native.TryStartUnoccupiedReposition(Source));
        int arrivedAt = 0;
        for (int i = 0; i < 36000; i++)
        {
            native.Advance(1d / 60);
            fast.Advance(1d / 120);
            fast.Advance(1d / 120);
            if (i % 2 == 0) slow.Advance(1d / 30);
            if (i % 2 != 0)
            {
                Assert.InRange(native.Position.DistanceSquared(slow.Position), 0, 1e-10);
                Assert.InRange(native.Position.DistanceSquared(fast.Position), 0, 1e-10);
                Assert.Equal(native.NativeState, slow.NativeState);
                Assert.Equal(native.NativeState, fast.NativeState);
                Assert.Equal(native.CurrentSide, slow.CurrentSide);
                Assert.Equal(native.CurrentSide, fast.CurrentSide);
                if (native.NativeState == Rac1TaxiNativeShuttleTraffic.IdleState)
                {
                    arrivedAt = i + 1;
                    break;
                }
            }
            if (i == 59)
                // First sixty native steps travel .00125*(1+...+60) = 2.2875 units.
                Assert.Equal(7.7125, native.Position.X, 10);
        }
        Assert.InRange(arrivedAt, 60, 36000);
        Assert.Equal(Source, native.Position);
        Assert.Equal(Source, slow.Position);
        Assert.Equal(Source, fast.Position);
        Assert.Equal(Rac1TaxiNativeShuttleTraffic.IdleState, native.NativeState);
        Assert.NotEqual(Far, native.Position);
        Assert.InRange(native.Position.DistanceSquared(slow.Position), 0, 1e-10);
        Assert.InRange(native.Position.DistanceSquared(fast.Position), 0, 1e-10);
        Assert.Equal(native.NativeState, slow.NativeState);
        Assert.Equal(native.NativeState, fast.NativeState);
    }

    [Fact]
    public void InvalidHeroAndTimeNeverAdmitOrAdvanceTheShuttle()
    {
        var traffic = NewTraffic(1);
        Assert.False(traffic.TryStartUnoccupiedReposition(
            new Rac1TaxiPoint(double.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => traffic.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            traffic.Advance(double.PositiveInfinity));
        Assert.Equal(Source, traffic.Position);
        Assert.Equal(1, traffic.NativeState);
    }

    [Fact]
    public void ResetClearsMotionAndReturnsToTheAuthoredInitialSide()
    {
        var traffic = NewTraffic(0);
        Assert.True(traffic.TryStartUnoccupiedReposition(Source));
        traffic.Advance(1);
        traffic.Reset();
        Assert.Equal(Far, traffic.Position);
        Assert.Equal(0, traffic.CurrentSide);
        Assert.Equal(1, traffic.NativeState);
        Assert.Equal(0, traffic.FocusDelayRemaining);
    }
}

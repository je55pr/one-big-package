using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1LocomotionAnimationClockTests
{
    [Fact]
    public void EntryTargetsFrameEightAndBlendTakesEightUpdatesRegardlessOfMultiplier()
    {
        var clock = Clock();
        Assert.Equal(new(3, 8, 8, 0, true), clock.Current);
        for (int update = 1; update < 8; update++)
        {
            clock.Step(0.03f, true);
            Assert.Equal(update / 8f, clock.Current.Fraction);
            Assert.True(clock.Current.IsBlending);
            Assert.Equal(4f, clock.PlaybackMultiplier);
        }
        clock.Step(0.03f, true);
        Assert.Equal(new(3, 8, 9, 0, false), clock.Current);
    }

    [Fact]
    public void RateChangeAppliesToTheFollowingUpdateWithoutRestartingPhase()
    {
        var clock = SettledWalk();
        clock.Step(0f, true);
        Assert.Equal(8, clock.Current.CurrentFrame);
        Assert.Equal(0.58875f, clock.Current.Fraction, 6);
        Assert.Equal(0.6f, clock.PlaybackMultiplier);
        clock.Step(0f, true);
        Assert.Equal(0.73875f, clock.Current.Fraction, 6);
    }

    [Fact]
    public void UnsupportedOrdinaryEdgeAdvancesButRetainsSequenceAndPreviousRate()
    {
        var clock = SettledWalk();
        float rate = clock.PlaybackMultiplier;
        for (int update = 0; update < 16; update++)
            clock.Step(0.095f, helperAdmitted: false);
        Assert.Equal(3, clock.Current.Sequence);
        Assert.Equal(rate, clock.PlaybackMultiplier);
        Assert.NotEqual(8, clock.Current.CurrentFrame);
        Assert.Equal(24, clock.SubstateAge);
    }

    [Fact]
    public void BothTransitionsMapTheAdvancedNextFrameAndResetAgeAndBlend()
    {
        var clock = SettledWalk();
        var advanced = clock.Step(0.095f, true);
        Assert.Equal(4, clock.Current.Sequence);
        Assert.Equal((advanced.NextFrame * 23 / 33 + 1) % 23, clock.Current.NextFrame);
        Assert.Equal(0, clock.SubstateAge);
        Assert.True(clock.Current.IsBlending);
        Assert.Equal(0f, clock.Current.Fraction);
        for (int update = 0; update < 4; update++)
            clock.Step(0.015f, true);
        Assert.Equal(4, clock.Current.Sequence);
        advanced = clock.Step(0.015f, true);
        Assert.Equal(3, clock.Current.Sequence);
        Assert.Equal((advanced.NextFrame * 33 / 23 + 5) % 33, clock.Current.NextFrame);
        Assert.Equal(0, clock.SubstateAge);
    }

    [Fact]
    public void LongLowStickWalkWrapsWithoutTimeBasedPromotion()
    {
        var clock = SettledWalk();
        bool wrapped = false;
        for (int update = 0; update < 600; update++)
        {
            int previous = clock.Current.CurrentFrame;
            clock.Step(0.015f, true);
            wrapped |= clock.Current.CurrentFrame < previous;
            Assert.Equal(3, clock.Current.Sequence);
        }
        Assert.True(wrapped);
    }

    [Fact]
    public void ResidualFractionIsConvertedThroughOldAndNewFrameRates()
    {
        double[] durations = Enumerable.Repeat(1d / 15d, 33).ToArray();
        durations[9] = 1d / 30d;
        var clock = new Rac1LocomotionAnimationClock(durations, Enumerable.Repeat(1d / 30d, 23).ToArray());
        for (int update = 0; update < 9; update++)
            clock.Step(0.03f, true);
        Assert.Equal(new(3, 9, 10, 0, false), clock.Current);
        clock.Step(0.03f, true);
        Assert.Equal(new(3, 10, 11, 0.5f, false), clock.Current);
    }

    [Fact]
    public void FractionNearOneSnapsBeforeCrossingFrameBoundary()
    {
        var clock = Clock();
        for (int update = 0; update < 8; update++)
            clock.Step(3.98f / 157f, true);
        clock.Step(3.98f / 157f, true);
        Assert.Equal(new(3, 9, 10, 0, false), clock.Current);
    }

    [Fact]
    public void InvalidInputCannotPartiallyAdvanceClock()
    {
        var clock = SettledWalk();
        var before = clock.Current;
        int age = clock.SubstateAge;
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Step(float.NaN, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Step(1f, true));
        Assert.Equal(before, clock.Current);
        Assert.Equal(age, clock.SubstateAge);
    }

    private static Rac1LocomotionAnimationClock Clock() => new(
        Enumerable.Repeat(1d / 15d, 33).ToArray(), Enumerable.Repeat(1d / 30d, 23).ToArray());

    [Fact]
    public void PlaybackHandoffPreservesPhaseAndImmediatelyRestoresSpeedMultiplier()
    {
        var clock = Rac1LocomotionAnimationClock.ResumePlayback(
            Enumerable.Repeat(1d / 15d, 33).ToArray(), Enumerable.Repeat(1d / 30d, 23).ToArray(),
            4, 17, .25f, .095f);
        Assert.Equal(new(4, 17, 18, .25f, false), clock.Current);
        Assert.Equal(1.33f, clock.PlaybackMultiplier, 5);
        clock.Step(.095f, true);
        Assert.Equal(17, clock.Current.CurrentFrame);
        Assert.Equal(.915f, clock.Current.Fraction, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1LocomotionAnimationClock.ResumePlayback(
            Enumerable.Repeat(1d / 15d, 33).ToArray(), Enumerable.Repeat(1d / 30d, 23).ToArray(),
            4, 17, float.NaN, .095f));
    }

    private static Rac1LocomotionAnimationClock SettledWalk()
    {
        var clock = Clock();
        for (int update = 0; update < 8; update++)
            clock.Step(0.015f, true);
        return clock;
    }
}

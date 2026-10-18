using OBP.RAC1.Player;
using Release = OBP.RAC1.Player.Rac1OrdinaryStopAnimation.Release;
using Restart = OBP.RAC1.Player.Rac1OrdinaryStopAnimation.Restart;

namespace OBP.Tests;

public sealed class Rac1StopAnimationTests
{
    [Fact]
    public void DefaultStopUsesStrictFractionalTimeBoundsAndPreviousPoseSequence()
    {
        Assert.Equal(6, Select(4, 3f));
        Assert.Equal(5, Select(4, MathF.BitIncrement(3f)));
        Assert.Equal(5, Select(4, MathF.BitDecrement(16f)));
        Assert.Equal(6, Select(4, 16f));
        Assert.Equal(5, Select(4, 7.5f));
        // Moby+0x52 is 255 while a snapshotted pose is being blended.
        foreach (int previous in new[] { 0, 3, 5, 6, 13, 20, 255 })
            Assert.Equal(6, Select(previous, 7.5f));
    }

    private static int Select(int previous, float time) =>
        Rac1RatchetSequenceSelection.SelectDefaultStopSequence(previous, time);

    [Fact]
    public void ReleaseSeparatesCappedInputContactLengthAndUncappedHistory()
    {
        float low = Rac1OrdinaryStopAnimation.LowMotionThreshold;
        float high = Rac1OrdinaryStopAnimation.HighMotionThreshold;
        Assert.Equal(Release.KeepLocomotion, ReleaseFor(0.17f, 0.095f, 1));
        Assert.Equal(Release.Standing, ReleaseFor(MathF.BitDecrement(.17f), low, 1));
        Assert.Equal(Release.MiddleStop, ReleaseFor(0, MathF.BitIncrement(low), 1));
        Assert.Equal(Release.MiddleStop, ReleaseFor(0, MathF.BitDecrement(high), 1));
        Assert.Equal(Release.DefaultStop, ReleaseFor(0, high, .8f));
        Assert.Equal(Release.MiddleStop, ReleaseFor(0, high, MathF.BitDecrement(.8f)));
        Assert.Equal(Release.DefaultStop, ReleaseFor(0, .095f, MathF.Sqrt(2)));
        Assert.Equal(Release.KeepLocomotion, Rac1OrdinaryStopAnimation.SelectRelease(0, 0, 0, 25));
        Assert.Equal(Release.Standing, Rac1OrdinaryStopAnimation.SelectRelease(0, 0, 0, 26));
    }

    [Fact]
    public void RestartKeepsStrictInputBoundsAndZeroScalarNoSelectionPath()
    {
        Assert.Equal(Restart.KeepStop, Rac1OrdinaryStopAnimation.SelectRestart(.2f, 0));
        Assert.Equal(Restart.StandingWithoutSelection,
            Rac1OrdinaryStopAnimation.SelectRestart(MathF.BitIncrement(.2f), 0));
        Assert.Equal(Restart.KeepStop, Rac1OrdinaryStopAnimation.SelectRestart(.5f, .01f));
        Assert.Equal(Restart.Locomotion,
            Rac1OrdinaryStopAnimation.SelectRestart(MathF.BitIncrement(.5f), .01f));
    }

    [Fact]
    public void HistoryClampsLookbackToAvailableSamplesAndWrapsAtThirty()
    {
        var history = new Rac1InputMagnitudeHistory();
        Assert.Equal(0, history.Read(4));
        history.Append(MathF.Sqrt(2));
        Assert.Equal(MathF.Sqrt(2), history.Read(4));
        for (int sample = 2; sample <= 45; sample++)
            history.Append(sample / 100f);
        Assert.Equal(30, history.Count);
        Assert.Equal(.45f, history.Read(1));
        Assert.Equal(.42f, history.Read(4));
        Assert.Equal(.16f, history.Read(30));
        history.Reset();
        Assert.Equal(0, history.Count);
        Assert.Equal(0, history.Read(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Append(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Read(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseFor(0, float.NaN, 0));
    }

    [Fact]
    public void AnimationTimeUsesAuthoredCoordinatesIncludingBlendAndWrap()
    {
        double[] timeline = [0, 2, 4, 6];
        Assert.Equal(2.5f, Rac1OrdinaryStopAnimation.EvaluateAnimationTime(new(3, 1, 2, .25f, false), timeline));
        Assert.Equal(4.25f, Rac1OrdinaryStopAnimation.EvaluateAnimationTime(new(3, 2, 2, .25f, true), timeline));
        Assert.Equal(6.25f, Rac1OrdinaryStopAnimation.EvaluateAnimationTime(new(3, 3, 0, .25f, false), timeline));
        var clock = new Rac1AnimationFrameClock(
            new Dictionary<int, IReadOnlyList<double>> { [0] = [1d / 7.5, 1d / 7.5] }, 0, 0, 1);
        clock.SelectOrdinaryStanding();
        for (int update = 0; update < 10; update++)
        {
            clock.Advance();
            Assert.True(clock.Current.IsBlending);
            Assert.Equal(Rac1OrdinaryStopAnimation.StandingBlend[update], clock.Current.Fraction);
        }
        clock.Advance();
        Assert.False(clock.Current.IsBlending);
    }

    private static Release ReleaseFor(float input, float motion, float history) =>
        Rac1OrdinaryStopAnimation.SelectRelease(input, motion, history, 30);
}

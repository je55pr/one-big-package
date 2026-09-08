using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Pure evaluation of the neutral ambient-animation contract
/// (<see cref="RuntimeAmbientAnimation"/> → <see cref="AmbientAnimationSample"/>).
/// Deterministic in the elapsed time so captures reproduce.
/// </summary>
public class AmbientAnimatorTests
{
    private static RuntimeAmbientAnimation UvScroll((double, double, double) rate, double phase = 0) =>
        new("sky", null, RuntimeAmbientAnimationKind.UvScroll, rate, phase);

    private static RuntimeAmbientAnimation Spin((double, double, double) rate, double phase = 0) =>
        new("sky", null, RuntimeAmbientAnimationKind.Spin, rate, phase);

    [Fact]
    public void TimeZero_NoPhase_IsTheNeutralPose()
    {
        Assert.Equal(AmbientAnimationSample.Neutral, AmbientAnimator.Sample(UvScroll((0.1, 0.2, 0)), 0));
        Assert.Equal(AmbientAnimationSample.Neutral, AmbientAnimator.Sample(Spin((0, 0.5, 0)), 0));
    }

    [Fact]
    public void UvScroll_IsRateTimesTime_WrappedInto01()
    {
        var s = AmbientAnimator.Sample(UvScroll((0.1, 0.04, 0)), 5);
        Assert.Equal(0.5, s.U, precision: 9);
        Assert.Equal(0.2, s.V, precision: 9);
        Assert.Equal(0, s.Radians);

        // 0.1 u/s * 15 s = 1.5 -> wraps to 0.5
        Assert.Equal(0.5, AmbientAnimator.Sample(UvScroll((0.1, 0, 0)), 15).U, precision: 9);
    }

    [Fact]
    public void UvScroll_NegativeRate_WrapsFromAbove()
    {
        Assert.Equal(0.9, AmbientAnimator.Sample(UvScroll((-0.1, 0, 0)), 1).U, precision: 9);
    }

    [Fact]
    public void Spin_IsAxisSpeedTimesTime_WrappedIntoTwoPi()
    {
        var s = AmbientAnimator.Sample(Spin((0, 0.5, 0)), 4);
        Assert.Equal(2.0, s.Radians, precision: 9); // |(0,0.5,0)| * 4
        Assert.Equal(0, s.U);

        double big = AmbientAnimator.Sample(Spin((0, 1, 0)), 100).Radians;
        Assert.InRange(big, 0, 2 * System.Math.PI);
        Assert.Equal(100.0 % (2 * System.Math.PI), big, precision: 6);
    }

    [Fact]
    public void Phase_OffsetsTheEvaluatedValue()
    {
        Assert.Equal(0.25, AmbientAnimator.Sample(UvScroll((0, 0, 0), phase: 0.25), 10).U, precision: 9);
        Assert.Equal(0.75, AmbientAnimator.Sample(Spin((0, 0, 0), phase: 0.75), 10).Radians, precision: 9);
    }

    [Fact]
    public void SameTime_SameOutput()
    {
        var anim = UvScroll((0.037, 0.011, 0));
        Assert.Equal(AmbientAnimator.Sample(anim, 12.34), AmbientAnimator.Sample(anim, 12.34));
    }

    [Fact]
    public void Matches_RespectsKindAndOptionalTextureId()
    {
        var anyTex = new RuntimeAmbientAnimation("sky", null, RuntimeAmbientAnimationKind.UvScroll, (0, 0, 0));
        var oneTex = new RuntimeAmbientAnimation("sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0, 0, 0));

        Assert.True(anyTex.Matches("sky", 0));
        Assert.True(anyTex.Matches("sky", 9));
        Assert.False(anyTex.Matches("tfrag", 0));

        Assert.True(oneTex.Matches("sky", 3));
        Assert.False(oneTex.Matches("sky", 4));
    }
}

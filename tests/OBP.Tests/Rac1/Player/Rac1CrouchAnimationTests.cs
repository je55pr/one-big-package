using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1CrouchAnimationTests
{
    [Fact]
    public void TurnEntryUsesStrictMagnitudeAndVelocityThresholds()
    {
        float threshold = Rac1CrouchAnimation.TurnVelocityThreshold;
        Assert.Null(Rac1CrouchAnimation.SelectTurn(13, 0.2f, 0.1f));
        Assert.Equal(14, Rac1CrouchAnimation.SelectTurn(13, MathF.BitIncrement(0.2f), 0.1f));
        Assert.Null(Rac1CrouchAnimation.SelectTurn(13, 1, threshold));
        Assert.Equal(14, Rac1CrouchAnimation.SelectTurn(13, 1, MathF.BitIncrement(threshold)));
        Assert.Equal(15, Rac1CrouchAnimation.SelectTurn(13, 1, -MathF.BitIncrement(threshold)));
        Assert.Null(Rac1CrouchAnimation.SelectTurn(14, 0, threshold));
        Assert.Null(Rac1CrouchAnimation.SelectTurn(15, 0, -threshold));
        Assert.Equal(13, Rac1CrouchAnimation.SelectTurn(14, 0, 0));
        Assert.Equal(13, Rac1CrouchAnimation.SelectTurn(15, 0, 0));
        Assert.Equal(15, Rac1CrouchAnimation.SelectTurn(14, 0, -0.1f));
    }

    [Fact]
    public void TurnRateUsesMagnitudeAndBothClamps()
    {
        Assert.Equal(0.55f, Rac1CrouchAnimation.TurnPlaybackMultiplier(0.001f));
        Assert.Equal(1f, Rac1CrouchAnimation.TurnPlaybackMultiplier(Rac1CrouchAnimation.TurnRateDivisor));
        Assert.Equal(1f, Rac1CrouchAnimation.TurnPlaybackMultiplier(-Rac1CrouchAnimation.TurnRateDivisor));
        Assert.Equal(2.5f, Rac1CrouchAnimation.TurnPlaybackMultiplier(1f));
    }

    [Fact]
    public void CrouchLoopsTheDecodedClipAndCurveDoesNotFinishAtPoint99()
    {
        var clock = new Rac1AnimationFrameClock(new Dictionary<int, IReadOnlyList<double>>
        {
            [0] = Enumerable.Repeat(1d / 15d, 2).ToArray(),
            [13] = Enumerable.Repeat(1d / 15d, 15).ToArray(),
        }, 13, 2, 9);
        for (int tick = 0; tick < 9 + 13 * 4; tick++)
            clock.Advance();
        Assert.Equal(new Rac1AnimationFrameClock.Sample(13, 0, 1, 0, false), clock.Current);
        clock.SelectCrouchExit();
        for (int tick = 0; tick < 15; tick++)
            clock.Advance();
        Assert.Equal(0.99f, clock.Current.Fraction);
        Assert.True(clock.Current.IsBlending);
        clock.Advance();
        Assert.Equal(new Rac1AnimationFrameClock.Sample(0, 0, 1, 0, false), clock.Current);
    }
}

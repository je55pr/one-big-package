using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeHoverTests
{
    [Fact]
    public void SourceConstantsProduceAuthoredHeightAndTilt()
    {
        var hover = new Rac1TaxiNativeHover(new Rac1TaxiHoverPhases(0, 0, 0));
        Assert.Equal(0, hover.HeightOffset);
        Assert.Equal(0, hover.PitchRadians);
        Assert.Equal(0, hover.RollRadians);

        hover.Advance(1d / 60d);
        Assert.Equal(.2 * Math.Sin(2d * Math.PI / 180d), hover.HeightOffset, 10);
        Assert.Equal(0, hover.PitchRadians);
        Assert.Equal(0, hover.RollRadians);

        hover.Advance(1d / 60d);
        double tilt = Math.PI / 40d * Math.Sin(70d * Math.PI / 180d / 60d);
        double spin = 100d * Math.PI / 180d / 60d;
        Assert.Equal(tilt * Math.Sin(spin), hover.PitchRadians, 10);
        Assert.Equal(tilt * Math.Cos(spin), hover.RollRadians, 10);
        Assert.InRange(hover.HeightOffset, -.2, .2);
    }

    [Fact]
    public void HoverAndTiltAreDeterministicAtDifferentHostTickRates()
    {
        var phases = new Rac1TaxiHoverPhases(.65, -.23, 1.45);
        var sixty = new Rac1TaxiNativeHover(phases);
        var thirty = new Rac1TaxiNativeHover(phases);
        var oneTwenty = new Rac1TaxiNativeHover(phases);
        for (int i = 0; i < 720; i++)
        {
            sixty.Advance(1d / 60d);
            if (i % 2 == 0)
                thirty.Advance(1d / 30d);
            oneTwenty.Advance(1d / 120d);
            oneTwenty.Advance(1d / 120d);
        }

        Assert.Equal(sixty.HeightOffset, thirty.HeightOffset, 10);
        Assert.Equal(sixty.HeightOffset, oneTwenty.HeightOffset, 10);
        Assert.Equal(sixty.PitchRadians, thirty.PitchRadians, 10);
        Assert.Equal(sixty.PitchRadians, oneTwenty.PitchRadians, 10);
        Assert.Equal(sixty.RollRadians, thirty.RollRadians, 10);
        Assert.Equal(sixty.RollRadians, oneTwenty.RollRadians, 10);
        Assert.InRange(Math.Abs(sixty.PitchRadians), 0, Math.PI / 40);
        Assert.InRange(Math.Abs(sixty.RollRadians), 0, Math.PI / 40);
    }

    [Fact]
    public void HoverRejectsInvalidSamplesOrSourcePhases()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1TaxiNativeHover(new Rac1TaxiHoverPhases(double.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1TaxiNativeHover(new Rac1TaxiHoverPhases(0, double.PositiveInfinity, 0)));
        var hover = new Rac1TaxiNativeHover(new Rac1TaxiHoverPhases(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => hover.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => hover.Advance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => hover.Advance(double.PositiveInfinity));
        Assert.Equal(0, hover.HeightOffset);
    }
}

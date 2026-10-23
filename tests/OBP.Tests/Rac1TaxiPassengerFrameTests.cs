using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiPassengerFrameTests
{
    [Fact]
    public void UnturnedCabRetainsExactBoardingWorldOffset()
    {
        var frame = new Rac1TaxiPassengerFrame(new(1.2, 0.4, -0.8), 0.7);
        var position = frame.PositionAt(new(10, 5, 20), 0.7);
        Assert.Equal(11.2, position.X, 10);
        Assert.Equal(5.4, position.Y, 10);
        Assert.Equal(19.2, position.Z, 10);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1.5707963267948966, 0, -1)]
    [InlineData(3.141592653589793, -1, 0)]
    [InlineData(-1.5707963267948966, 0, 1)]
    [InlineData(6.283185307179586, 1, 0)]
    public void RiderRevolvesAroundCabYAxisWithoutLosingHeight(
        double turn, double expectedX, double expectedZ)
    {
        var frame = new Rac1TaxiPassengerFrame(new(1, 0.6, 0), 0);
        var offset = frame.OffsetAt(turn);
        Assert.Equal(expectedX, offset.X, 9);
        Assert.Equal(0.6, offset.Y, 9);
        Assert.Equal(expectedZ, offset.Z, 9);
    }

    [Fact]
    public void RelativeHeadingNotAbsoluteHeadingControlsRiderOrbit()
    {
        const double heading = -1.23;
        var a = new Rac1TaxiPassengerFrame(new(0.8, 0.1, -0.5), 0);
        var b = new Rac1TaxiPassengerFrame(new(0.8, 0.1, -0.5), heading);
        var first = a.OffsetAt(0.25);
        var second = b.OffsetAt(heading + 0.25);
        Assert.Equal(first.X, second.X, 10);
        Assert.Equal(first.Y, second.Y, 10);
        Assert.Equal(first.Z, second.Z, 10);
        Assert.Equal(Math.Sqrt(0.8 * 0.8 + 0.5 * 0.5),
            Math.Sqrt(second.X * second.X + second.Z * second.Z), 10);
    }

    [Fact]
    public void InvalidPoseDoesNotCreatePassengerFrameOrPosition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1TaxiPassengerFrame(new(double.NaN, 0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1TaxiPassengerFrame(new(0, 0, 0), double.PositiveInfinity));
        var frame = new Rac1TaxiPassengerFrame(new(1, 0, 0), 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            frame.OffsetAt(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            frame.PositionAt(new(0, double.NegativeInfinity, 0), 0));
    }
}

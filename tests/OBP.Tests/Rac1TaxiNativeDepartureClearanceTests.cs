using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeDepartureClearanceTests
{
    private static Rac1TaxiPoint At(double x, double y = 0, double z = 0) =>
        new(x, y, z);

    [Theory]
    [InlineData(15, 0, 0, false)]
    [InlineData(15.001, 0, 0, true)]
    [InlineData(0, 15.001, 0, true)]
    [InlineData(0, 0, -15.001, true)]
    [InlineData(12, 9, 0, false)]
    public void AuthoredExitPathRequiresMoreThanFifteenUnitsInThreeDimensions(
        double x, double y, double z, bool expected)
    {
        Assert.Equal(expected, Rac1TaxiNativeDepartureClearance.Cleared(
            At(0), At(x, y, z), 61));
        // Synthetic fixtures lack PVar. Keep the ordinary 15-unit case.
        Assert.Equal(expected, Rac1TaxiNativeDepartureClearance.Cleared(
            At(0), At(x, y, z), null));
    }

    [Theory]
    [InlineData(5, 0, 0, false)]
    [InlineData(5.001, 0, 0, true)]
    [InlineData(0, 500, 0, false)]
    [InlineData(0, 500, -5.001, true)]
    public void PathlessExitSelectorUsesOnlyFiveHorizontalUnits(
        double x, double y, double z, bool expected)
    {
        Assert.Equal(expected, Rac1TaxiNativeDepartureClearance.Cleared(
            At(0), At(x, y, z), -1));
    }

    [Fact]
    public void NonFiniteSourceOrPlayerCannotAdmitDepartingTaxi()
    {
        Assert.False(Rac1TaxiNativeDepartureClearance.Cleared(
            At(double.NaN), At(1000), 61));
        Assert.False(Rac1TaxiNativeDepartureClearance.Cleared(
            At(0), At(0, double.PositiveInfinity), -1));
    }
}

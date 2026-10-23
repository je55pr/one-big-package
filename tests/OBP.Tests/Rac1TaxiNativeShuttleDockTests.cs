using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeShuttleDockTests
{
    private static readonly Rac1TaxiPoint Source = new(10, 20, 30);
    private static readonly Rac1TaxiPoint OtherEnd = new(110, 40, 130);
    private static Rac1TaxiNativeShuttleDock Dock(int initialSide = 1) => new(
        [OtherEnd, Source], [Source, OtherEnd], initialSide, -1);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NativeInitialSideSelectsAuthoredStartOfThatRoute(int side)
    {
        var dock = Dock(side);
        Assert.Equal(side, dock.InitialSide);
        Assert.Equal(side == 0 ? OtherEnd : Source, dock.NativeInitialWaypoint);
        Assert.Equal(-1, dock.OptionalCuboidSelector);
    }

    [Fact]
    public void NearestEndSelectorIsInvertedByNativeStateOne()
    {
        var dock = Dock();
        // s4/s7 = 1 when side-zero start (the opposite end) is
        // closer, and 0 when side-one start is closer.
        Assert.Equal(1, dock.OppositeNearestPathSide(OtherEnd));
        Assert.Equal(0, dock.OppositeNearestPathSide(Source));
        // Exact tie chooses zero (the native c.lt branch is strict).
        Assert.Equal(0, dock.OppositeNearestPathSide(
            Rac1TaxiPoint.Lerp(Source, OtherEnd, 0.5)));
        Assert.Null(dock.OppositeNearestPathSide(
            new Rac1TaxiPoint(double.NaN, 0, 0)));
    }

    [Fact]
    public void AutomaticNativeRepositionRequiresDifferentNearestEnds()
    {
        var dock = Dock();
        Assert.Equal(1, dock.RepositionSide(OtherEnd, Source));
        Assert.Equal(0, dock.RepositionSide(Source, OtherEnd));
        Assert.Null(dock.RepositionSide(Source, Source));
        Assert.Null(dock.RepositionSide(OtherEnd, OtherEnd));
        Assert.Null(dock.RepositionSide(
            new Rac1TaxiPoint(double.PositiveInfinity, 0, 0), OtherEnd));
    }

    [Fact]
    public void InvalidNativeSidesCuboidsAndCoordinatesAreNotAccepted()
    {
        Assert.Throws<ArgumentException>(() => Dock(initialSide: 2));
        Assert.Throws<ArgumentException>(() => new Rac1TaxiNativeShuttleDock(
            [Source, OtherEnd], [OtherEnd, Source], 0, -2));
        Assert.Throws<ArgumentException>(() => new Rac1TaxiNativeShuttleDock(
            [Source], [OtherEnd, Source], 0, -1));
        Assert.Throws<ArgumentException>(() => new Rac1TaxiNativeShuttleDock(
            [Source, OtherEnd], [Source, new Rac1TaxiPoint(double.NaN, 0, 0)],
            0, -1));
    }
}

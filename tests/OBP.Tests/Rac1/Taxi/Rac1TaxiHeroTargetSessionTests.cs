using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiHeroTargetSessionTests
{
    private static Rac1TaxiPoint At(double x, double z = 0) => new(x, 8, z);

    [Fact]
    public void RetailHeroTargetDoesNotDepartUntilAfterWalkSettleAndRelease()
    {
        var target = new Rac1TaxiHeroTargetSession(At(10), Math.PI / 2);
        Assert.Equal(0x65, target.NativeState);
        Assert.Equal(1, target.NativeHeroTargetStatus);
        target.Observe(At(9.79), Math.PI / 2, 0, animationFinished: true);
        Assert.Equal(0x65, target.NativeState);
        target.Observe(At(9.85), 0, 0, animationFinished: true);
        Assert.Equal(0x65, target.NativeState);
        target.Observe(At(9.85), Math.PI / 2, 0.01, animationFinished: false);
        Assert.Equal(0x67, target.NativeState);
        Assert.Equal(2, target.NativeHeroTargetStatus);
        Assert.False(target.DepartureAdmitted);
        target.Observe(At(9.86), Math.PI / 2, 0, animationFinished: true);
        Assert.True(target.DepartureAdmitted);
        Assert.Equal(0, target.NativeHeroTargetStatus);
    }

    [Fact]
    public void MovingModesBrakeThrough66ThenRequireAnotherTargetObservation()
    {
        var target = new Rac1TaxiHeroTargetSession(At(10), 0, movementMode: 1);
        target.Observe(At(10), 0, 0.1, animationFinished: false);
        Assert.Equal(0x66, target.NativeState);
        target.Observe(At(10), 0, 0.01, animationFinished: false);
        Assert.Equal(0x66, target.NativeState);
        target.Observe(At(10), 0, 0.01, animationFinished: true);
        Assert.Equal(0x65, target.NativeState);
        target.Observe(At(10), 0, 0.01, animationFinished: false);
        Assert.Equal(0x67, target.NativeState);
        target.Observe(At(10.21), 0, 0.01, animationFinished: true);
        Assert.Equal(0x65, target.NativeState); // Moved off the spot before release.
    }

    [Fact]
    public void NativeFlagReleasesSettledStateAndRetargetDoesNotResetActiveState()
    {
        var target = new Rac1TaxiHeroTargetSession(At(0), 0);
        target.Observe(At(0), 0, 0, animationFinished: false);
        Assert.Equal(0x67, target.NativeState);
        target.Retarget(At(0), 0, flag: 1);
        Assert.Equal(0x67, target.NativeState);
        target.Observe(At(0), 0, 0, animationFinished: false);
        Assert.True(target.DepartureAdmitted);
        target.Retarget(At(2), Math.PI, flag: 0);
        Assert.Equal(0x65, target.NativeState);
        Assert.Equal(2, target.Target.X);
    }

    [Fact]
    public void ScriptedWalkContractWrapsFacingAndRejectsNonFiniteObservations()
    {
        var target = new Rac1TaxiHeroTargetSession(At(0), Math.PI - 0.01);
        target.Observe(At(0), -Math.PI + 0.01, 0.01, animationFinished: false);
        Assert.Equal(0x67, target.NativeState);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            target.Observe(At(0), double.NaN, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            target.Observe(At(0), 0, -1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            target.Retarget(At(double.PositiveInfinity), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1TaxiHeroTargetSession(At(0), 0, movementMode: 2));
    }
}

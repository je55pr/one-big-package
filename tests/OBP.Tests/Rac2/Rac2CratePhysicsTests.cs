using System.Numerics;
using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class Rac2CratePhysicsTests
{
    [Fact]
    public void SupportedCrateFallsWhenItsLowerCrateDisappearsAndSettlesOnTheFloor()
    {
        var crate = new Rac2CrateFallSession(5);
        for (int tick = 0; tick < 60; tick++) crate.Step(5);
        Assert.Equal(5, crate.Height);
        crate.Step(null);
        Assert.True(crate.Height < 5);
        Assert.Equal(-Rac2CrateFallSession.GravityPerTick, crate.Velocity);
        for (int tick = 0; tick < 120; tick++) crate.Step(4);
        Assert.Equal(4, crate.Height);
        Assert.Equal(0, crate.Velocity);
        for (int tick = 0; tick < 200; tick++) crate.Step(null);
        Assert.Equal(-.3f, crate.Velocity);
    }

    [Fact]
    public void RapidBreaksReduceTheNativePartBudgetAndQuietTimeRestoresIt()
    {
        var budget = new Rac2CrateBreakBudget();
        Assert.Equal(0x25, budget.Begin(0, 256));
        Assert.Equal(4, budget.Recent);
        Assert.Equal(0x25, budget.Begin(0, 256));
        Assert.Equal(8, budget.Recent);
        Assert.Equal(0x25, budget.Begin(0, 256));
        Assert.Equal(11, budget.Recent);
        Assert.Equal(5, budget.Begin(0, 256));
        Assert.Equal(0x25, budget.Begin(241, 256));
        Assert.Equal(4, budget.Recent);
        Assert.Equal(0, budget.Begin(241, 100));
    }

    [Fact]
    public void FragmentHasBallisticMotionBouncesAndEventuallyFades()
    {
        var fragment = new Rac2CrateFragment(0, Vector3.UnitZ, (min, max) => (min + max) / 2);
        var velocity = fragment.Velocity;
        fragment.Step(null);
        Assert.True(fragment.Position.X > 0);
        Assert.True(fragment.Velocity.Z < velocity.Z);
        Assert.NotEqual(Quaternion.Identity, fragment.Rotation);
        for (int tick = 0; tick < 40; tick++) fragment.Step(null);
        fragment.Step(new(Vector3.Zero, Vector3.UnitZ));
        Assert.True(fragment.Velocity.Z >= 0);
        for (int tick = 0; tick < 900; tick++) fragment.Step(new(Vector3.Zero, Vector3.UnitZ));
        Assert.True(fragment.Expired);
    }
}

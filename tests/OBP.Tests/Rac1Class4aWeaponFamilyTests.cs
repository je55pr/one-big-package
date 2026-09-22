using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1Class4aWeaponFamilyTests
{
    [Fact]
    public void ContractBindsMineControllerToClass4aChild()
    {
        Assert.Equal(0xbe, Rac1Class4aWeaponFamily.NativeWeaponClassId);
        Assert.Equal(0x002c1ad0, Rac1Class4aWeaponFamily.NativeWeaponUpdate);
        Assert.Equal(0x4a, Rac1Class4aWeaponFamily.NativeProjectileClassId);
        Assert.Equal(0x002a9ed0, Rac1Class4aWeaponFamily.NativeProjectileConstructor);
        Assert.Equal(0x002aa670, Rac1Class4aWeaponFamily.NativeProjectileUpdate);
        Assert.Equal(0, Rac1Class4aWeaponFamily.ProjectileCreationNativeState);
        Assert.Equal(1, Rac1Class4aWeaponFamily.ProjectileLaunchedNativeState);
        Assert.Equal(0x30, Rac1Class4aWeaponFamily.ProjectileSourcePvarOffset);
        Assert.Equal(10, Rac1Class4aWeaponFamily.ProjectileRearmTicks);
        Assert.Equal(20, Rac1Class4aWeaponFamily.FireCooldownTicks);
    }
    [Fact]
    public void StagingAdmissionRemainsExternalToBoundedMineFamily()
    {
        var session = new Rac1Class4aWeaponFamilySession();

        var blocked = session.Step(fireRequested: true, stagingAdmitted: false);
        Assert.Null(blocked.PrearmedProjectile);
        Assert.Null(blocked.Launch);

        var staged = session.Step(fireRequested: false, stagingAdmitted: true);
        var projectile = Assert.IsType<Rac1Class4aProjectile>(
            staged.PrearmedProjectile);
        Assert.Equal(1, projectile.ProjectileId);
        Assert.Equal(0xbe, projectile.NativeWeaponClassId);
        Assert.Equal(0x4a, projectile.NativeProjectileClassId);
        Assert.Equal(0x30, projectile.ProjectileSourcePvarOffset);
    }

    [Fact]
    public void LaunchUsesRecoveredTenTwentyTickCadence()
    {
        var session = new Rac1Class4aWeaponFamilySession();
        var staged = session.Step(false, stagingAdmitted: true);
        var first = Assert.IsType<Rac1Class4aProjectile>(
            staged.PrearmedProjectile);

        var fired = session.Step(true, stagingAdmitted: true);
        var launch = Assert.IsType<Rac1Class4aLaunch>(fired.Launch);
        Assert.Equal(first, launch.Projectile);
        Assert.Equal(20, fired.FireCooldownTicksRemaining);
        Assert.Equal(10, fired.ProjectileRearmTicksRemaining);
        Assert.Null(fired.PrearmedProjectile);
        for (int tick = 1; tick < Rac1Class4aWeaponFamily.ProjectileRearmTicks; tick++)
        {
            var probe = session.Step(false, stagingAdmitted: true);
            Assert.Null(probe.PrearmedProjectile);
        }

        var rearmed = session.Step(false, stagingAdmitted: true);
        Assert.NotNull(rearmed.PrearmedProjectile);
        Assert.Equal(10, rearmed.FireCooldownTicksRemaining);
        Assert.Equal(0, rearmed.ProjectileRearmTicksRemaining);

        for (int tick = 11; tick < Rac1Class4aWeaponFamily.FireCooldownTicks; tick++)
        {
            var probe = session.Step(true, stagingAdmitted: true);
            Assert.Null(probe.Launch);
        }

        var second = session.Step(true, stagingAdmitted: true);
        Assert.NotNull(second.Launch);
        Assert.Equal(20, second.FireCooldownTicksRemaining);
        Assert.Equal(10, second.ProjectileRearmTicksRemaining);
    }
}

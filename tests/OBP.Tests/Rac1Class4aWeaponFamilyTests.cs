using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1Class4aWeaponFamilyTests
{
    [Fact]
    public void ContractFreezesOnlyRetainedSeparateFamilyFacts()
    {
        Assert.Equal(0xc0, Rac1Class4aWeaponFamily.NativeWeaponClassId);
        Assert.Equal(0x4a, Rac1Class4aWeaponFamily.NativeProjectileClassId);
        Assert.Equal(0, Rac1Class4aWeaponFamily.ProjectileCreationNativeState);
        Assert.Equal(1, Rac1Class4aWeaponFamily.ProjectileLaunchedNativeState);
        Assert.Equal(0x30, Rac1Class4aWeaponFamily.ProjectileSourcePvarOffset);
        Assert.Equal(10, Rac1Class4aWeaponFamily.ProjectileRearmTicks);
        Assert.Equal(20, Rac1Class4aWeaponFamily.FireCooldownTicks);
        Assert.Equal(1d, Rac1Class4aWeaponFamily.NativeDamage);
        Assert.Equal(0x00010000u, Rac1Class4aWeaponFamily.NativeDamageFlags);

        var handoff = Rac1Class4aWeaponFamily.DirectDamageHandoff;
        Assert.Equal(Rac1NativeDamageHandoffKind.DirectVictimRecord, handoff.Kind);
        Assert.True(handoff.RetainsSourceMoby);
        Assert.True(handoff.VictimIsPreselected);
        Assert.False(handoff.ExcludesSourceMobyFromCandidates);
    }
    [Fact]
    public void StagingAdmissionRemainsExternalAndCannotInventAnItemBinding()
    {
        var session = new Rac1Class4aWeaponFamilySession();

        var blocked = session.Step(fireRequested: true, stagingAdmitted: false);
        Assert.Null(blocked.PrearmedProjectile);
        Assert.Null(blocked.Launch);

        var staged = session.Step(fireRequested: false, stagingAdmitted: true);
        var projectile = Assert.IsType<Rac1Class4aProjectile>(
            staged.PrearmedProjectile);
        Assert.Equal(1, projectile.ProjectileId);
        Assert.Equal(0xc0, projectile.NativeWeaponClassId);
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

    [Fact]
    public void DirectDamageRequiresALaunchedProjectileAndKeepsCompletionExplicit()
    {
        var session = new Rac1Class4aWeaponFamilySession();
        var victim = Dynamic(nativeClassId: 749, instanceIndex: 7);

        Assert.Null(session.ResolvePreselectedVictim(999, victim));
        session.Step(false, stagingAdmitted: true);
        var fired = session.Step(true, stagingAdmitted: true);
        var launch = Assert.IsType<Rac1Class4aLaunch>(fired.Launch);

        var damage = Assert.IsType<Rac1Class4aDirectDamageResult>(
            session.ResolvePreselectedVictim(launch.Projectile.ProjectileId, victim));
        Assert.Equal(749, damage.TargetNativeClassId);
        Assert.Equal(1d, damage.NativeDamage);
        Assert.Equal(0x00010000u, damage.NativeDamageFlags);
        Assert.Equal(
            Rac1NativeDamageHandoffKind.DirectVictimRecord,
            damage.DamageHandoff.Kind);

        Assert.True(session.CompleteProjectile(launch.Projectile.ProjectileId));
        Assert.Null(
            session.ResolvePreselectedVictim(launch.Projectile.ProjectileId, victim));
    }

    private static RuntimeDynamicObject Dynamic(
        int nativeClassId,
        int instanceIndex) =>
        new(
            "rac1",
            nativeClassId,
            instanceIndex,
            null,
            $"moby:{nativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            Array.Empty<RuntimeOpaquePayload>());
}

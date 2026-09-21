using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1BombGloveTests
{
    [Fact]
    public void ProjectileContractMatchesControlledItem10Witness()
    {
        Assert.Equal(0x79, Rac1BombGlove.NativeProjectileClassId);
        Assert.Equal(1, Rac1BombGlove.ProjectileLaunchedNativeState);
        Assert.Equal(2, Rac1BombGlove.ProjectileContactNativeState);
        Assert.Equal(0xfe, Rac1BombGlove.ProjectileTerminalNativeState);
        Assert.Equal(0x50, Rac1BombGlove.ProjectilePvarOwnerPointerOffset);
        Assert.Equal(0x50, Rac1BombGlove.WeaponPvarStagedProjectileOffset);
        Assert.Equal(300, Rac1BombGlove.ProjectileLongCountdownTicks);
        Assert.Equal(30, Rac1BombGlove.ProjectileShortCountdownTicks);
        Assert.Equal(0.003055555745959282d, Rac1BombGlove.NativeVerticalStepDeltaAtAuthorityScale);
        Assert.Equal(2d, Rac1BombGlove.NativeDamage);
        Assert.Equal(0x00830000u, Rac1BombGlove.NativeDamageFlags);
    }

    [Fact]
    public void FireConsumesExactlyOneRoundAndLaunchesPrearmedProjectile()
    {
        var session = new Rac1BombGloveSession(initialAmmo: 6);
        var ready = session.Probe();
        var prearmed = Assert.IsType<Rac1BombGloveProjectile>(ready.PrearmedProjectile);
        Assert.Equal(0x79, prearmed.NativeClassId);
        Assert.Equal(0, prearmed.CreationNativeState);
        Assert.Equal(1, prearmed.LaunchedNativeState);
        Assert.Equal(Rac1BombGlove.NativeWeaponClassId, prearmed.Ownership.OwnerNativeClassId);
        Assert.Equal(Rac1BombGlove.NativeProjectileClassId, prearmed.Ownership.SpawnedNativeClassId);
        Assert.Equal(0x50, prearmed.Ownership.SpawnedPvarOwnerPointerOffset);
        Assert.Equal(0x50, prearmed.Ownership.OwnerPvarStagedObjectOffset);
        Assert.True(prearmed.Ownership.UsesDedicatedWeaponLaunchFrame);

        var fired = session.Step(fireRequested: true);
        var shot = Assert.IsType<Rac1BombGloveShot>(fired.Shot);

        Assert.Equal(prearmed, shot.Projectile);
        Assert.Equal(6, shot.AmmoBefore);
        Assert.Equal(5, shot.AmmoAfter);
        Assert.Equal(5, fired.Ammo);
        Assert.Equal(20, fired.FireCooldownTicksRemaining);
        Assert.Equal(0, fired.ProjectileRearmTicksRemaining);
        var replacement = Assert.IsType<Rac1BombGloveProjectile>(fired.PrearmedProjectile);
        Assert.NotEqual(prearmed.ProjectileId, replacement.ProjectileId);
        Assert.Equal(0x79, replacement.NativeClassId);
        var use = Assert.IsType<Rac1WeaponUseAdmission>(fired.UseAdmission);
        Assert.True(use.Accepted);
        Assert.Equal(Rac1WeaponUseRejection.None, use.Rejection);
        Assert.Equal(Rac1RatchetSequenceSelection.FirstRangedFireSequenceId, use.NativePlayerSequenceId);
        Assert.Same(use, shot.Admission);
    }

    [Fact]
    public void HeldFireCannotLaunchAgainUntilTwentiethFollowingNativeTick()
    {
        var session = new Rac1BombGloveSession(initialAmmo: 3);
        Assert.NotNull(session.Step(fireRequested: true).Shot);

        for (int tick = 1; tick < Rac1BombGlove.FireCooldownTicks; tick++)
        {
            var probe = session.Step(fireRequested: true);
            Assert.Null(probe.Shot);
            var rejected = Assert.IsType<Rac1WeaponUseAdmission>(probe.UseAdmission);
            Assert.False(rejected.Accepted);
            Assert.Equal(Rac1WeaponUseRejection.CadenceBlocked, rejected.Rejection);
            Assert.NotNull(probe.PrearmedProjectile);
            Assert.Equal(0, probe.ProjectileRearmTicksRemaining);
        }

        var second = session.Step(fireRequested: true);
        Assert.NotNull(second.Shot);
        Assert.Equal(1, second.Ammo);
        Assert.Equal(Rac1BombGlove.FireCooldownTicks, second.FireCooldownTicksRemaining);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Rac1BombGlove.MaxAmmo + 1)]
    public void InitialAmmoMustStayInsideRecoveredCapacity(int ammo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac1BombGloveSession(ammo));
    }

    [Fact]
    public void InventoryBackedSessionUsesItem10AmmoAndRequiresBombGloveEquipped()
    {
        var inventory = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            equipped: Rac1WeaponId.Wrench,
            firstRangedAmmo: 6);
        var session = new Rac1BombGloveSession(inventory);

        var unequipped = session.Step(fireRequested: true);
        Assert.Null(unequipped.Shot);
        Assert.Equal(Rac1WeaponUseRejection.NotEquipped, unequipped.UseAdmission?.Rejection);
        Assert.Equal(6, inventory.FirstRangedAmmo);
        Assert.True(inventory.TryEquip(Rac1WeaponId.FirstRanged));

        var fired = session.Step(fireRequested: true);
        var shot = Assert.IsType<Rac1BombGloveShot>(fired.Shot);
        Assert.Equal(6, shot.AmmoBefore);
        Assert.Equal(5, shot.AmmoAfter);
        Assert.Equal(5, inventory.FirstRangedAmmo);
        Assert.Equal(inventory.FirstRangedAmmo, session.Probe().Ammo);
    }

    [Fact]
    public void ZeroAmmoCannotPrearmOrFire()
    {
        var session = new Rac1BombGloveSession(initialAmmo: 0);
        Assert.Null(session.Probe().PrearmedProjectile);
        var rejected = session.Step(fireRequested: true);
        Assert.Null(rejected.Shot);
        Assert.Equal(Rac1WeaponUseRejection.NoAmmo, rejected.UseAdmission?.Rejection);
        Assert.Equal(0, session.Probe().Ammo);
    }

    [Fact]
    public void ContactAdmitsOnlyRepresentativeGoalOneHostileUntilProjectileCompletes()
    {
        var session = new Rac1BombGloveSession(initialAmmo: 2);
        var shot = Assert.IsType<Rac1BombGloveShot>(session.Step(true).Shot);
        var crate = Dynamic(Rac1BoltCrate.NativeClassId, instanceIndex: 89);
        var hostile = Dynamic(Rac1Class749Hostile.NativeClassId, instanceIndex: 149);

        Assert.Null(session.ResolveGoal1Contact(shot.Projectile.ProjectileId, crate, Rac1BoltCrate.ActiveNativeState));
        var damage = Assert.IsType<Rac1BombGloveDamageResult>(
            session.ResolveGoal1Contact(shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));

        Assert.Equal(Rac1Class749Hostile.NativeClassId, damage.TargetNativeClassId);
        Assert.Equal(2d, damage.NativeDamage);
        Assert.Equal(0x00830000u, damage.NativeDamageFlags);
        Assert.Equal(Rac1NativeDamageHandoffKind.ContactVolume, damage.DamageHandoff.Kind);
        Assert.True(damage.DamageHandoff.RetainsSourceMoby);
        Assert.False(damage.DamageHandoff.VictimIsPreselected);
        Assert.True(damage.DamageHandoff.ExcludesSourceMobyFromCandidates);
        Assert.NotNull(session.ResolveGoal1Contact(
            shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));
        Assert.True(session.CompleteProjectile(shot.Projectile.ProjectileId));
        Assert.Null(session.ResolveGoal1Contact(
            shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));
    }

    [Theory]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFd)]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFe)]
    public void ContactRejectsRecoveredTerminalTargetStates(int terminalState)
    {
        var session = new Rac1BombGloveSession(initialAmmo: 1);
        var shot = Assert.IsType<Rac1BombGloveShot>(session.Step(true).Shot);
        var hostile = Dynamic(Rac1Class749Hostile.NativeClassId, instanceIndex: 149);

        Assert.Null(session.ResolveGoal1Contact(shot.Projectile.ProjectileId, hostile, terminalState));
    }

    private static RuntimeDynamicObject Dynamic(
        int nativeClassId,
        int instanceIndex,
        RuntimeOpaquePayload[]? payloads = null) =>
        new(
            "rac1",
            nativeClassId,
            instanceIndex,
            null,
            $"moby:{nativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            payloads);
}

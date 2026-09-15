using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1BombGloveTests
{
    [Fact]
    public void FireConsumesExactlyOneRoundAndLaunchesPrearmedProjectile()
    {
        var session = new Rac1BombGloveSession(initialAmmo: 6);
        var ready = session.Probe();
        var prearmed = Assert.IsType<Rac1BombGloveProjectile>(ready.PrearmedProjectile);
        Assert.Equal(0x4a, prearmed.NativeClassId);
        Assert.Equal(0, prearmed.CreationNativeState);
        Assert.Equal(1, prearmed.LaunchedNativeState);

        var fired = session.Step(fireRequested: true);
        var shot = Assert.IsType<Rac1BombGloveShot>(fired.Shot);

        Assert.Equal(prearmed, shot.Projectile);
        Assert.Equal(6, shot.AmmoBefore);
        Assert.Equal(5, shot.AmmoAfter);
        Assert.Equal(5, fired.Ammo);
        Assert.Equal(20, fired.FireCooldownTicksRemaining);
        Assert.Equal(10, fired.ProjectileRearmTicksRemaining);
        Assert.Null(fired.PrearmedProjectile);
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
            if (tick == Rac1BombGlove.ProjectileRearmTicks)
            {
                Assert.NotNull(probe.PrearmedProjectile);
                Assert.Equal(10, probe.FireCooldownTicksRemaining);
                Assert.Equal(0, probe.ProjectileRearmTicksRemaining);
            }
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

        Assert.Null(session.Step(fireRequested: true).Shot);
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
        Assert.Null(session.Step(fireRequested: true).Shot);
        Assert.Equal(0, session.Probe().Ammo);
    }

    [Fact]
    public void ImpactAdmitsOnlyRepresentativeGoalOneHostileAndOnlyOnce()
    {
        var session = new Rac1BombGloveSession(initialAmmo: 2);
        var shot = Assert.IsType<Rac1BombGloveShot>(session.Step(true).Shot);
        var crate = Dynamic(Rac1BoltCrate.NativeClassId, instanceIndex: 89);
        var hostile = Dynamic(Rac1Class749Hostile.NativeClassId, instanceIndex: 149);

        Assert.Null(session.ResolveGoal1Impact(shot.Projectile.ProjectileId, crate, Rac1BoltCrate.ActiveNativeState));
        var damage = Assert.IsType<Rac1BombGloveDamageResult>(
            session.ResolveGoal1Impact(shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));

        Assert.Equal(Rac1Class749Hostile.NativeClassId, damage.TargetNativeClassId);
        Assert.Equal(1d, damage.NativeDamage);
        Assert.Equal(0x00010000u, damage.NativeDamageFlags);
        Assert.Null(session.ResolveGoal1Impact(shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));
    }

    [Theory]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFd)]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFe)]
    public void ImpactRejectsRecoveredTerminalTargetStates(int terminalState)
    {
        var session = new Rac1BombGloveSession(initialAmmo: 1);
        var shot = Assert.IsType<Rac1BombGloveShot>(session.Step(true).Shot);
        var hostile = Dynamic(Rac1Class749Hostile.NativeClassId, instanceIndex: 149);

        Assert.Null(session.ResolveGoal1Impact(shot.Projectile.ProjectileId, hostile, terminalState));
    }

    [Fact]
    public void RepresentativeImpactDrivesRecoveredClass749DamageState()
    {
        var hostile = Class749(instanceIndex: 149, health: 1f);
        var hostileSession = Registered(hostile);
        var weapon = new Rac1BombGloveSession(initialAmmo: 1);
        var shot = Assert.IsType<Rac1BombGloveShot>(weapon.Step(true).Shot);
        var damage = Assert.IsType<Rac1BombGloveDamageResult>(
            weapon.ResolveGoal1Impact(shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));

        var damaged = hostileSession.ApplyBombGloveDamage(hostile, damage);
        Assert.Equal(0f, damaged.Health);
        Assert.Equal(Rac1Class749Hostile.DamageNativeState, damaged.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, damaged.EntityState.Presentation.Presence);
    }

    [Fact]
    public void UnprovenClass749HealthStillRefusesBombGloveConsequence()
    {
        var hostile = Class749(instanceIndex: 149, health: 2f);
        var hostileSession = Registered(hostile);
        var weapon = new Rac1BombGloveSession(initialAmmo: 1);
        var shot = Assert.IsType<Rac1BombGloveShot>(weapon.Step(true).Shot);
        var damage = Assert.IsType<Rac1BombGloveDamageResult>(
            weapon.ResolveGoal1Impact(shot.Projectile.ProjectileId, hostile, Rac1Class749Hostile.TargetSearchNativeState));

        Assert.Throws<NotSupportedException>(() =>
            hostileSession.ApplyBombGloveDamage(hostile, damage));
        Assert.Equal(2f, hostileSession.Probe(hostile).Health);
    }

    private static Rac1Class749HostileSession Registered(RuntimeDynamicObject source)
    {
        var session = new Rac1Class749HostileSession();
        session.RegisterRepresentative(source, RuntimeEntityState.FromAuthored(source));
        return session;
    }

    private static RuntimeDynamicObject Class749(int instanceIndex, float health)
    {
        var pvar = new byte[Rac1Class749Hostile.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class749Hostile.HealthOffset, sizeof(int)),
            BitConverter.SingleToInt32Bits(health));
        return Dynamic(Rac1Class749Hostile.NativeClassId, instanceIndex,
            [new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar)]);
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

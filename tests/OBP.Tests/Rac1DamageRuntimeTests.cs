using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1DamageRuntimeTests
{
    [Fact]
    public void WrenchDamageCarriesPlayerAsSourceAndOwner()
    {
        var target = new Rac1MobyRuntimeKey(
            Rac1BoltCrate.NativeClassId,
            89);
        var damage = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);

        var transport = Rac1DamageRuntime.FromWrench(target, damage);

        Assert.Equal(Rac1GameplayEntityRef.Player, transport.Source);
        var owner = Assert.IsType<Rac1GameplayDamageOwner>(transport.Owner);
        Assert.True(owner.HasRuntimeIdentity);
        Assert.Equal(Rac1GameplayEntityRef.Player, owner.RuntimeEntity);
        Assert.Equal(
            Rac1GameplayEntityRef.Moby(target),
            transport.Target);
        Assert.Equal(damage.NativeDamage, transport.NativeDamage);
        Assert.Equal(damage.NativeDamageFlags, transport.NativeDamageFlags);
    }

    [Fact]
    public void Class749AttackCarriesSourceMobyAsExactOwner()
    {
        var source = new Rac1MobyRuntimeKey(
            Rac1Class749Hostile.NativeClassId,
            149);
        var attack = new Rac1Class749AttackEvent(
            Rac1Class749Hostile.AttackMarker,
            Rac1Class749Hostile.AttackDamage);

        var transport = Rac1DamageRuntime.FromClass749Attack(source, attack);

        var sourceRef = Rac1GameplayEntityRef.Moby(source);
        Assert.Equal(sourceRef, transport.Source);
        var owner = Assert.IsType<Rac1GameplayDamageOwner>(transport.Owner);
        Assert.True(owner.HasRuntimeIdentity);
        Assert.Equal(sourceRef, owner.RuntimeEntity);
        Assert.Equal(Rac1GameplayEntityRef.Player, transport.Target);
        Assert.Equal(attack.NativeMarker, transport.NativeMarker);
    }

    [Fact]
    public void BombGloveDamageRetainsWeaponOwnerClassWithoutInventingOwnerInstance()
    {
        var damage = new Rac1BombGloveDamageResult(
            ProjectileId: 7,
            TargetNativeClassId: Rac1Class749Hostile.NativeClassId,
            TargetInstanceIndex: 149,
            NativeDamage: Rac1BombGlove.NativeDamage,
            NativeDamageFlags: Rac1BombGlove.NativeDamageFlags);

        var transport = Rac1DamageRuntime.FromBombGlove(damage);

        Assert.Equal(
            Rac1GameplayEntityRef.Projectile(
                Rac1BombGlove.NativeProjectileClassId,
                projectileId: 7),
            transport.Source);
        var owner = Assert.IsType<Rac1GameplayDamageOwner>(transport.Owner);
        Assert.Equal(Rac1BombGlove.NativeWeaponClassId, owner.NativeClassId);
        Assert.False(owner.HasRuntimeIdentity);
        Assert.Null(owner.RuntimeEntity);
        Assert.Equal(
            Rac1GameplayEntityRef.Moby(
                new Rac1MobyRuntimeKey(
                    Rac1Class749Hostile.NativeClassId,
                    149)),
            transport.Target);
        Assert.Equal(
            Rac1NativeDamageHandoffKind.ContactVolume,
            transport.HandoffKind);
    }

    [Fact]
    public void DamageOwnerRejectsRuntimeIdentityFromDifferentNativeClass()
    {
        var mismatched = new Rac1GameplayDamageOwner(
            NativeClassId: Rac1BombGlove.NativeWeaponClassId,
            RuntimeEntity: Rac1GameplayEntityRef.Projectile(
                Rac1BombGlove.NativeProjectileClassId,
                projectileId: 1));

        Assert.Throws<InvalidDataException>(() =>
            new Rac1GameplayDamageEvent(
                Rac1GameplayEntityRef.Player,
                Rac1GameplayEntityRef.Player,
                nativeDamage: 1d,
                owner: mismatched));
    }
}

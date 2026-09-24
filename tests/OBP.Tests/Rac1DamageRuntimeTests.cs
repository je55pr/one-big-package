using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1DamageRuntimeTests
{
    [Fact]
    public void Class749PlayerDamagePreservesMarkerWithoutInventingFlags()
    {
        var source = new Rac1MobyRuntimeKey(
            Rac1Class749Hostile.NativeClassId,
            149);
        var attack = new Rac1Class749AttackEvent(
            Rac1Class749Hostile.AttackMarker,
            Rac1Class749Hostile.AttackDamage);

        var damage = Rac1DamageRuntime.FromClass749Attack(source, attack);

        Assert.Equal(Rac1GameplayEntityRef.Moby(source), damage.Source);
        Assert.Equal(Rac1GameplayEntityRef.Player, damage.Target);
        Assert.Equal(Rac1Class749Hostile.AttackDamage, damage.NativeDamage);
        Assert.Equal(Rac1Class749Hostile.AttackMarker, damage.NativeMarker);
        Assert.Null(damage.NativeDamageFlags);
        Assert.Null(damage.DamageEnvelope);
        Assert.Null(damage.HandoffKind);
    }

    [Fact]
    public void WrenchDamageCarriesPlayerToMobyIdentityAndNativeFlags()
    {
        var target = new Rac1MobyRuntimeKey(
            Rac1Class749Hostile.NativeClassId,
            149);
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);
        var damage = Rac1DamageRuntime.FromWrench(target, wrench);

        Assert.Equal(Rac1GameplayEntityRef.Player, damage.Source);
        Assert.Equal(Rac1GameplayEntityRef.Moby(target), damage.Target);
        Assert.Equal(wrench.DamageEnvelope, damage.DamageEnvelope);
        Assert.Null(damage.NativeMarker);
        Assert.Null(damage.HandoffKind);
    }

    [Fact]
    public void BombDamageCarriesProjectileIdentityAndRecoveredContactVolumeHandoff()
    {
        var target = new Rac1MobyRuntimeKey(
            Rac1Class749Hostile.NativeClassId,
            149);
        var bomb = new Rac1BombGloveDamageResult(
            ProjectileId: 7,
            TargetNativeClassId: Rac1Class749Hostile.NativeClassId,
            TargetInstanceIndex: 149,
            NativeDamage: Rac1BombGlove.NativeDamage,
            NativeDamageFlags: Rac1BombGlove.NativeDamageFlags);

        var damage = Rac1DamageRuntime.FromBombGlove(bomb);

        Assert.Equal(
            Rac1GameplayEntityRef.Projectile(
                Rac1BombGlove.NativeProjectileClassId,
                7),
            damage.Source);
        Assert.Equal(Rac1GameplayEntityRef.Moby(target), damage.Target);
        Assert.Equal(bomb.DamageEnvelope, damage.DamageEnvelope);
        Assert.Equal(
            Rac1NativeDamageHandoffKind.ContactVolume,
            damage.HandoffKind);
    }
}

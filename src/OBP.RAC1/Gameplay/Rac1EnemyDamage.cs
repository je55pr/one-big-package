namespace OBP.RAC1.Gameplay;

/// <summary>Admitted player-owned native descriptors, independent of enemy health/state layout.</summary>
public static class Rac1EnemyDamage
{
    public static bool IsClass1440Contact(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby && damage.Source.NativeClassId == 1440 &&
        damage.Owner?.RuntimeEntity == damage.Source && damage.NativeDamageFlags == 0x00010001 &&
        damage.NativeDamage == 1 && damage.NativeMarker is null;

    public static bool IsPlayerWeapon(Rac1GameplayDamageEvent damage)
    {
        bool wrench = (damage.Source == Rac1GameplayEntityRef.Player || damage.Source == Rac1GameplayEntityRef.PlayerWrench) &&
            (damage.Source != Rac1GameplayEntityRef.PlayerWrench || damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player) &&
            damage.NativeDamageFlags == Rac1WrenchAttackProfile.NativeDamageFlags && damage.NativeDamage is 1 or 2;
        bool bomb = damage.Source.Kind == Rac1GameplayEntityKind.Projectile &&
            damage.Source.NativeClassId == Rac1BombGlove.NativeProjectileClassId &&
            damage.Owner?.NativeClassId == Rac1BombGlove.NativeWeaponClassId &&
            damage.NativeDamageFlags == Rac1BombGlove.NativeDamageFlags && damage.NativeDamage == Rac1BombGlove.NativeDamage;
        return (wrench || bomb) && damage.NativeMarker is null;
    }
}

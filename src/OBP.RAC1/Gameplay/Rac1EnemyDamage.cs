namespace OBP.RAC1.Gameplay;

/// <summary>Admitted player-owned descriptors, independent of enemy health/state layout.</summary>
public static class Rac1EnemyDamage
{
    public static bool IsBlaster(Rac1GameplayDamageEvent damage) =>
        damage.Source == new Rac1GameplayEntityRef(Rac1GameplayEntityKind.PlayerEquipment, 168, 15) &&
        damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player &&
        damage.NativeDamageFlags == Rac1WeaponProfiles.BlasterFlags && damage.NativeDamage == Rac1WeaponProfiles.BlasterDamage;

    /// <summary>
    /// 0x25a478 replaces Blaster hit-record damage from table0x160080 indexed
    /// by victim health-state+8 (PVar+0x28 for these two enemy families).
    /// The raw projectile descriptor stays intact in the transported event.
    /// </summary>
    public static float ResolveNativeDamage(Rac1GameplayDamageEvent damage, ReadOnlySpan<byte> victimPVar)
    {
        if (!IsBlaster(damage)) return checked((float)damage.NativeDamage);
        if (victimPVar.Length <= 0x28) throw new ArgumentException("Victim has no health-state kind.", nameof(victimPVar));
        return victimPVar[0x28] switch
        {
            0 => 1f,
            1 => BitConverter.Int32BitsToSingle(0x3f2ac083),
            2 or 3 => .5f,
            _ => throw new NotSupportedException("Unrecovered Blaster victim damage kind."),
        };
    }

    public static bool IsClass1440Contact(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby && damage.Source.NativeClassId == 1440 &&
        damage.Owner?.RuntimeEntity == damage.Source && damage.NativeDamageFlags == 0x00010001 &&
        damage.NativeDamage == 1 && damage.NativeMarker is null;

    public static bool IsPlayerWeapon(Rac1GameplayDamageEvent damage)
    {
        bool adapted = IsHostAdaptedWeapon(damage);
        bool wrench = (damage.Source == Rac1GameplayEntityRef.Player || damage.Source == Rac1GameplayEntityRef.PlayerWrench) &&
            (damage.Source != Rac1GameplayEntityRef.PlayerWrench || damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player) &&
            damage.NativeDamageFlags == Rac1WrenchAttackProfile.NativeDamageFlags && damage.NativeDamage is 1 or 2;
        bool bomb = damage.Source.Kind == Rac1GameplayEntityKind.Projectile &&
            damage.Source.NativeClassId == Rac1BombGlove.NativeProjectileClassId &&
            damage.Owner?.NativeClassId == Rac1BombGlove.NativeWeaponClassId &&
            damage.NativeDamageFlags == Rac1BombGlove.NativeDamageFlags && damage.NativeDamage == Rac1BombGlove.NativeDamage;
        bool direct = damage.Source.Kind == Rac1GameplayEntityKind.PlayerEquipment &&
            damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player &&
            ((damage.Source.NativeClassId, damage.Source.RuntimeId, damage.NativeDamageFlags, damage.NativeDamage) is
                (168, 15, 0x10001, .25) or (176, 16, 0x10000, 1));
        // Suck's outgoing victim callback is still a bounded reconstruction;
        // keep that admission visibly separate from recovered gun envelopes.
        bool sucked = damage.Source == new Rac1GameplayEntityRef(Rac1GameplayEntityKind.PlayerEquipment, 849, 9) &&
            damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player &&
            damage.NativeDamage == Rac1WeaponReconstructionPolicy.SuckDamage &&
            damage.NativeDamageFlags == Rac1WeaponReconstructionPolicy.SuckDamageFlags;
        bool mine = damage.Source.Kind == Rac1GameplayEntityKind.Projectile && damage.Source.NativeClassId == 74 &&
            damage.Owner?.NativeClassId == 190 && damage.NativeDamage == 3 && damage.NativeDamageFlags == 0x00810000;
        bool doom = damage.Source.Kind == Rac1GameplayEntityKind.Projectile && damage.Source.NativeClassId == 186 &&
            damage.Owner?.NativeClassId == 229 && damage.NativeDamage == 3 && damage.NativeDamageFlags == 0x00010000;
        return (adapted || wrench || bomb || direct || sucked || mine || doom || Rac1ArsenalRules.AdmitsDamage(damage)) && damage.NativeMarker is null;
    }

    /// <summary>
    /// OBP opt-in foreign-weapon envelope. No foreign class/flags are treated
    /// as R&C1 native identities. Only the two Veldin enemy consumers are
    /// admitted; class749's bounded consequence requires a lethal hit.
    /// </summary>
    private static bool IsHostAdaptedWeapon(Rac1GameplayDamageEvent damage) =>
        damage.HostWeaponIntent is { } intent && intent.Shot.Weapon.SourceGame is "rac2" or "rac3" &&
        intent.Shot.Weapon.NativeItemId >= 0 && intent.Target.SourceGame == "rac1" &&
        intent.Target.NativeClassId is 749 or 1440 &&
        (intent.Target.NativeClassId != 749 || intent.SourceDamage >= 1) &&
        damage.Source == Rac1GameplayEntityRef.Player && damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player &&
        damage.Target.MatchesMoby(new(intent.Target.NativeClassId, intent.Target.InstanceIndex)) &&
        float.IsFinite(intent.SourceDamage) && intent.SourceDamage > 0 && damage.NativeDamage == intent.SourceDamage &&
        damage.NativeDamageFlags == 0x00010000;
}

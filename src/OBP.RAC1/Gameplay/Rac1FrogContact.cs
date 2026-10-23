using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>Native class-827 state-12 animation frame 10..15 calls common
/// FUN_L00_002599e8 with 1 damage and 0.333 radius. The exact flags and
/// joint/sphere solver remain unknown; null flags preserve that distinction.</summary>
public static class Rac1FrogContact
{
    public const float NativeRadius = .333f;
    public const int Damage = 1;

    public static Rac1GameplayDamageEvent DamageToPlayer(int instance)
    {
        var source = Rac1GameplayEntityRef.Moby(new(Rac1FrogAuthored.ClassId, instance));
        return new(source, Rac1GameplayEntityRef.Player, Damage,
            owner: Rac1GameplayDamageOwner.FromEntity(source));
    }

    public static bool IsAdmitted(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby &&
        damage.Source.NativeClassId == Rac1FrogAuthored.ClassId &&
        damage.Target == Rac1GameplayEntityRef.Player &&
        damage.Owner?.RuntimeEntity == damage.Source &&
        damage.NativeDamage == Damage && damage.NativeDamageFlags is null && damage.NativeMarker is null;
}

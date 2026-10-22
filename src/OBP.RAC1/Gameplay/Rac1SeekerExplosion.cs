using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Verified R&C1 Seeker state-7 shared explosion descriptor.
/// Native 0x2c5990 invokes 0x25e450 with radius=2, damage=1;
/// the common helper emits flags 0x810001 and Moby admission mask 0x10.
/// </summary>
public static class Rac1SeekerExplosion
{
    public const float Radius = 2f;
    public const int Damage = 1;
    public const uint NativeDamageFlags = 0x00810001;
    public const int NativeMobyMask = 0x10;

    public static bool IsNativeExplosion(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby &&
        damage.Source.NativeClassId == Rac1SeekerSession.ClassId &&
        damage.NativeDamage == Damage &&
        damage.NativeDamageFlags == NativeDamageFlags &&
        damage.Owner?.RuntimeEntity == damage.Source &&
        damage.NativeMarker is null;

    public static Rac1GameplayDamageEvent DamageTo(int sourceInstanceIndex, Rac1GameplayEntityRef target)
    {
        if (sourceInstanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceInstanceIndex));
        var source = Rac1GameplayEntityRef.Moby(new(Rac1SeekerSession.ClassId, sourceInstanceIndex));
        return new(source, target, Damage, NativeDamageFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(source));
    }
}

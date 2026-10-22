using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native class-572/865/866 lunge at animation time 15.5 calls the common
/// nearby effect with damage descriptor 0x10001 and 1.0f damage.
/// </summary>
public static class Rac1AmoeboidAttack
{
    public const uint NativeFlags = 0x00010001;
    public const int NativeDamage = 1;
    public static Rac1GameplayDamageEvent ToPlayer(int cls, int instanceIndex)
    {
        if (!Rac1AmoeboidAuthored.IsFamily(cls) || instanceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cls));
        var source = Rac1GameplayEntityRef.Moby(new(cls, instanceIndex));
        return new(source, Rac1GameplayEntityRef.Player, NativeDamage, NativeFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(source));
    }

    public static bool IsNativePlayerAttack(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby &&
        Rac1AmoeboidAuthored.IsFamily(damage.Source.NativeClassId) &&
        damage.Target == Rac1GameplayEntityRef.Player &&
        damage.NativeDamage == NativeDamage &&
        damage.NativeDamageFlags == NativeFlags &&
        damage.Owner?.RuntimeEntity == damage.Source &&
        damage.NativeMarker is null;
}

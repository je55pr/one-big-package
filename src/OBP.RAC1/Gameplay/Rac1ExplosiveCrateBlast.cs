using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class-505 state-3 expanding blast from the original shared crate updater.
/// The source sets Burst.unk1C = 2.0, then dispatches the Moby sphere with
/// unk14 = 1 and the hero sphere with unk14 = 0x30000. Keep those envelopes
/// separate, with the same Moby owner and a strict admitted damage amount.
/// </summary>
public static class Rac1ExplosiveCrateBlast
{
    public const int Damage = 2;
    public const uint MobyDamageFlags = 0x00000001;
    public const uint PlayerDamageFlags = 0x00030000;

    public static Rac1GameplayDamageEvent ToPlayer(int instanceIndex) =>
        Create(instanceIndex, Rac1GameplayEntityRef.Player, PlayerDamageFlags);

    public static Rac1GameplayDamageEvent ToMoby(int instanceIndex, Rac1MobyRuntimeKey target) =>
        Create(instanceIndex, Rac1GameplayEntityRef.Moby(target), MobyDamageFlags);

    public static bool IsNativePlayerBlast(Rac1GameplayDamageEvent damage) =>
        MatchesNativeEnvelope(damage, PlayerDamageFlags) &&
        damage.Target == Rac1GameplayEntityRef.Player;

    public static bool IsNativeMobyBlast(Rac1GameplayDamageEvent damage) =>
        MatchesNativeEnvelope(damage, MobyDamageFlags) &&
        damage.Target.Kind == Rac1GameplayEntityKind.Moby;

    private static Rac1GameplayDamageEvent Create(
        int instanceIndex, Rac1GameplayEntityRef target, uint flags)
    {
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        var source = Rac1GameplayEntityRef.Moby(new(Rac1ExplosiveCrateSession.ClassId, instanceIndex));
        return new(source, target, Damage, flags,
            owner: Rac1GameplayDamageOwner.FromEntity(source));
    }

    private static bool MatchesNativeEnvelope(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby &&
        damage.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId &&
        damage.NativeDamage == Damage &&
        damage.NativeMarker is null &&
        damage.Owner?.RuntimeEntity == damage.Source;

    private static bool MatchesNativeEnvelope(Rac1GameplayDamageEvent damage, uint flags) =>
        MatchesNativeEnvelope(damage) && damage.NativeDamageFlags == flags;
}

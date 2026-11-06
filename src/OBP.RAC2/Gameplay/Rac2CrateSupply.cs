using OBP.Core;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

/// <summary>Loaded Oozla crate-family facts; source-specific rules, not player inventory policy.</summary>
public static class Rac2CrateSupply
{
    public const int NanotechClass = 501, AmmoClass = 511, AmmoPickupClass = 3291;
    public const int NanotechPerCloud = 1, ParticleCount = 8;
    public const float NanotechAttractionDistance = 10;
    public static bool IsCrate(int nativeClass) => nativeClass is 500 or NanotechClass or AmmoClass;
    public static bool IsSupply(int nativeClass) => nativeClass is NanotechClass or AmmoClass;

    public static byte? RespawnFlag(RuntimeDynamicObject source) =>
        source.SourceGame == ObpSourceGame.Rac2 && IsCrate(source.NativeClassId) &&
        source.NativePayloads?.FirstOrDefault(payload => payload.Format == "rac2-pvar")?.Data is { Length: > 0xc8 } pvar
            ? pvar[0xc8] : null;

    public static RuntimeEntityState? ApplyDamage(RuntimeDynamicObject source, RuntimeEntityState current,
        Rac2GameplayDamageEvent damage)
    {
        current.EnsureMatches(source);
        if (!damage.Target.Matches(source)) throw new InvalidOperationException("Crate damage target does not match its source.");
        if (!IsSupply(source.NativeClassId) || RespawnFlag(source) != 0 ||
            current.Presentation.Presence != RuntimeEntityPresence.Active ||
            !float.IsFinite(damage.Damage.DamageHp) || !Rac2CrateInteraction.ShouldBreakClass500(damage.Damage)) return null;
        // The common positive-damage path is 0x3912DC..94; state 3 +C8=0
        // deactivates both supply classes at 0x3914E4..0x391548.
        return current.WithPresence(RuntimeEntityPresence.Inactive);
    }

    // 0x30F77C..98 returns two objects for RNG(5)==0, otherwise one.
    public static int AmmoPieceCount(int randomBelowFive) => (uint)randomBelowFive < 5
        ? randomBelowFive == 0 ? 2 : 1 : throw new ArgumentOutOfRangeException(nameof(randomBelowFive));

    public static int SelectAmmo(IReadOnlyList<(int Index, int Count, int Capacity)> owned, Func<int, int> below)
    {
        var eligible = owned.Where(entry => entry.Index is >= 0 and < 56 and not 31 and not 45 && entry.Capacity > 0).ToArray();
        var missing = eligible.Where(entry => entry.Count < entry.Capacity).ToArray();
        var pool = missing.Length > 0 ? missing : eligible;
        return pool.Length == 0 ? -1 : pool[below(pool.Length)].Index;
    }
}

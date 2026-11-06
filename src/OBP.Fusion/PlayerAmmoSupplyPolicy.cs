using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

public sealed record PlayerAmmoSupply(SourceWeaponKey Weapon, int Count, int Capacity, int Amount);

/// <summary>
/// Explicit OBP mixed-arsenal supply policy: choose an owned implemented weapon
/// with room at collection time. Each recipient supplies its own
/// native grant amount; equal item numbers across sources are never aliases.
/// </summary>
public static class PlayerAmmoSupplyPolicy
{
    // OBP presentation allowance so the crate has time to emit a visible drop.
    public const int DropGraceTicks = 12;

    public static PlayerAmmoSupply? Select(IReadOnlyList<PlayerAmmoSupply> owned, Func<int, int> below)
    {
        var missing = owned.Where(item => item.Capacity > 0 && item.Amount > 0 && item.Count < item.Capacity).ToArray();
        return missing.Length == 0 ? null : missing[below(missing.Length)];
    }
}

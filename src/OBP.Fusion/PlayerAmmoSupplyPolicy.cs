using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

public sealed record PlayerAmmoSupply(SourceWeaponKey Weapon, int Count, int Capacity, int Amount,
    AmmoPickupFamily? Family = null);

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

    /// <summary>Recipients remain unassigned within their emitted family until collection.</summary>
    public static PlayerAmmoSupply? Select(IReadOnlyList<PlayerAmmoSupply> owned, AmmoPickupFamily family, Func<int, int> below) =>
        Select(owned.Where(item => item.Family == family).ToArray(), below);

    /// <summary>
    /// Fix the visible family when the piece is emitted. Prefer families with missing ammo;
    /// an all-full owned arsenal may still leave a useful physical drop for later.
    /// Each missing family gets one ticket, independently of its number of registered weapons.
    /// </summary>
    public static AmmoPickupFamily? SelectDropFamily(IReadOnlyList<PlayerAmmoSupply> owned, Func<int, int> below)
    {
        var usable = owned.Where(item => item.Family is { } family && Enum.IsDefined(family) &&
            item.Capacity > 0 && item.Amount > 0 && item.Count >= 0).ToArray();
        var missing = usable.Where(item => item.Count < item.Capacity).ToArray();
        var families = (missing.Length > 0 ? missing : usable).Select(item => item.Family.GetValueOrDefault()).Distinct().ToArray();
        return families.Length == 0 ? null : families[below(families.Length)];
    }
}

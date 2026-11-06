using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

public sealed record PlayerAmmoSupply(SourceWeaponKey Weapon, int Count, int Capacity, int Amount);

/// <summary>
/// Explicit OBP mixed-arsenal supply policy: choose an owned implemented weapon
/// with room, otherwise an owned full weapon. Each recipient supplies its own
/// native grant amount; equal item numbers across sources are never aliases.
/// </summary>
public static class PlayerAmmoSupplyPolicy
{
    public static PlayerAmmoSupply? Select(IReadOnlyList<PlayerAmmoSupply> owned, Func<int, int> below)
    {
        var eligible = owned.Where(item => item.Capacity > 0 && item.Amount > 0).ToArray();
        var missing = eligible.Where(item => item.Count < item.Capacity).ToArray();
        var pool = missing.Length > 0 ? missing : eligible;
        return pool.Length == 0 ? null : pool[below(pool.Length)];
    }
}

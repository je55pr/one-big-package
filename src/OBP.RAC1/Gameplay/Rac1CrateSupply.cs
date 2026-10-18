using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Crate resource selection: class501 owns class806's one-point Nanotech
/// cloud; class511's first-break preferred item comes from PVar+cb. The
/// common ammo constructor uses the item's native descriptor, not bolt classes.
/// </summary>
public static class Rac1CrateSupply
{
    public const int NanotechCrateClass = 501;
    public const int NanotechCloudClass = 806;
    public const int NanotechPerCloud = 1;
    public const float NanotechAttractDistance = 10;
    public const int BombGloveAmmoClass = 226;
    public const int BombGloveAmmoPerPickup = 3;
    private static readonly int[] PreferredItems = [10, 11, 13, 15, 16, 17, 19, 20, 23, 24, 25];
    // Item descriptor +3a -> Moby class; ammo descriptor +0c -> per-piece grant.
    public sealed record AmmoDefinition(int Item, int ModelClass, int Amount);
    public static IReadOnlyList<AmmoDefinition> AmmoDefinitions { get; } = Array.AsReadOnly(new AmmoDefinition[]
    {
        new(10, 226, 3), new(11, 204, 2), new(13, 222, 1), new(15, 1006, 25),
        new(16, 214, 20), new(17, 225, 5), new(19, 213, 20), new(20, 223, 1),
        new(23, 1438, 1), new(24, 1447, 1), new(25, 1449, 2),
    });

    public static int PreferredAmmoItem(RuntimeDynamicObject source)
    {
        if (source.NativeClassId != 511)
            throw new ArgumentException("Expected an R&C1 ammunition crate.", nameof(source));
        byte[] pvar = Rac1BoltCrate.RequirePVar(source);
        int selector = pvar[0xcb];
        return (uint)selector < PreferredItems.Length ? PreferredItems[selector] : -1;
    }

    public static int SelectFirstAmmoPieceCount(Rac1PickupRandom random) => random.Below(5) == 0 ? 2 : 1;

    /// <summary>
    /// First-break preferred item bypasses the fallback selector (0x257238..80).
    /// The fallback prefers owned, non-full items, then any owned ammo item.
    /// OBP uses descriptor order; the native twelve-slot runtime order is unresolved.
    /// </summary>
    public static AmmoDefinition SelectFirstAmmo(RuntimeDynamicObject source, Rac1WeaponInventory inventory, Rac1PickupRandom random)
    {
        int preferred = PreferredAmmoItem(source);
        var authored = AmmoDefinitions.FirstOrDefault(definition => definition.Item == preferred);
        if (authored is not null) return authored;
        var owned = AmmoDefinitions.Where(definition => inventory.OwnsNativeItem(definition.Item)).ToArray();
        var available = owned.Where(definition => inventory.GetAmmo(definition.Item) < Rac1ItemAmmoDescriptors.Get(definition.Item).MaxAmmo).ToArray();
        var pool = available.Length > 0 ? available : owned;
        // 0x2573c0 returns item10 if there are no owned entries.
        return pool.Length > 0 ? pool[random.Below(pool.Length)] : AmmoDefinitions[0];
    }
}

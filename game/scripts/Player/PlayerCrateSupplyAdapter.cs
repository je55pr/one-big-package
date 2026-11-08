using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>Selected player's source-qualified recipients for OBP mixed-world crate supplies.</summary>
internal static class PlayerCrateSupplyAdapter
{
    internal static IReadOnlyList<PlayerAmmoSupply> Read(PlayerSessionNativeState? player,
        IReadOnlyList<EquipmentCatalogEntry> equipment)
    {
        if (player is null) return [];
        var result = new List<PlayerAmmoSupply>();
        foreach (var entry in equipment.Where(entry => entry.Selectable && entry.State.Owned &&
            entry.Definition.Category == EquipmentCategory.Weapon))
        {
            if (entry.Definition.Item is not { } key) continue;
            int amount = PickupAmount(key);
            if (amount <= 0 || PlayerAmmoFamilies.ForWeapon(key) is not { } family) continue;
            var status = player.Weapons.Status(key);
            if (status.Capacity > 0) result.Add(new(key, status.Ammo, status.Capacity, amount, family));
        }
        return result;
    }

    private static int PickupAmount(SourceWeaponKey key)
    {
        if (key.SourceGame == ObpSourceGame.Rac1)
            return Rac1CrateSupply.AmmoDefinitions.FirstOrDefault(definition => definition.Item == key.NativeItemId)?.Amount ?? 0;
        if (key == FusionWeaponSession.Lancer) return Rac2LancerProfile.AmmoPickupAmount;
        return key == FusionWeaponSession.ShockCannon ? Rac3ShockCannonProfile.AmmoPickupAmount : 0;
    }

    internal static int Grant(PlayerSessionNativeState? player, SourceWeaponKey weapon, int amount)
    {
        if (player is null || !player.Life.Alive) return 0;
        if (weapon.SourceGame == ObpSourceGame.Rac1 && player.Inventory.OwnsNativeItem(weapon.NativeItemId))
            return player.Inventory.GrantAmmoClamped(weapon.NativeItemId, amount).AmmoGranted;
        if (player.LoadoutPolicy != PlayerLoadoutPolicy.DevelopmentAvailable) return 0;
        return weapon == FusionWeaponSession.Lancer ? player.Weapons.LancerSession.AddAmmo(amount)
            : weapon == FusionWeaponSession.ShockCannon ? player.Weapons.CannonSession.AddAmmo(amount) : 0;
    }
}

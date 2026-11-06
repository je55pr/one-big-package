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
        IReadOnlyList<EquipmentCatalogEntry> equipment, int lancerAmount)
    {
        if (player is null) return [];
        var result = new List<PlayerAmmoSupply>();
        foreach (var entry in equipment.Where(entry => entry.State.Owned && entry.Definition.Category == EquipmentCategory.Weapon &&
            entry.Definition.Implementation == EquipmentImplementation.Implemented &&
            entry.Usability is not (EquipmentUsability.MissingSource or EquipmentUsability.UnsupportedBuild)))
        {
            if (entry.Definition.Item is not { } key) continue;
            int amount = key.SourceGame == ObpSourceGame.Rac1
                ? Rac1CrateSupply.AmmoDefinitions.FirstOrDefault(definition => definition.Item == key.NativeItemId)?.Amount ?? 0
                : key == FusionWeaponSession.Lancer ? lancerAmount
                : key == FusionWeaponSession.ShockCannon ? Rac3ShockCannonProfile.AmmoPickupAmount : 0;
            if (amount <= 0) continue;
            var status = player.Weapons.Status(key);
            if (status.Capacity > 0) result.Add(new(key, status.Ammo, status.Capacity, amount));
        }
        return result;
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

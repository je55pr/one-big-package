using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Player;

/// <summary>Movement-related item identities from the retail equipment table.</summary>
public enum Rac1TraversalEquipmentId
{
    HeliPack = 2,
    ThrusterPack = 3,
    HydroPack = 4,
    OxygenMask = 6,
    Swingshot = 12,
    Magneboots = 28,
    Grindboots = 29,
}

public readonly record struct Rac1TraversalEquipmentDescriptor(
    Rac1TraversalEquipmentId Id,
    int NameTextId,
    int Category,
    int EquipmentDomain,
    int PrimaryMobyClass);

/// <summary>
/// Ownership is the persistent item byte, independently of remembered equipment
/// and the transient held item. This projection never acquires or equips items.
/// </summary>
public readonly record struct Rac1MovementCapabilities(
    bool HeliPack,
    bool ThrusterPack,
    bool HydroPack,
    bool OxygenMask,
    bool Swingshot,
    bool Magneboots,
    bool Grindboots)
{
    public static Rac1MovementCapabilities FromInventory(Rac1WeaponInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return new(
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.HeliPack),
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.ThrusterPack),
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.HydroPack),
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.OxygenMask),
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.Swingshot),
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.Magneboots),
            inventory.OwnsNativeItem((int)Rac1TraversalEquipmentId.Grindboots));
    }

    public bool Owns(Rac1TraversalEquipmentId equipment) => equipment switch
    {
        Rac1TraversalEquipmentId.HeliPack => HeliPack,
        Rac1TraversalEquipmentId.ThrusterPack => ThrusterPack,
        Rac1TraversalEquipmentId.HydroPack => HydroPack,
        Rac1TraversalEquipmentId.OxygenMask => OxygenMask,
        Rac1TraversalEquipmentId.Swingshot => Swingshot,
        Rac1TraversalEquipmentId.Magneboots => Magneboots,
        Rac1TraversalEquipmentId.Grindboots => Grindboots,
        _ => throw new ArgumentOutOfRangeException(nameof(equipment)),
    };

    /// <summary>
    /// Decode save block 32's backpack entry. The live active pack is separately
    /// loaded/transitioned by retail and must not be inferred from ownership alone.
    /// </summary>
    public Rac1TraversalEquipmentId? RememberedBackpack(Rac1WeaponInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        int id = inventory.EquippedGadgets[3];
        return id is 2 or 3 or 4 && Owns((Rac1TraversalEquipmentId)id)
            ? (Rac1TraversalEquipmentId)id
            : null;
    }

    public static Rac1TraversalEquipmentDescriptor Descriptor(Rac1TraversalEquipmentId equipment) =>
        equipment switch
        {
            Rac1TraversalEquipmentId.HeliPack => new(equipment, 0x4e6e, 3, 5, 0x25f),
            Rac1TraversalEquipmentId.ThrusterPack => new(equipment, 0x4e6d, 3, 5, 0x260),
            Rac1TraversalEquipmentId.HydroPack => new(equipment, 0x4e6f, 3, 5, 0x261),
            Rac1TraversalEquipmentId.OxygenMask => new(equipment, 0x4e6b, 2, 4, 0x509),
            Rac1TraversalEquipmentId.Swingshot => new(equipment, 0x4e61, 0, 1, 0xd0),
            Rac1TraversalEquipmentId.Magneboots => new(equipment, 0x4e67, 1, 2, 0xad),
            Rac1TraversalEquipmentId.Grindboots => new(equipment, 0x4e68, 1, 2, 0xc3),
            _ => throw new ArgumentOutOfRangeException(nameof(equipment)),
        };
}

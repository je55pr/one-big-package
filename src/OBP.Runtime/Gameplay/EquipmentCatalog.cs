using OBP.Core;
using System.Text.Json.Serialization;
using OBP.Runtime.Player;

namespace OBP.Runtime.Gameplay;

public enum EquipmentCategory { Weapon, Gadget, Appearance }
public enum EquipmentImplementation { Unimplemented, Implemented }
public enum EquipmentUsage { Handheld, Backpack, Passive, ContextAction }
public enum EquipmentResourceStatus { Unavailable, Available, NotRequired }
public enum EquipmentUsability { Ready, Unimplemented, MissingSource, UnsupportedBuild, MissingModel, Unowned, ContextBlocked }

/// <summary>Source-owned registration facts. Item keys reuse the existing weapon/item identity.</summary>
public sealed record EquipmentDefinition(string Id, string DisplayName, [property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame SourceGame, string BuildId,
    EquipmentCategory Category, EquipmentImplementation Implementation, SourceWeaponKey? Item = null,
    PlayerAvatarIdentity? Appearance = null, string? ConflictGroup = null, string? UnsupportedReason = null,
    EquipmentUsage Usage = EquipmentUsage.Handheld);

public sealed record EquipmentLiveState(bool Owned, bool Selected, bool Equipped,
    EquipmentResourceStatus Model, EquipmentResourceStatus Icon, int? Ammo = null, int? Capacity = null,
    bool ContextAllowed = true, string? ContextReason = null);

public sealed record EquipmentCatalogEntry(EquipmentDefinition Definition, EquipmentLiveState State,
    bool SourceAvailable, EquipmentUsability Usability, string? Reason)
{
    public bool Usable => Usability == EquipmentUsability.Ready;
    public bool Selectable => Definition.Implementation == EquipmentImplementation.Implemented && SourceAvailable &&
        Usability is not (EquipmentUsability.UnsupportedBuild or EquipmentUsability.MissingModel or EquipmentUsability.Unowned);
}

public sealed record PlayerEquipmentAssignmentsSnapshot(IReadOnlyList<SourceWeaponKey?> QuickSelect, IReadOnlyList<SourceWeaponKey> EnabledGadgets);
public sealed record EquipmentChangeResult(bool Accepted, string? Reason = null);

using OBP.Core;
using OBP.Fusion;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC2;
using OBP.RAC3;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>Composition registrations, not a replacement inventory or native item table.</summary>
public static class PlayerEquipmentRegistrations
{
    public static PlayerEquipmentCatalog Catalog { get; } = new(Definitions());
    public static readonly SourceWeaponKey Wrench = new(ObpSourceGame.Rac1, (int)Rac1WeaponId.Wrench);

    private static IEnumerable<EquipmentDefinition> Definitions()
    {
        foreach (var item in Rac1WeaponProfiles.Additional.Select(profile => profile.Item).Prepend(Rac1WeaponId.FirstRanged).Prepend(Rac1WeaponId.Wrench))
            yield return new($"rac1/item/{(int)item}", Rac1WeaponProfiles.Name(item), ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId,
                EquipmentCategory.Weapon, EquipmentImplementation.Implemented, new(ObpSourceGame.Rac1, (int)item));
        yield return new("rac2/item/30", "Lancer", ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, EquipmentCategory.Weapon,
            EquipmentImplementation.Implemented, FusionWeaponSession.Lancer);
        yield return new("rac3/item/43", "Shock Cannon", ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, EquipmentCategory.Weapon,
            EquipmentImplementation.Implemented, FusionWeaponSession.ShockCannon);
        yield return new("rac1/item/27", "Metal Detector", ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, EquipmentCategory.Gadget,
            EquipmentImplementation.Unimplemented, new(ObpSourceGame.Rac1, Rac1MetalDetectorSession.NativeGadgetItem),
            UnsupportedReason: "Native digging logic exists; the genuine handheld model and common equipment action are not implemented.");
        foreach (var (item, name, implemented) in new[]
        {
            (Rac1TraversalEquipmentId.HeliPack, "Heli-Pack", true), (Rac1TraversalEquipmentId.ThrusterPack, "Thruster-Pack", true),
            (Rac1TraversalEquipmentId.HydroPack, "Hydro-Pack", true), (Rac1TraversalEquipmentId.OxygenMask, "Oxygen Mask", true),
            (Rac1TraversalEquipmentId.Swingshot, "Swingshot", true), (Rac1TraversalEquipmentId.Magneboots, "Magneboots", false),
            (Rac1TraversalEquipmentId.Grindboots, "Grindboots", false),
        })
            yield return new($"rac1/item/{(int)item}", name, ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, EquipmentCategory.Gadget,
                implemented ? EquipmentImplementation.Implemented : EquipmentImplementation.Unimplemented, new(ObpSourceGame.Rac1, (int)item),
                ConflictGroup: item is Rac1TraversalEquipmentId.HeliPack or Rac1TraversalEquipmentId.ThrusterPack or Rac1TraversalEquipmentId.HydroPack ? "backpack" : null,
                UnsupportedReason: implemented ? null : "The source-owned traversal controller is not implemented.",
                Usage: item is Rac1TraversalEquipmentId.HeliPack or Rac1TraversalEquipmentId.ThrusterPack or Rac1TraversalEquipmentId.HydroPack
                    ? EquipmentUsage.Backpack : item == Rac1TraversalEquipmentId.Swingshot ? EquipmentUsage.ContextAction : EquipmentUsage.Passive);
        yield return Appearance(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, Rac1PlayerAvatarProvider.Instance.DefaultAvatarId,
            "rac1:ratchet:moby-class-0", true);
        yield return Appearance(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, "ratchet", "rac2:ratchet", false);
        yield return Appearance(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "ratchet", "rac3:ratchet", false);
    }

    private static EquipmentDefinition Appearance(ObpSourceGame source, string build, string avatar, string model, bool implemented) =>
        new($"{SourceIdentityWire.FormatSource(source)}/appearance/ratchet", $"Ratchet [{SourceIdentityWire.FormatSource(source)}]", source, build, EquipmentCategory.Appearance,
            implemented ? EquipmentImplementation.Implemented : EquipmentImplementation.Unimplemented,
            Appearance: new PlayerAvatarIdentity(source, build, avatar, model),
            UnsupportedReason: implemented ? null : "A genuine player provider and measured pose compatibility are not implemented.");
}

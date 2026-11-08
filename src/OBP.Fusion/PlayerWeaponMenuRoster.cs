using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

/// <summary>An OBP-authored display family, never a shared inventory or an equipment entitlement.</summary>
public sealed record PlayerWeaponMenuFamily(string Id, string Name, IReadOnlyList<SourceWeaponKey> Members)
{
    public EquipmentCatalogEntry? Recipient(IReadOnlyList<EquipmentCatalogEntry> catalog) => catalog
        .Where(entry => entry.Definition.Category == EquipmentCategory.Weapon &&
            entry.Definition.Item is { } key && Members.Contains(key))
        .OrderByDescending(entry => entry.Usable).ThenByDescending(entry => entry.Selectable).ThenByDescending(entry => entry.State.Equipped)
        .ThenByDescending(entry => entry.State.Selected).ThenByDescending(entry => entry.State.Owned)
        .ThenBy(entry => entry.Definition.SourceGame).FirstOrDefault();
}

/// <summary>
/// Fixed trilogy menu order: 47 weapon families and the wrench. Returning weapons and upgrades
/// share a display cell; every admitted action still targets its actual source-qualified registration.
/// Native IDs below are the supported discs' item/definition identities, not interchangeable slot numbers.
/// </summary>
public static class PlayerWeaponMenuRoster
{
    public static IReadOnlyList<PlayerWeaponMenuFamily> Families { get; } = Array.AsReadOnly(new[]
    {
        F("wrench", "OmniWrench", R1(8)),
        F("bomb-glove", "Bomb Glove", R1(10), R2(12, 115)),
        F("pyrocitor", "Pyrocitor", R1(16)),
        F("blaster", "Blaster", R1(15)),
        F("glove-of-doom", "Glove of Doom", R1(20), R3(87, 88, 89, 90, 91, 92, 93, 94)),
        F("mine-glove", "Mine Glove", R1(17)),
        F("taunter", "Taunter", R1(14)),
        F("suck-cannon", "Suck Cannon", R1(9), R3(135, 136, 137, 138, 139, 140, 141, 142)),
        F("devastator", "Devastator", R1(11)),
        F("walloper", "Walloper", R1(18), R2(53, 116)),
        F("visibomb", "Visibomb Gun", R1(13), R2(14, 117)),
        F("decoy-glove", "Decoy Glove", R1(25), R2(17, 118)),
        F("drone-device", "Drone Device", R1(24)),
        F("tesla-claw", "Tesla Claw", R1(19), R2(18, 114)),
        F("morph-o-ray", "Morph-o-Ray", R1(21)),
        F("ryno", "RYNO", R1(23)),
        F("lancer", "Lancer", R2(30, 60, 79, 80)),
        F("gravity-bomb", "Gravity Bomb", R2(42, 71, 81, 82)),
        F("chopper", "Chopper", R2(22, 65, 83, 84)),
        F("blitz-gun", "Blitz Gun", R2(26, 68, 91, 92)),
        F("pulse-rifle", "Pulse Rifle", R2(23, 66, 87, 88)),
        F("lava-gun", "Lava Gun", R2(29, 63, 95, 96), R3(17)),
        F("miniturret", "Miniturret Glove", R2(41, 64, 89, 90), R3(21)),
        F("seeker-gun", "Seeker Gun", R2(24, 67, 85, 86)),
        F("ryno-2", "RYNO II", R2(44)),
        F("synthenoid", "Synthenoid", R2(31, 61, 93, 94)),
        F("spiderbot", "Spiderbot Glove", R2(32, 78, 105, 106)),
        F("minirocket", "Minirocket Tube", R2(27, 69, 99, 100)),
        F("plasma-coil", "Plasma Coil", R2(28, 62, 101, 102), R3(16)),
        F("hoverbomb", "Hoverbomb Gun", R2(25, 70, 103, 104)),
        F("bouncer", "Bouncer", R2(37, 76, 97, 98), R3(19)),
        F("shield-charger", "Shield Charger", R2(45, 77, 107, 108), R3(22)),
        F("sheepinator", "Sheepinator", R2(16, 72, 109, 110)),
        F("zodiac", "Zodiac", R2(43)),
        F("clank-zapper", "Clank Zapper", R2(9, 73)),
        F("shock-blaster", "Shock Blaster", R3(39, 40, 41, 42, 43, 44, 45, 46)),
        F("n60-storm", "N60 Storm", R3(47, 48, 49, 50, 51, 52, 53, 54)),
        F("infector", "Infector", R3(55, 56, 57, 58, 59, 60, 61, 62)),
        F("annihilator", "Annihilator", R3(63, 64, 65, 66, 67, 68, 69, 70)),
        F("spitting-hydra", "Spitting Hydra", R3(71, 72, 73, 74, 75, 76, 77, 78)),
        F("disc-blade", "Disc Blade Gun", R3(79, 80, 81, 82, 83, 84, 85, 86)),
        F("rift-inducer", "Rift Inducer", R3(95, 96, 97, 98, 99, 100, 101, 102)),
        F("holoshield", "Holoshield Glove", R3(103, 104, 105, 106, 107, 108, 109, 110)),
        F("flux-rifle", "Flux Rifle", R3(111, 112, 113, 114, 115, 116, 117, 118)),
        F("nitro-launcher", "Nitro Launcher", R3(119, 120, 121, 122, 123, 124, 125, 126)),
        F("plasma-whip", "Plasma Whip", R3(127, 128, 129, 130, 131, 132, 133, 134)),
        F("qwack-o-ray", "Qwack-O-Ray", R3(143, 144, 145, 146, 147, 148, 149, 150)),
        F("ryno-3", "RY3NO", R3(151, 152, 153, 154, 155)),
    });

    private static PlayerWeaponMenuFamily F(string id, string name, params SourceWeaponKey[][] sources) =>
        new(id, name, Array.AsReadOnly(sources.SelectMany(keys => keys).ToArray()));
    private static SourceWeaponKey[] R1(params int[] ids) => Keys(ObpSourceGame.Rac1, ids);
    private static SourceWeaponKey[] R2(params int[] ids) => Keys(ObpSourceGame.Rac2, ids);
    private static SourceWeaponKey[] R3(params int[] ids) => Keys(ObpSourceGame.Rac3, ids);
    private static SourceWeaponKey[] Keys(ObpSourceGame game, int[] ids) => ids.Select(id => new SourceWeaponKey(game, id)).ToArray();
}

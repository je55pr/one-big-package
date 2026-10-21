namespace OBP.RAC1.Gameplay;

/// <summary>Ordinary NTSC-U equipment table 0x179ac0, stride0x4c. No gold upgrades.</summary>
public static class Rac1WeaponProfiles
{
    public sealed record Profile(Rac1WeaponId Item, string Name, string Key, int ModelClass,
        int HandSemantic, int HandJoint, int BodySequence, int UpdateAddress, string CompressedSha256);

    public static IReadOnlyList<Profile> Additional { get; } =
    [
        new(Rac1WeaponId.Pyrocitor, "Pyrocitor", "pyrocitor", 176, 0, 56, 51, 0x2b77f0,
            "0730a449409fa9c0e9e5202ddcebe6b9ca957f09a7fabec5d270788d64114b0b"),
        new(Rac1WeaponId.Blaster, "Blaster", "blaster", 168, 0, 56, 56, 0x2b49a8,
            "7a9e97bb21b6d914733d8732da6c7a727221a62ae120e4bc20273180409c0042"),
        new(Rac1WeaponId.GloveOfDoom, "Glove of Doom", "glove-of-doom", 229, 6, 54, 44, 0x2c7a58,
            "b08c8f997bb72d677b146168dbda9bc0da0fe3ebb9a3539cb9c276f8e7fd0d94"),
        new(Rac1WeaponId.MineGlove, "Mine Glove", "mine-glove", 190, 6, 54, 44, 0x2c1ad0,
            "e1c989440529bfdde6976f0fb16716c99f1d27399b44ea0be12bf58ab36248dd"),
        new(Rac1WeaponId.SuckCannon, "Suck Cannon", "suck-cannon", 849, 0, 56, 51, 0x2da790,
            "76b2f714bee2f86a81de818a6cbe7eff293a00470b53605b4303d69203eba347"),
        new(Rac1WeaponId.Devastator, "Devastator", "devastator", 157, 0, 56, 54, 0x2b2100,
            "d014f6bfb619070c93b7964af38e056b2234313f47ea40808e9c8ce5dfda09db"),
        new(Rac1WeaponId.VisibombGun, "Visibomb Gun", "visibomb", 163, 0, 56, 0, 0x2b3058,
            "4e000b246dd96eef41b8acdd78815a98bc413112be12a63ada2a9fa05fc7e3d0"),
        new(Rac1WeaponId.Taunter, "Taunter", "taunter", 175, 0, 56, 51, 0x2b6f10,
            "e281caf04a1d0532cf419eb3b50e7358015fbf6c97375fe850e1c3aeb1e7d240"),
        new(Rac1WeaponId.Walloper, "Walloper", "walloper", 180, 1, 55, 0, 0x2bb558,
            "ceca37617f9848a81df6a0f09b378ddd3d816362afc1c1bd829ec1eb02d20c74"),
        new(Rac1WeaponId.TeslaClaw, "Tesla Claw", "tesla-claw", 177, 0, 56, 51, 0x2b87e0,
            "aad5475f0ed76e2a04ea7e4da1a75f7f3b70f519efb348225790b7247e0755e5"),
        new(Rac1WeaponId.MorphORay, "Morph-o-Ray", "morph-o-ray", 185, 0, 56, 56, 0x2bc7e8,
            "43c134dc09102245c9119e63e8c448edae93813dedaad04441e38846c8f66a39"),
        new(Rac1WeaponId.Ryno, "RYNO", "ryno", 454, 0, 56, 81, 0x2cc608,
            "e7c8e1ad765c68e2d14d85a10f5d304c6bf77e27ccd7a0901c9f477169bcb66d"),
        new(Rac1WeaponId.DroneDevice, "Drone Device", "drone-device", 483, 6, 54, 44, 0x2cdcc0,
            "1f3942358cd9b9c00f31272349b9495ce56ec5aceaa679e7229324a7d3589700"),
        new(Rac1WeaponId.DecoyGlove, "Decoy Glove", "decoy-glove", 562, 6, 54, 44, 0x2d2670,
            "afc1b1faa940967362bef13405ea85423bbe9433c56265a6104aae8ab9a1c9a2"),
    ];

    public static Profile Get(Rac1WeaponId item) => Additional.Single(profile => profile.Item == item);
    public static bool IsAdditional(Rac1WeaponId item) => Additional.Any(profile => profile.Item == item);
    public static string Name(Rac1WeaponId item) => item switch
    {
        Rac1WeaponId.Wrench => "Wrench",
        Rac1WeaponId.FirstRanged => "Bomb Glove",
        _ => Get(item).Name,
    };
    public static string HandSocket(int semantic) => $"rac1:equipment-hand:{semantic}";

    // Native class168 admission 0x2b4e20..4e4c. Geometry/aim discovery is an adapter.
    public const int BlasterFireTicks = 6;
    public const float BlasterSpeed = 40f / 60;
    public const int BlasterCarrierTicks = 35; // 0x2cb084/0x2cb094, ordinary class305 carrier.
    public const double BlasterDamage = .25; // class305 segment record, 0x2cb418.
    public const uint BlasterFlags = 0x00010001;
    public const uint BlasterMuzzleColor = 0x2f4f7f7f; // 0x2b5260/2b5280, packed RGBA.
    public const uint BlasterImpactColor = 0x7f2f4f6f; // 0x2cb21c/2cb23c.
    // Class176's flame-particle contact, 0x2b8538..2b857c, builds
    // damage(upgrade+1, 0x10000) using 0x259888 before its volume query.
    public const double PyrocitorDamage = 1;
    public const uint PyrocitorFlags = 0x00010000;
    // Initialized overlay SDATA 0x161530 and 0x161520 (gp=0x166c00).
    public const int PyrocitorAmmoInterval = 10;
    public const float PyrocitorReach = 12;
    // Class74 splash 0x2abd64..2abda4; class186 splash 0x2c1388..2c13c0.
    public const double MineDamage = 3;
    public const uint MineFlags = 0x00810000;
    public const double DoomDamage = 3;
    public const uint DoomFlags = 0x00010000;
    public const int DoomAgentsPerCanister = 4;
    public const int DoomAgentSpawnTicks = 8;
    public const int SuckFireTicks = 25; // 0x2db12c..2db138, weapon PVar+8.

    public static Rac1GameplayDamageEvent ChildDamage(Rac1WeaponId weapon, int childClass, long childId,
        Rac1MobyRuntimeKey target, double amount, uint flags) => new(
            Rac1GameplayEntityRef.Projectile(childClass, childId), Rac1GameplayEntityRef.Moby(target), amount, flags,
            handoffKind: Rac1NativeDamageHandoffKind.ContactVolume,
            owner: Rac1GameplayDamageOwner.NativeClass(Get(weapon).ModelClass));

    public static Rac1GameplayDamageEvent DirectDamage(Rac1WeaponId weapon, Rac1MobyRuntimeKey target,
        double amount, uint flags) => new(
            new(Rac1GameplayEntityKind.PlayerEquipment, Get(weapon).ModelClass, (int)weapon),
            Rac1GameplayEntityRef.Moby(target), amount, flags,
            handoffKind: Rac1NativeDamageHandoffKind.ContactVolume,
            owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
}

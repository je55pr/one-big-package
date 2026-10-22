using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Native R&C1 ordinary item -> quick-select bitmap animation IDs.
///
/// The original 2002 NTSC-U HUD animation bank contains the contiguous
/// EA69..EA79 item glyph family. Their silhouettes independently identify
/// the weapons: EA6A bomb, EA6B missile, EA6E speaker, EA70 flame,
/// EA73 lightning, EA75 chicken, etc. The icon's low byte equals the
/// native item ID plus 0x60. This is a visual + sequence attribution,
/// pending a populated retail menu-item table witness.
///
/// Wrench (item 8) has no EA68 bank animation and is not a standard
/// quick-select weapon. Do not substitute a made-up icon for it.
/// </summary>
public static class Rac1WeaponIconCatalog
{
    public readonly record struct Icon(ushort AnimationId, int Frame);

    public static Icon? Ordinary(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.SuckCannon => new(0xEA69, 0),
        Rac1WeaponId.FirstRanged => new(0xEA6A, 0),
        Rac1WeaponId.Devastator => new(0xEA6B, 0),
        Rac1WeaponId.VisibombGun => new(0xEA6D, 0),
        Rac1WeaponId.Taunter => new(0xEA6E, 0),
        Rac1WeaponId.Blaster => new(0xEA6F, 0),
        Rac1WeaponId.Pyrocitor => new(0xEA70, 0),
        Rac1WeaponId.MineGlove => new(0xEA71, 0),
        Rac1WeaponId.Walloper => new(0xEA72, 0),
        Rac1WeaponId.TeslaClaw => new(0xEA73, 0),
        Rac1WeaponId.GloveOfDoom => new(0xEA74, 0),
        Rac1WeaponId.MorphORay => new(0xEA75, 0),
        Rac1WeaponId.Ryno => new(0xEA77, 0),
        Rac1WeaponId.DroneDevice => new(0xEA78, 0),
        Rac1WeaponId.DecoyGlove => new(0xEA79, 0),
        _ => null,
    };
}

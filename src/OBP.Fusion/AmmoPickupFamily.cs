using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

/// <summary>OBP-authored supply families, named after their visual donors, not native ammo identities.</summary>
public enum AmmoPickupFamily
{
    BombGlove, Pyrocitor, Blaster, GloveOfDoom, MineGlove,
    Devastator, Visibomb, TeslaClaw, Ryno, DroneDevice,
}

public static class PlayerAmmoFamilies
{
    /// <summary>
    /// Explicit bindings for recovered, currently implemented recipients only.
    /// Source-qualified identity is essential: equal native item numbers are not aliases.
    /// The full authored design, including future weapons, is in docs/AMMO_PICKUPS.md.
    /// </summary>
    public static AmmoPickupFamily? ForWeapon(SourceWeaponKey weapon) => weapon.SourceGame switch
    {
        ObpSourceGame.Rac1 => weapon.NativeItemId switch
        {
            10 => AmmoPickupFamily.BombGlove,
            16 or 25 => AmmoPickupFamily.Pyrocitor,
            15 => AmmoPickupFamily.Blaster,
            20 => AmmoPickupFamily.GloveOfDoom,
            11 => AmmoPickupFamily.Devastator,
            13 => AmmoPickupFamily.Visibomb,
            19 => AmmoPickupFamily.TeslaClaw,
            23 => AmmoPickupFamily.Ryno,
            // Intentional: the Mine Glove weapon uses Drone Device drops.
            17 or 24 => AmmoPickupFamily.DroneDevice,
            _ => null,
        },
        ObpSourceGame.Rac2 when weapon.NativeItemId == 30 => AmmoPickupFamily.Blaster,
        // Definition 43 is the implemented V5 Shock Cannon, in the Shock Blaster line.
        ObpSourceGame.Rac3 when weapon.NativeItemId == 43 => AmmoPickupFamily.TeslaClaw,
        _ => null,
    };
}

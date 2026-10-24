using OBP.Runtime.Gameplay;
using System.Numerics;

namespace OneBigPackage;

/// <summary>
/// OBP feedback endpoints in the held mesh's scene axes. Weapon placement now
/// comes directly from the source skeletal hand matrix, without grip offsets.
/// </summary>
public static class FusionWeaponMount
{
    // OBP barrel endpoints from the decoded rest geometry, not recovered native
    // draw-time muzzle sockets. Keep firing feedback attached to the moved mesh.
    public static Vector3 Muzzle(SourceWeaponKey weapon) => weapon == FusionWeaponSession.Lancer ? new(-.19f, -.03f, -.648f) :
        weapon == FusionWeaponSession.ShockCannon ? new(-.31f, 0, -1.10f) :
        throw new ArgumentOutOfRangeException(nameof(weapon));

}

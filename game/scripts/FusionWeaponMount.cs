using OBP.Runtime.Gameplay;
using System.Numerics;

namespace OneBigPackage;

/// <summary>
/// OBP cross-game grip alignment. Meshes keep their native dimensions; align
/// their decoded -Y barrels with the R&C1 gun socket's native -Y direction
/// (scene-local -Z). The Cannon's +Z top needs a quarter roll to the socket's
/// +X top. This is not a recovered GC/UYA player attachment.
/// </summary>
public static class FusionWeaponMount
{
    // Explicit OBP palm placement, in the converted held mesh's scene axes.
    // Cannon's grip occupies native X .072..172, Y -.454..-.355, Z -.501..-.107.
    public static Vector3 Grip(SourceWeaponKey weapon) => weapon == FusionWeaponSession.Lancer ? Vector3.Zero :
        weapon == FusionWeaponSession.ShockCannon ? new(-.125f, -.23f, -.403f) :
        throw new ArgumentOutOfRangeException(nameof(weapon));

    // OBP barrel endpoints from the decoded rest geometry, not recovered native
    // draw-time muzzle sockets. Keep firing feedback attached to the moved mesh.
    public static Vector3 Muzzle(SourceWeaponKey weapon) => weapon == FusionWeaponSession.Lancer ? new(-.19f, -.03f, -.648f) :
        weapon == FusionWeaponSession.ShockCannon ? new(-.31f, 0, -1.10f) :
        throw new ArgumentOutOfRangeException(nameof(weapon));

    public static Quaternion Rotation(SourceWeaponKey weapon) => weapon == FusionWeaponSession.Lancer ? Quaternion.Identity :
        weapon == FusionWeaponSession.ShockCannon ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) :
        throw new ArgumentOutOfRangeException(nameof(weapon));
}

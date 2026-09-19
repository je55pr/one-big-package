using Godot;

namespace OBP.Godot.Player;

/// <summary>Presentation-only native Z-up facing conversion for Godot avatars.</summary>
public static class PlayerAvatarFacing
{
    // RAC1 native +X becomes scene -X after the host handedness correction;
    // native +Y becomes scene +Z. PlayerAvatarView's neutral forward is -Z.
    public static Vector3 NativeZUpPlanarDirectionToGodot(double nativeX, double nativeY)
    {
        if (!double.IsFinite(nativeX)) throw new ArgumentOutOfRangeException(nameof(nativeX));
        if (!double.IsFinite(nativeY)) throw new ArgumentOutOfRangeException(nameof(nativeY));
        return new Vector3((float)-nativeX, 0f, (float)nativeY);
    }

    public static float NativeZUpYawToGodotSceneYaw(double nativeYaw)
    {
        if (!double.IsFinite(nativeYaw)) throw new ArgumentOutOfRangeException(nameof(nativeYaw));
        Vector3 sceneForward = NativeZUpPlanarDirectionToGodot(Math.Cos(nativeYaw), Math.Sin(nativeYaw));

        // The reconstructed Ratchet mesh's visible neutral facing is local +X,
        // not Godot's conventional local -Z. Rotate the presentation root by
        // an additional +90 degrees so the mesh's nose, rather than the node's
        // abstract Forward vector, follows the recovered native facing.
        return Mathf.Atan2(-sceneForward.X, -sceneForward.Z) + (Mathf.Pi / 2f);
    }
}

using Godot;

namespace OBP.Game;

/// <summary>
/// Shared arsenal-world presentation orientation. Missile, drone and morphed
/// Moby visuals use the same host projection without borrowing family state.
/// </summary>
internal static class Rac1ArsenalViewPose
{
    internal static void Orient(Node3D view, Vector3 direction)
    {
        view.Rotation = new(0, MathF.Atan2(direction.Z, -direction.X),
            -MathF.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length()));
    }
}

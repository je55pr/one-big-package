namespace OBP.Runtime.Player;

/// <summary>
/// Engine-neutral player input for one deterministic controller update.
/// Planar components are a desired direction in the host's horizontal plane;
/// source-game controllers own acceleration and motion semantics.
/// </summary>
public readonly record struct PlayerControlIntent(
    double PlanarX,
    double PlanarY,
    bool JumpHeld,
    bool JumpPressed,
    bool CrouchHeld = false)
{
    public (double X, double Y) NormalizedPlanar()
    {
        double length = System.Math.Sqrt((PlanarX * PlanarX) + (PlanarY * PlanarY));
        return length > 1e-12
            ? (PlanarX / length, PlanarY / length)
            : (0d, 0d);
    }
}

/// <summary>
/// Collision/contact facts supplied by the host after its own world collision.
/// Source-game controllers may react to contact but never own collision geometry.
/// </summary>
public readonly record struct PlayerContactFacts(
    bool IsGrounded,
    bool HitCeiling = false);

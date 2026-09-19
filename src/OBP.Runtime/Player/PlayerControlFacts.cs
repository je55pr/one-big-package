namespace OBP.Runtime.Player;

/// <summary>
/// Engine-neutral player input for one deterministic controller update.
/// Planar components preserve unconditioned signed control-axis magnitude;
/// source-game controllers own dead zones, shaping and acceleration semantics.
/// </summary>
public readonly record struct PlayerControlIntent(
    double PlanarX,
    double PlanarY,
    bool JumpHeld,
    bool JumpPressed,
    bool CrouchHeld = false,
    PlayerPlanarBasis? PlanarBasis = null)
{
    public PlayerPlanarBasis EffectivePlanarBasis => PlanarBasis ?? PlayerPlanarBasis.Identity;
}

/// <summary>
/// Engine-neutral 2D control basis. Right/forward control-space input can be
/// transformed into the host's horizontal output plane without exposing any
/// presentation-engine type to a native controller.
/// </summary>
public readonly record struct PlayerPlanarBasis(
    double RightX,
    double RightY,
    double ForwardX,
    double ForwardY)
{
    public static PlayerPlanarBasis Identity { get; } = new(1d, 0d, 0d, 1d);

    public (double X, double Y) Transform(double right, double forward) =>
        ((right * RightX) + (forward * ForwardX),
         (right * RightY) + (forward * ForwardY));
}

/// <summary>
/// Collision/contact facts supplied by the host after its own world collision.
/// Source-game controllers may react to contact but never own collision geometry.
/// </summary>
public readonly record struct PlayerContactFacts(
    bool IsGrounded,
    bool HitCeiling = false);

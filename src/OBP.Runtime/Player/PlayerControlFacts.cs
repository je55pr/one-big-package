namespace OBP.Runtime.Player;

/// <summary>
/// Engine-neutral player input for one deterministic controller update.
/// Planar components preserve unconditioned signed control-axis magnitude;
/// source-game controllers own dead zones, shaping and acceleration semantics.
/// PlanarBasis maps right/forward control space; NativePlanarBasis separately
/// maps source-game planar world axes when recovered behavior depends on facing.
/// </summary>
public readonly record struct PlayerControlIntent(
    double PlanarX,
    double PlanarY,
    bool JumpHeld,
    bool JumpPressed,
    bool CrouchHeld = false,
    PlayerPlanarBasis? PlanarBasis = null,
    PlayerPlanarBasis? NativePlanarBasis = null)
{
    public PlayerPlanarBasis EffectivePlanarBasis => PlanarBasis ?? PlayerPlanarBasis.Identity;
    public PlayerPlanarBasis EffectiveNativePlanarBasis => NativePlanarBasis ?? PlayerPlanarBasis.Identity;
}

/// <summary>
/// Engine-neutral 2D planar basis. It can map right/forward control space or
/// source-game planar world axes into the host horizontal plane without
/// exposing any presentation-engine type to a native controller.
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

using OBP.Core.Math;

namespace OBP.Runtime.Camera;

/// <summary>
/// Engine-neutral camera pose published by a source-game camera controller.
/// Coordinates are OBP Y-up world space. Heading is measured around +Y from
/// +X toward +Z, so hosts never need the source game's native axis convention.
/// </summary>
public sealed record RuntimeCameraState(
    Vec3 Eye,
    Vec3 Forward,
    double ControlHeadingRadians,
    double PreferredDistance,
    double EffectiveDistance)
{
    public RuntimeCameraState Validate()
    {
        ValidateFinite(Eye, nameof(Eye));
        ValidateFinite(Forward, nameof(Forward));
        ValidateFinite(ControlHeadingRadians, nameof(ControlHeadingRadians));
        ValidateFinite(PreferredDistance, nameof(PreferredDistance));
        ValidateFinite(EffectiveDistance, nameof(EffectiveDistance));

        if (PreferredDistance < 0d)
            throw new ArgumentOutOfRangeException(nameof(PreferredDistance));
        if (EffectiveDistance < 0d)
            throw new ArgumentOutOfRangeException(nameof(EffectiveDistance));
        return this;
    }

    private static void ValidateFinite(Vec3 value, string name)
    {
        if (!double.IsFinite(value.X) ||
            !double.IsFinite(value.Y) ||
            !double.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// Geometry-neutral endpoints for the source game's camera-contact stage.
/// The host chooses the collision primitive used between them; this record does
/// not claim a ray, sphere cast, capsule, or any particular engine query.
/// </summary>
public readonly record struct RuntimeCameraObstructionProbe(
    Vec3 From,
    Vec3 To);

/// <summary>
/// Collision facts returned by the host to a source-game camera controller.
/// LateralDot is the signed contact-orientation witness used by camera rules;
/// zero is appropriate when the host cannot resolve a meaningful side.
/// </summary>
public readonly record struct RuntimeCameraObstructionFacts(
    bool HasContact,
    double LateralDot = 0d)
{
    public RuntimeCameraObstructionFacts Validate()
    {
        if (!double.IsFinite(LateralDot))
            throw new ArgumentOutOfRangeException(nameof(LateralDot));
        return this;
    }
}

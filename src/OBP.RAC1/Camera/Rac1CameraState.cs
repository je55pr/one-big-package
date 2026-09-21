using OBP.Core.Math;
using OBP.Runtime.Camera;

namespace OBP.RAC1.Camera;

/// <summary>
/// Recovered R&amp;C1 horizontal camera/control state. The heading is the native
/// control-heading carried at retail global 0x00166dd8; it is not Ratchet yaw.
/// </summary>
public readonly record struct Rac1CameraControlState(
    double ControlHeading,
    double HeadingStep,
    double Pitch);

/// <summary>
/// Ordinary chase-target state. Native coordinates remain Z-up inside RAC1.
/// </summary>
public readonly record struct Rac1CameraFollowState(
    Vec3 FilteredTargetNativeZUp,
    double RawTargetZ,
    double VerticalVelocity);

/// <summary>
/// Type-0 framing state whose persistent fields are proven by the loaded retail
/// camera object. Fields with unresolved selectors are preserved but not guessed.
/// </summary>
public readonly record struct Rac1CameraFramingState(
    Vec3 EyeNativeZUp,
    Vec3 EyeAnchorNativeZUp,
    Vec3 RadialOffsetNativeZUp,
    double RadialVelocity,
    double PreferredRadius,
    double PreferredRadiusVelocity,
    double RadiusTransitionAcceleration,
    double CurrentLookHeight,
    double LookHeightVelocity,
    double CurrentEyeHeight,
    double EyeHeightVelocity,
    double PreferredEyeHeight,
    double PreferredEyeHeightVelocity,
    double EyeHeightTransitionAcceleration);

/// <summary>Persistent obstruction fields from type-0 camera state.</summary>
public readonly record struct Rac1CameraObstructionState(
    double RadialCorrection,
    int ReleaseTimerTicks,
    int LateralSide)
{
    public double EffectiveRadius(double preferredRadius) =>
        preferredRadius - RadialCorrection;
}

/// <summary>
/// Engine-independent R&amp;C1 camera snapshot. It retains native state needed by
/// recovered recurrences and can project only proven presentation data into the
/// game-neutral runtime contract.
/// </summary>
public sealed record Rac1CameraState(
    Rac1CameraControlState Control,
    Rac1CameraFollowState Follow,
    Rac1CameraFramingState Framing,
    Rac1CameraObstructionState Obstruction)
{
    public double EffectiveRadius =>
        Obstruction.EffectiveRadius(Framing.PreferredRadius);

    public RuntimeCameraState ToRuntimeState()
    {
        Validate();

        double cosPitch = Math.Cos(Control.Pitch);
        var nativeForward = new Vec3(
            Math.Cos(Control.ControlHeading) * cosPitch,
            Math.Sin(Control.ControlHeading) * cosPitch,
            -Math.Sin(Control.Pitch));
        Vec3 forward = nativeForward.NativeZUpToObpYUp();

        return new RuntimeCameraState(
            Framing.EyeNativeZUp.NativeZUpToObpYUp(),
            forward,
            Rac1CameraRecurrence.WrapPi(Control.ControlHeading),
            Framing.PreferredRadius,
            EffectiveRadius).Validate();
    }

    public Rac1CameraState Validate()
    {
        ValidateFinite(Control.ControlHeading, nameof(Control.ControlHeading));
        ValidateFinite(Control.HeadingStep, nameof(Control.HeadingStep));
        ValidateFinite(Control.Pitch, nameof(Control.Pitch));
        ValidateFinite(Follow.FilteredTargetNativeZUp, nameof(Follow.FilteredTargetNativeZUp));
        ValidateFinite(Follow.RawTargetZ, nameof(Follow.RawTargetZ));
        ValidateFinite(Follow.VerticalVelocity, nameof(Follow.VerticalVelocity));
        ValidateFinite(Framing.EyeNativeZUp, nameof(Framing.EyeNativeZUp));
        ValidateFinite(Framing.EyeAnchorNativeZUp, nameof(Framing.EyeAnchorNativeZUp));
        ValidateFinite(Framing.RadialOffsetNativeZUp, nameof(Framing.RadialOffsetNativeZUp));
        ValidateFinite(Framing.RadialVelocity, nameof(Framing.RadialVelocity));
        ValidateFinite(Framing.PreferredRadius, nameof(Framing.PreferredRadius));
        ValidateFinite(Framing.PreferredRadiusVelocity, nameof(Framing.PreferredRadiusVelocity));
        ValidateFinite(Framing.RadiusTransitionAcceleration, nameof(Framing.RadiusTransitionAcceleration));
        ValidateFinite(Framing.CurrentLookHeight, nameof(Framing.CurrentLookHeight));
        ValidateFinite(Framing.LookHeightVelocity, nameof(Framing.LookHeightVelocity));
        ValidateFinite(Framing.CurrentEyeHeight, nameof(Framing.CurrentEyeHeight));
        ValidateFinite(Framing.EyeHeightVelocity, nameof(Framing.EyeHeightVelocity));
        ValidateFinite(Framing.PreferredEyeHeight, nameof(Framing.PreferredEyeHeight));
        ValidateFinite(Framing.PreferredEyeHeightVelocity, nameof(Framing.PreferredEyeHeightVelocity));
        ValidateFinite(Framing.EyeHeightTransitionAcceleration, nameof(Framing.EyeHeightTransitionAcceleration));
        ValidateFinite(Obstruction.RadialCorrection, nameof(Obstruction.RadialCorrection));

        if (Obstruction.ReleaseTimerTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(Obstruction.ReleaseTimerTicks));
        if (Obstruction.LateralSide is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Obstruction.LateralSide));
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

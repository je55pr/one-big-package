using OBP.Core.Math;
using OBP.RAC1.Player;
using OBP.Runtime.Camera;

namespace OBP.RAC1.Camera;

public enum Rac1CameraRadialFollowProfile
{
    Baseline,
    Fast,
}

/// <summary>
/// Ordinary type-0 R&C1 camera producer assembled only from retained native stages.
/// One Step is one NTSC camera update.
/// </summary>
public sealed class Rac1OrdinaryCameraController
{
    public const double UpdateHz = 60d;
    public const double ManualInputAcceleration = 0.019999999552965164d;
    public const double ManualInputDamping = 0.20000000298023224d;
    public const double HorizontalRadiansPerUnit = 0.022689279168844223d;
    public const double VerticalInputDeadzone = 0.30000001192092896d;
    public const double VerticalSpanRadians = 0.6981316804885864d;

    public const double OrdinaryPreferredRadius = 5.999970436096191d;
    public const double OrdinaryEyeHeight = 1.9999926090240479d;
    public const double OrdinaryLookHeight = 1.5000041723251343d;
    public const double BaselineRadialAcceleration = 0.02d;
    public const double BaselineRadialDamping = 0.2d;
    public const double FastRadialAcceleration = 0.04d;
    public const double FastRadialDamping = 0.3d;

    public readonly record struct Input(
        Vec3 PlayerNativeZUp,
        double RawRightX,
        double RawRightYUp,
        RuntimeCameraObstructionFacts Obstruction,
        Rac1CameraRadialFollowProfile RadialProfile = Rac1CameraRadialFollowProfile.Baseline);

    private double _controlHeading;
    private double _manualYaw;
    private double _manualYawVelocity;
    private double _manualPitch;
    private double _manualPitchVelocity;

    private double _filteredZ;
    private double _followZVelocity;
    private double _eyeHeight = OrdinaryEyeHeight;
    private double _eyeHeightVelocity;
    private double _currentRadius = OrdinaryPreferredRadius;
    private double _radialVelocity;
    private double _obstructionCorrection;
    private int _obstructionReleaseTicks;
    private int _lateralSide;
    private bool _initialized;

    public double ControlHeading => _controlHeading;
    public double ManualYawState => _manualYaw;
    public double ManualPitchState => _manualPitch;
    public double VerticalOrbitRadians => -_manualPitch * VerticalSpanRadians;
    public double CurrentRadius => _currentRadius;
    public double ObstructionCorrection => _obstructionCorrection;
    public int ObstructionReleaseTicks => _obstructionReleaseTicks;

    public void Reset(Vec3 playerNativeZUp, double controlHeading)
    {
        ValidateFinite(playerNativeZUp, nameof(playerNativeZUp));
        ValidateFinite(controlHeading, nameof(controlHeading));
        _controlHeading = Rac1CameraRecurrence.WrapPi(controlHeading);
        _manualYaw = _manualYawVelocity = 0d;
        _manualPitch = _manualPitchVelocity = 0d;
        _filteredZ = playerNativeZUp.Z;
        _followZVelocity = 0d;
        _eyeHeight = OrdinaryEyeHeight;
        _eyeHeightVelocity = 0d;
        _currentRadius = OrdinaryPreferredRadius;
        _radialVelocity = 0d;
        _obstructionCorrection = 0d;
        _obstructionReleaseTicks = 0;
        _lateralSide = 0;
        _initialized = true;
    }

    public Rac1CameraState Step(Input input)
    {
        ValidateFinite(input.PlayerNativeZUp, nameof(input.PlayerNativeZUp));
        ValidateFinite(input.RawRightX, nameof(input.RawRightX));
        ValidateFinite(input.RawRightYUp, nameof(input.RawRightYUp));
        input.Obstruction.Validate();
        if (!_initialized)
            Reset(input.PlayerNativeZUp, 0d);

        var right = Rac1AnalogueInput.ConditionUnitAxes(
            input.RawRightX,
            input.RawRightYUp);

        var yaw = Rac1CameraRecurrence.AdvanceDampedScalar(
            _manualYaw,
            _manualYawVelocity,
            right.X,
            ManualInputAcceleration,
            ManualInputDamping);
        _manualYaw = yaw.Value;
        _manualYawVelocity = yaw.Velocity;
        _controlHeading = Rac1CameraRecurrence.WrapPi(
            _controlHeading - (_manualYaw * HorizontalRadiansPerUnit));

        double pitchTarget = RemapVerticalInput(right.Y);
        if (pitchTarget != 0d)
        {
            var pitch = Rac1CameraRecurrence.AdvanceDampedScalar(
                _manualPitch,
                _manualPitchVelocity,
                pitchTarget,
                ManualInputAcceleration,
                ManualInputDamping);
            _manualPitch = pitch.Value;
            _manualPitchVelocity = pitch.Velocity;
        }
        else
        {
            // Native type-0 selects the separate state+0x1cc camera-state
            // fallback only after manual Y goes neutral. Its writer is recovered,
            // but no ordinary-play trigger/source is promoted here, so the host
            // must not synthesize zero as a fallback command.
            _manualPitchVelocity = 0d;
        }

        var follow = Rac1CameraRecurrence.AdvanceOrdinaryFollowZ(
            _filteredZ,
            _followZVelocity,
            input.PlayerNativeZUp.Z);
        _filteredZ = follow.Value;
        _followZVelocity = follow.Velocity;

        UpdateObstruction(input.Obstruction);
        double targetRadius = OrdinaryPreferredRadius - _obstructionCorrection;
        (double radialAcceleration, double radialDamping) =
            RadialCoefficients(input.RadialProfile);

        var radius = Rac1CameraRecurrence.AdvanceDampedScalar(
            _currentRadius,
            _radialVelocity,
            targetRadius,
            radialAcceleration,
            radialDamping);
        _currentRadius = radius.Value;
        _radialVelocity = radius.Velocity;

        var eyeHeight = Rac1CameraRecurrence.AdvanceEyeHeight(
            _eyeHeight,
            _eyeHeightVelocity,
            OrdinaryEyeHeight);
        _eyeHeight = eyeHeight.Value;
        _eyeHeightVelocity = eyeHeight.Velocity;

        var anchor = new Vec3(
            input.PlayerNativeZUp.X,
            input.PlayerNativeZUp.Y,
            _filteredZ + _eyeHeight);
        Vec3 radial = ComposeVerticalOrbitOffset(
            _controlHeading,
            _currentRadius,
            VerticalOrbitRadians);
        var eye = anchor + radial;

        double planarRadius = Math.Sqrt(
            (radial.X * radial.X) +
            (radial.Y * radial.Y));
        double cameraPitch = Math.Atan2(
            eye.Z - (_filteredZ + OrdinaryLookHeight),
            Math.Max(planarRadius, Rac1CameraRecurrence.ObstructionInnerRadiusFloor));

        return new Rac1CameraState(
            new Rac1CameraControlState(
                _controlHeading,
                0d,
                cameraPitch),
            new Rac1CameraFollowState(
                new Vec3(input.PlayerNativeZUp.X, input.PlayerNativeZUp.Y, _filteredZ),
                input.PlayerNativeZUp.Z,
                _followZVelocity),
            new Rac1CameraFramingState(
                eye,
                anchor,
                radial,
                _radialVelocity,
                OrdinaryPreferredRadius,
                0d,
                Rac1CameraRecurrence.ProfileTransitionAcceleration,
                OrdinaryLookHeight,
                0d,
                _eyeHeight,
                _eyeHeightVelocity,
                OrdinaryEyeHeight,
                0d,
                Rac1CameraRecurrence.ProfileTransitionAcceleration),
            new Rac1CameraObstructionState(
                _obstructionCorrection,
                _obstructionReleaseTicks,
                _lateralSide)).Validate();
    }

    private void UpdateObstruction(RuntimeCameraObstructionFacts obstruction)
    {
        if (obstruction.HasContact)
        {
            double workingRadius = OrdinaryPreferredRadius - _obstructionCorrection;
            double pulled = Rac1CameraRecurrence.PullInWorkingRadius(workingRadius);
            _obstructionCorrection = OrdinaryPreferredRadius - pulled;
            _obstructionReleaseTicks = Rac1CameraRecurrence.ObstructionContactTimerTicks;
            _lateralSide = Rac1CameraRecurrence.ClassifyLateralSide(obstruction.LateralDot);
            _controlHeading = Rac1CameraRecurrence.WrapPi(
                _controlHeading + Rac1CameraRecurrence.LateralCorrectionRadians(_lateralSide));
        }
        else
        {
            var clear = Rac1CameraRecurrence.AdvanceClearLineRecovery(
                _obstructionCorrection,
                _obstructionReleaseTicks);
            _obstructionCorrection = clear.RadialCorrection;
            _obstructionReleaseTicks = clear.ReleaseTimerTicks;
            _lateralSide = 0;
        }

        var guard = Rac1CameraRecurrence.EnforceFinalEffectiveRadiusFloor(
            OrdinaryPreferredRadius,
            _obstructionCorrection,
            _obstructionReleaseTicks);
        _obstructionCorrection = guard.RadialCorrection;
        _obstructionReleaseTicks = guard.ReleaseTimerTicks;
    }

    public static double RemapVerticalInput(double conditionedY)
    {
        ValidateFinite(conditionedY, nameof(conditionedY));
        double magnitude = Math.Abs(conditionedY);
        if (magnitude <= VerticalInputDeadzone)
            return 0d;
        double remapped = (magnitude - VerticalInputDeadzone) /
            (1d - VerticalInputDeadzone);
        return Math.CopySign(Math.Clamp(remapped, 0d, 1d), conditionedY);
    }

    public static Vec3 ComposeVerticalOrbitOffset(
        double controlHeading,
        double radius,
        double orbitRadians)
    {
        ValidateFinite(controlHeading, nameof(controlHeading));
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(orbitRadians, nameof(orbitRadians));
        if (radius < 0d)
            throw new ArgumentOutOfRangeException(nameof(radius));

        double planarRadius = radius * Math.Cos(orbitRadians);
        return new Vec3(
            -Math.Cos(controlHeading) * planarRadius,
            -Math.Sin(controlHeading) * planarRadius,
            radius * Math.Sin(orbitRadians));
    }

    public static (double Acceleration, double Damping) RadialCoefficients(
        Rac1CameraRadialFollowProfile profile) =>
        profile switch
        {
            Rac1CameraRadialFollowProfile.Baseline =>
                (BaselineRadialAcceleration, BaselineRadialDamping),
            Rac1CameraRadialFollowProfile.Fast =>
                (FastRadialAcceleration, FastRadialDamping),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };

    private static void ValidateFinite(Vec3 value, string name)
    {
        if (!double.IsFinite(value.X) ||
            !double.IsFinite(value.Y) ||
            !double.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

using OBP.Core.Math;
using OBP.RAC1.Player;
using OBP.Runtime.Camera;

namespace OBP.RAC1.Camera;

public enum Rac1CameraRadialFollowProfile { Baseline, Fast }

/// <summary>
/// Ordinary upright type-0 follow camera. Desired offset, rendered offset,
/// target translation and view are separate native stages (0x002e9c28).
/// One Step is one NTSC update; all coordinates here are native Z-up.
/// </summary>
public sealed partial class Rac1OrdinaryCameraController
{
    public const double UpdateHz = 60d;
    public const double ManualInputAcceleration = 1d;
    public const double ManualInputDamping = 1d;
    public const double HorizontalRadiansPerUnit = 0.022689279168844223d;
    public const double VerticalInputDeadzone = 0.30000001192092896d;
    public const double VerticalSpanRadians = 0.6981316804885864d;
    public const double VerticalActiveStep = 0.019999999552965164d;
    public const double VerticalReleaseStep = 0.009999999776482582d;
    public const double VerticalTurnLimit = 0.030543262138962746d;
    public const double OrdinaryPreferredRadius = 6d;
    public const double OrdinaryEyeHeight = 2d;
    public const double OrdinaryLookHeight = 1.5d;
    public const double BaselineRadialAcceleration = 0.019999999552965164d;
    public const double BaselineRadialDamping = 0.20000000298023224d;
    public const double FastRadialAcceleration = 0.03999999910593033d;
    public const double FastRadialDamping = 0.30000001192092896d;
    private const double AngularAcceleration = 0.014999999664723873d;
    private const double AngularDamping = 0.20000000298023224d;

    public readonly record struct Input(
        Vec3 PlayerNativeZUp, double RawRightX, double RawRightYUp,
        RuntimeCameraObstructionFacts Obstruction,
        Rac1CameraRadialFollowProfile RadialProfile = Rac1CameraRadialFollowProfile.Baseline,
        Rac1CameraOptions? Options = null);

    private double _controlHeading, _manualYaw, _manualYawVelocity, _manualPitch, _manualPitchVelocity;
    private double _filteredZ, _followZVelocity, _eyeHeight = OrdinaryEyeHeight, _eyeHeightVelocity;
    private double _currentRadius = OrdinaryPreferredRadius, _radialVelocity, _obstructionCorrection;
    private int _obstructionReleaseTicks, _lateralSide;
    private bool _initialized, _pipelineInitialized;
    private Vec3 _desiredOffset, _actualOffset, _previousDesiredEye, _positionBase, _positionVelocity, _lastPlayer;
    private double _offsetYawVelocity, _offsetPitchVelocity, _lookHeight = OrdinaryLookHeight, _lookHeightVelocity;
    private double _aimTilt, _aimTiltVelocity;
    private double _preferredEyeHeight = OrdinaryEyeHeight, _preferredHeightVelocity;

    public double ControlHeading => _controlHeading;
    public double ManualYawState => _manualYaw;
    public double ManualPitchState => _manualPitch;
    public double VerticalOrbitRadians => Elevation(_desiredOffset);
    public double CurrentRadius => _currentRadius;
    public double ObstructionCorrection => _obstructionCorrection;
    public int ObstructionReleaseTicks => _obstructionReleaseTicks;

    /// <summary>OBP repro state. The optional extension preserves old movement bookmarks.</summary>
    public sealed record Snapshot(
        double ControlHeading, double ManualYaw, double ManualYawVelocity,
        double ManualPitch, double ManualPitchVelocity, double FilteredZ,
        double FollowZVelocity, double EyeHeight, double EyeHeightVelocity,
        double CurrentRadius, double RadialVelocity, double ObstructionCorrection,
        int ObstructionReleaseTicks, int LateralSide, bool Initialized,
        PipelineSnapshot? Pipeline = null)
    {
        public void Validate()
        {
            double[] scalars = [ControlHeading, ManualYaw, ManualYawVelocity, ManualPitch,
                ManualPitchVelocity, FilteredZ, FollowZVelocity, EyeHeight, EyeHeightVelocity,
                CurrentRadius, RadialVelocity, ObstructionCorrection];
            if (scalars.Any(value => !double.IsFinite(value)) || CurrentRadius < 0 ||
                ObstructionReleaseTicks < 0 || LateralSide is < -1 or > 1)
                throw new ArgumentException("Invalid ordinary camera snapshot.");
            Pipeline?.Validate();
        }
    }

    public sealed record PipelineSnapshot(
        Vec3 DesiredOffset, Vec3 ActualOffset, Vec3 PreviousDesiredEye,
        Vec3 PositionBase, Vec3 PositionVelocity, Vec3 LastPlayer,
        double OffsetYawVelocity, double OffsetPitchVelocity,
        double LookHeight, double LookHeightVelocity, double AimTilt, double AimTiltVelocity,
        double PreferredEyeHeight = OrdinaryEyeHeight, double PreferredHeightVelocity = 0d)
    {
        public void Validate()
        {
            foreach (var v in new[] { DesiredOffset, ActualOffset, PreviousDesiredEye, PositionBase, PositionVelocity, LastPlayer })
                ValidateFinite(v, nameof(PipelineSnapshot));
            foreach (double v in new[] { OffsetYawVelocity, OffsetPitchVelocity, LookHeight, LookHeightVelocity, AimTilt, AimTiltVelocity, PreferredEyeHeight, PreferredHeightVelocity })
                ValidateFinite(v, nameof(PipelineSnapshot));
        }
    }

    public Snapshot CaptureSnapshot() => new(
        _controlHeading, _manualYaw, _manualYawVelocity, _manualPitch, _manualPitchVelocity,
        _filteredZ, _followZVelocity, _eyeHeight, _eyeHeightVelocity, _currentRadius,
        _radialVelocity, _obstructionCorrection, _obstructionReleaseTicks, _lateralSide, _initialized,
        _pipelineInitialized ? new(_desiredOffset, _actualOffset, _previousDesiredEye, _positionBase,
            _positionVelocity, _lastPlayer, _offsetYawVelocity, _offsetPitchVelocity,
            _lookHeight, _lookHeightVelocity, _aimTilt, _aimTiltVelocity, _preferredEyeHeight, _preferredHeightVelocity) : null);

    public void RestoreSnapshot(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        _controlHeading = snapshot.ControlHeading;
        _manualYaw = snapshot.ManualYaw;
        _manualYawVelocity = snapshot.ManualYawVelocity;
        _manualPitch = snapshot.ManualPitch;
        _manualPitchVelocity = snapshot.ManualPitchVelocity;
        _filteredZ = snapshot.FilteredZ;
        _followZVelocity = snapshot.FollowZVelocity;
        _eyeHeight = snapshot.EyeHeight;
        _eyeHeightVelocity = snapshot.EyeHeightVelocity;
        _currentRadius = snapshot.CurrentRadius;
        _radialVelocity = snapshot.RadialVelocity;
        _obstructionCorrection = snapshot.ObstructionCorrection;
        _obstructionReleaseTicks = snapshot.ObstructionReleaseTicks;
        _lateralSide = snapshot.LateralSide;
        _initialized = snapshot.Initialized;
        _pipelineInitialized = snapshot.Pipeline is not null;
        if (snapshot.Pipeline is { } p)
        {
            _desiredOffset = p.DesiredOffset; _actualOffset = p.ActualOffset;
            _previousDesiredEye = p.PreviousDesiredEye; _positionBase = p.PositionBase;
            _positionVelocity = p.PositionVelocity; _lastPlayer = p.LastPlayer;
            _offsetYawVelocity = p.OffsetYawVelocity; _offsetPitchVelocity = p.OffsetPitchVelocity;
            _lookHeight = p.LookHeight; _lookHeightVelocity = p.LookHeightVelocity;
            _aimTilt = p.AimTilt; _aimTiltVelocity = p.AimTiltVelocity;
            _preferredEyeHeight = p.PreferredEyeHeight; _preferredHeightVelocity = p.PreferredHeightVelocity;
        }
    }

    public void Reset(Vec3 playerNativeZUp, double controlHeading)
    {
        ValidateFinite(playerNativeZUp, nameof(playerNativeZUp));
        ValidateFinite(controlHeading, nameof(controlHeading));
        _controlHeading = Rac1CameraRecurrence.WrapPi(controlHeading);
        _manualYaw = _manualYawVelocity = _manualPitch = _manualPitchVelocity = 0d;
        _filteredZ = playerNativeZUp.Z; _followZVelocity = 0d;
        _eyeHeight = OrdinaryEyeHeight; _eyeHeightVelocity = 0d;
        _currentRadius = OrdinaryPreferredRadius; _radialVelocity = 0d;
        _obstructionCorrection = 0d; _obstructionReleaseTicks = _lateralSide = 0;
        InitializePipeline(playerNativeZUp, 0d);
        _initialized = true;
    }

    private void InitializePipeline(Vec3 player, double elevation)
    {
        _actualOffset = _desiredOffset = ComposeVerticalOrbitOffset(_controlHeading, _currentRadius, elevation);
        _previousDesiredEye = player + new Vec3(0, 0, OrdinaryEyeHeight) + _desiredOffset;
        _positionBase = _lastPlayer = player; _positionVelocity = Vec3.Zero;
        _offsetYawVelocity = _offsetPitchVelocity = _aimTilt = _aimTiltVelocity = 0d;
        _lookHeight = OrdinaryLookHeight; _lookHeightVelocity = 0d;
        _preferredEyeHeight = OrdinaryEyeHeight; _preferredHeightVelocity = 0d;
        _pipelineInitialized = true;
    }

    public Rac1CameraState Step(Input input, IRac1CameraGeometry? geometry = null)
    {
        ValidateFinite(input.PlayerNativeZUp, nameof(input.PlayerNativeZUp));
        ValidateFinite(input.RawRightX, nameof(input.RawRightX));
        ValidateFinite(input.RawRightYUp, nameof(input.RawRightYUp));
        input.Obstruction.Validate();
        var options = (input.Options ?? Rac1CameraOptions.Default).Validate();
        if (!_initialized) Reset(input.PlayerNativeZUp, 0d);
        if (!_pipelineInitialized) InitializePipeline(input.PlayerNativeZUp, -_manualPitch * VerticalSpanRadians);

        Vec3 displacement = input.PlayerNativeZUp - _lastPlayer;
        _lastPlayer = input.PlayerNativeZUp;
        var right = Rac1AnalogueInput.ConditionUnitAxes(input.RawRightX, input.RawRightYUp);
        (double a, double b) = options.SelectManualInput(right.X, right.Y);

        var follow = Rac1CameraRecurrence.AdvanceOrdinaryFollowZ(_filteredZ, _followZVelocity, input.PlayerNativeZUp.Z);
        _filteredZ = follow.Value; _followZVelocity = follow.Velocity;

        // 0x002e5830: the actual position base follows the target with its own vector spring.
        (_positionBase, _positionVelocity) = SpringVector(_positionBase, _positionVelocity,
            input.PlayerNativeZUp, AngularAcceleration, AngularDamping);
        Vec3 anchor = input.PlayerNativeZUp + new Vec3(0, 0, _preferredEyeHeight);
        double heightTarget = OrdinaryEyeHeight;
        if (geometry is not null)
            anchor = ResolveHeightAnchor(geometry, input.PlayerNativeZUp, out heightTarget);

        // 0x002e8f40: carry the desired offset relative to the moving anchor. This is
        // the ordinary movement-follow turn, not automatic alignment to Ratchet yaw.
        _desiredOffset = _desiredOffset * .25d + (_previousDesiredEye - anchor) * .75d;
        double desiredLength = Length(_desiredOffset);
        double minLength = Math.Clamp(OrdinaryPreferredRadius - _obstructionCorrection, 0d, OrdinaryPreferredRadius);
        _desiredOffset = WithLength(_desiredOffset, Math.Clamp(desiredLength, minLength, OrdinaryPreferredRadius));
        _obstructionCorrection = Math.Max(0d, OrdinaryPreferredRadius - Length(_desiredOffset));

        TurnDesiredOffset(a, b, options.YawRadiansPerUpdate);
        if (geometry is null) UpdateObstruction(input.Obstruction);
        else ResolveObstruction(geometry, anchor, right.X, -right.Y);
        _desiredOffset = WithLength(_desiredOffset, OrdinaryPreferredRadius - _obstructionCorrection);
        _previousDesiredEye = anchor + _desiredOffset;

        // 0x002e9518: a run toward the camera chooses the faster radial response.
        double planarMovement = Math.Sqrt(displacement.X * displacement.X + displacement.Y * displacement.Y);
        bool movingTowardCamera = planarMovement > .0001d &&
            displacement.X * Math.Cos(_controlHeading) + displacement.Y * Math.Sin(_controlHeading) <= -.3d * planarMovement;
        var radialProfile = movingTowardCamera ? Rac1CameraRadialFollowProfile.Fast : input.RadialProfile;
        (double k, double d) = RadialCoefficients(radialProfile);
        var radius = Rac1CameraRecurrence.AdvanceDampedScalar(_currentRadius, _radialVelocity,
            OrdinaryPreferredRadius - _obstructionCorrection, k, d);
        _currentRadius = radius.Value; _radialVelocity = radius.Velocity;

        // 0x002e9720: length, pitch and yaw each have independent persistent velocity.
        double targetPitch = ArcSin(_desiredOffset.Z / Math.Max(Length(_desiredOffset), 1e-12));
        double currentPitch = ArcSin(_actualOffset.Z / Math.Max(_currentRadius, 1e-12));
        var pitch = SpringAngle(currentPitch, _offsetPitchVelocity, targetPitch);
        _offsetPitchVelocity = pitch.Velocity;
        double currentAzimuth = Math.Atan2(_actualOffset.Y, _actualOffset.X);
        double desiredAzimuth = Math.Atan2(_desiredOffset.Y, _desiredOffset.X);
        double yawDelta = Rac1CameraRecurrence.WrapPi(desiredAzimuth - currentAzimuth);
        var yaw = Rac1CameraRecurrence.AdvanceDampedScalar(0d, _offsetYawVelocity, Math.Abs(yawDelta), AngularAcceleration, AngularDamping);
        _offsetYawVelocity = yaw.Velocity;
        double azimuth = currentAzimuth + Math.CopySign(yaw.Value, yawDelta);
        _actualOffset = new Vec3(Math.Cos(azimuth) * Math.Cos(pitch.Value),
            Math.Sin(azimuth) * Math.Cos(pitch.Value), Math.Sin(pitch.Value)) * _currentRadius;
        Vec3 eyeAnchor = _positionBase + new Vec3(0, 0, _preferredEyeHeight);
        Vec3 eye = eyeAnchor + _actualOffset;

        // 0x002e9010: aim horizontally at the current target; height and the low-view
        // tilt use different springs. A moving target need not share the offset azimuth.
        double heading = Math.Atan2(input.PlayerNativeZUp.Y - eye.Y, input.PlayerNativeZUp.X - eye.X);
        _controlHeading = Rac1CameraRecurrence.WrapPi(heading);
        var height = Rac1CameraRecurrence.AdvanceEyeHeight(_eyeHeight, _eyeHeightVelocity, _preferredEyeHeight);
        _eyeHeight = height.Value; _eyeHeightVelocity = height.Velocity;
        var look = Rac1CameraRecurrence.AdvanceEyeHeight(_lookHeight, _lookHeightVelocity, OrdinaryLookHeight);
        _lookHeight = look.Value; _lookHeightVelocity = look.Velocity;
        double tilt = 0d;
        double elevationRatio = NativeOffsetElevation(_desiredOffset) / VerticalSpanRadians;
        if (elevationRatio < -.10000000149011612d)
        {
            double f = -elevationRatio;
            if (f > .5d) f = 1d - f;
            tilt = f * 2d * 0.2617993950843811d;
        }
        var tiltStep = Rac1CameraRecurrence.AdvanceDampedScalar(_aimTilt, _aimTiltVelocity, tilt, .004999999888241291d, AngularDamping);
        _aimTilt = tiltStep.Value; _aimTiltVelocity = tiltStep.Velocity;
        double planarDistance = Math.Sqrt(Math.Pow(input.PlayerNativeZUp.X - eye.X, 2) + Math.Pow(input.PlayerNativeZUp.Y - eye.Y, 2));
        double cameraPitch = Math.Clamp(Math.Atan2(_actualOffset.Z + _eyeHeight - _lookHeight,
            Math.Max(planarDistance, 1e-12)) - _aimTilt, -1.2217304706573486d, 1.2217304706573486d);

        var preferredHeight = Rac1CameraRecurrence.AdvanceDampedScalar(_preferredEyeHeight, _preferredHeightVelocity,
            heightTarget, Rac1CameraRecurrence.ProfileTransitionAcceleration, AngularDamping);
        _preferredEyeHeight = preferredHeight.Value; _preferredHeightVelocity = preferredHeight.Velocity;

        return new Rac1CameraState(new(_controlHeading, 0d, cameraPitch),
            new(new(input.PlayerNativeZUp.X, input.PlayerNativeZUp.Y, _filteredZ), input.PlayerNativeZUp.Z, _followZVelocity),
            new(eye, eyeAnchor, _actualOffset, _radialVelocity,
                OrdinaryPreferredRadius, 0d, Rac1CameraRecurrence.ProfileTransitionAcceleration,
                _lookHeight, _lookHeightVelocity, _eyeHeight, _eyeHeightVelocity, _preferredEyeHeight, _preferredHeightVelocity,
                Rac1CameraRecurrence.ProfileTransitionAcceleration),
            new(_obstructionCorrection, _obstructionReleaseTicks, _lateralSide)).Validate();
    }

    private void TurnDesiredOffset(double a, double b, double speed)
    {
        // 0x002e8bb8 uses k = d = 1. The apparent yaw release inertia lives in
        // the position spring; filtering the stick with .02/.2 moves it to the wrong stage.
        var yaw = Rac1CameraRecurrence.AdvanceDampedScalar(_manualYaw, _manualYawVelocity, a, 1d, 1d);
        _manualYaw = yaw.Value; _manualYawVelocity = yaw.Velocity;
        if (a != 0d) _desiredOffset = RotateZ(_desiredOffset, _manualYaw * speed);
        double p = RemapVerticalInput(b);
        double limit = p == 0d || Math.Sign(p) == Math.Sign(_manualPitch) && Math.Abs(p) < Math.Abs(_manualPitch)
            ? VerticalReleaseStep : VerticalActiveStep;
        double delta = p - _manualPitch;
        _manualPitchVelocity = Math.Clamp(delta, -limit, limit);
        _manualPitch += _manualPitchVelocity;
        double want = _manualPitch * VerticalSpanRadians;
        double elevation = NativeOffsetElevation(_desiredOffset);
        if (Math.Abs(elevation) < Math.Abs(want) && limit == VerticalReleaseStep)
            _manualPitch = elevation / VerticalSpanRadians;
        double turn = Math.Clamp(Rac1CameraRecurrence.WrapPi(want - elevation), -VerticalTurnLimit, VerticalTurnLimit);
        if (turn < 0d && _lateralSide < 0) turn = 0d;
        double actualElevation = Elevation(_desiredOffset) + turn;
        double heading = Math.Atan2(-_desiredOffset.Y, -_desiredOffset.X);
        _desiredOffset = ComposeVerticalOrbitOffset(heading, Length(_desiredOffset), actualElevation);
    }

    private void UpdateObstruction(RuntimeCameraObstructionFacts obstruction)
    {
        if (obstruction.HasContact)
        {
            _obstructionCorrection = OrdinaryPreferredRadius - Rac1CameraRecurrence.PullInWorkingRadius(OrdinaryPreferredRadius - _obstructionCorrection);
            _obstructionReleaseTicks = Rac1CameraRecurrence.ObstructionContactTimerTicks;
            _lateralSide = Rac1CameraRecurrence.ClassifyLateralSide(obstruction.LateralDot);
        }
        else
        {
            var clear = Rac1CameraRecurrence.AdvanceClearLineRecovery(_obstructionCorrection, _obstructionReleaseTicks);
            _obstructionCorrection = clear.RadialCorrection; _obstructionReleaseTicks = clear.ReleaseTimerTicks;
            _lateralSide = 0;
        }
        var guard = Rac1CameraRecurrence.EnforceFinalEffectiveRadiusFloor(OrdinaryPreferredRadius, _obstructionCorrection, _obstructionReleaseTicks);
        _obstructionCorrection = guard.RadialCorrection; _obstructionReleaseTicks = guard.ReleaseTimerTicks;
    }

    public static double RemapVerticalInput(double conditionedY)
    {
        ValidateFinite(conditionedY, nameof(conditionedY));
        double magnitude = Math.Abs(conditionedY);
        if (magnitude <= VerticalInputDeadzone) return 0d;
        return Math.CopySign(Math.Clamp((magnitude - VerticalInputDeadzone) / (1d - VerticalInputDeadzone), 0d, 1d), conditionedY);
    }

    public static Vec3 ComposeVerticalOrbitOffset(double heading, double radius, double orbitRadians)
    {
        ValidateFinite(heading, nameof(heading)); ValidateFinite(radius, nameof(radius)); ValidateFinite(orbitRadians, nameof(orbitRadians));
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        return new(-Math.Cos(heading) * radius * Math.Cos(orbitRadians),
            -Math.Sin(heading) * radius * Math.Cos(orbitRadians), radius * Math.Sin(orbitRadians));
    }

    public static (double Acceleration, double Damping) RadialCoefficients(Rac1CameraRadialFollowProfile profile) => profile switch
    {
        Rac1CameraRadialFollowProfile.Baseline => (BaselineRadialAcceleration, BaselineRadialDamping),
        Rac1CameraRadialFollowProfile.Fast => (FastRadialAcceleration, FastRadialDamping),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    private static Rac1CameraRecurrence.ScalarResult SpringAngle(double current, double velocity, double target)
    {
        var step = Rac1CameraRecurrence.AdvanceDampedScalar(0d, velocity,
            Rac1CameraRecurrence.WrapPi(target - current), AngularAcceleration, AngularDamping);
        return new(Rac1CameraRecurrence.WrapPi(current + step.Value), step.Velocity);
    }

    private static (Vec3 Value, Vec3 Velocity) SpringVector(Vec3 cur, Vec3 velocity, Vec3 target, double k, double d)
    {
        var x = Rac1CameraRecurrence.AdvanceDampedScalar(cur.X, velocity.X, target.X, k, d);
        var y = Rac1CameraRecurrence.AdvanceDampedScalar(cur.Y, velocity.Y, target.Y, k, d);
        var z = Rac1CameraRecurrence.AdvanceDampedScalar(cur.Z, velocity.Z, target.Z, k, d);
        return (new(x.Value, y.Value, z.Value), new(x.Velocity, y.Velocity, z.Velocity));
    }

    // The original atan helper's argument order is (horizontal, vertical).
    private static double NativeOffsetElevation(Vec3 v) => Math.Atan2(v.Z, Length(v));
    private static double Elevation(Vec3 v) => Math.Atan2(v.Z, Math.Sqrt(v.X * v.X + v.Y * v.Y));
    private static double Length(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    private static Vec3 WithLength(Vec3 v, double length) => v * (length / Math.Max(Length(v), 1e-12));
    private static Vec3 RotateZ(Vec3 v, double angle) => new(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle), v.Z);
    // 0x001ff818, coefficients at 0x00160760. Its small non-zero result at zero
    // is observable in the native camera's settled vertical offset.
    private static double ArcSin(double value)
    {
        double x = Math.Clamp(Math.Abs(value), 0d, 1d);
        double polynomial = 1.5707287788391113d - x * .2121143937110901d +
            x * x * .07426100224256516d - x * x * x * .018729299306869507d;
        double angle = 1.5707963705062866d - polynomial * Math.Sqrt(1d - x);
        return value < 0d ? -angle : angle;
    }
    private static void ValidateFinite(Vec3 v, string name)
    {
        if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z)) throw new ArgumentOutOfRangeException(name);
    }
    private static void ValidateFinite(double v, string name)
    {
        if (!double.IsFinite(v)) throw new ArgumentOutOfRangeException(name);
    }
}

using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Source-specific equipment and collision facts. Forward clearance is the
/// P+0x248 obstruction query, separately from ground clearance P+0x2dc.
/// Missing contextual producers must be supplied conservatively by the host.
/// </summary>
public readonly record struct Rac1MovementContext(
    int ActivePackId, bool ClankAvailable, double FacingYaw, double ControlYaw,
    float GroundClearance, float ForwardClearance = 0, float ForwardNormalAngle = 1.5707964f,
    bool ForwardCrateFamily = false, bool PackDisabled = false, float NativePositionHeight = 0,
    bool NativeAnimationExitRequested = false, Rac1AirborneWallProbeResult? WallProbe = null,
    float AuthoredAnimationTime = 0);

public sealed partial class Rac1RatchetMovementController
{
    private Rac1AirborneMotionSession? _specialAir;
    private Rac1AirborneBoostSession? _airBoost;
    private Rac1AirbornePowerSlamSession? _powerSlam;
    private Rac1AirborneHeliLongSession? _heliLong;
    private Rac1AirborneLandingSession? _landing;
    private Rac1MovementAnimationSelection? _ordinaryAirAnimation;
    private int? _postLandingGroundAction;
    private bool _boostAvailable;
    private float _originalLaunchHeight;
    private bool _nativeAirPlanar;
    private float _specialEntryGroundClearance;
    private float _specialEntryFacing;
    private Rac1AirborneFlipPlanarSession? _flipPlanar;
    private readonly Queue<float> _inputYawHistory = new();
    private float _historicalInputYaw;
    private int _specialAge;
    private bool _specialLeftSupport;
    private bool _specialCeilingInterrupted;
    private bool _gliding;
    private int _ordinaryAirAge;
    private int _crouchAge;
    private int _crossBuffer;
    private int _crossPressAge = 1000;
    private int _crouchPressAge = 1000;
    private bool _extendedCrouchWasHeld;
    private long _crossPressSerial;
    private long _launchPressSerial;
    private int _directionBuffer;
    private int _previousInputDirection = 2;
    private bool _previousExtendedInputActive;
    private long _traversalGeneration;
    private int _wallLease;
    private double _wallNormalYaw;
    private double _wallLaunchNormalYaw;
    private bool _hasWallJumped;
    private double _previousWallNormalYaw;

    public Rac1MovementAnimationSelection? TraversalAnimation { get; private set; }
    public Rac1MovementAnimationSelection? OrdinaryAirAnimation => _ordinaryAirAnimation;
    public Rac1MovementAnimationSelection? GroundAnimationEntry { get; private set; }
    public bool IsLanding => _landing is not null;
    public int? TraversalAction => TraversalAnimation?.NativeAction;
    public int TraversalAge => _gliding || _specialAir is not null || _airBoost is not null || _powerSlam is not null
        ? _specialAge : _ordinaryAirAge;
    public double? TraversalFacingTargetYaw => _specialAir?.Profile.Kind == Rac1AirborneJumpKind.DirectionalFlip
        ? _flipPlanar?.FacingTargetYaw : _specialAir?.Profile.Kind == Rac1AirborneJumpKind.WallJump && _specialAge > 9
            ? _wallLaunchNormalYaw : null;
    public int CurrentNativeMovementAction => _landing?.NativeAction ?? TraversalAction ?? _ordinaryAirAnimation?.NativeAction ??
        _postLandingGroundAction ??
        (Phase != Rac1RatchetMovementPhase.Grounded && !IsOrdinaryEdgeFall ? 7 : _crouchAge > 0 ? 4 : 2);
    public int CurrentMovementDomain => _powerSlam is not null ? 11 :
        Phase != Rac1RatchetMovementPhase.Grounded && !IsOrdinaryEdgeFall ? 4 : 2;
    public bool HasWallJumped => _hasWallJumped;
    public int CurrentFlipDirection => _flipPlanar?.FlipDirection ?? 3;
    public double PreviousWallNormalYaw => _previousWallNormalYaw;
    public double CurrentAirGravityPerUpdate => _specialAir?.GravityPerUpdate ??
        (Phase == Rac1RatchetMovementPhase.Falling ? FallGravityPerTick : ReleasedRiseGravityPerTick);

    public bool MayReenterWater => _specialAir?.Profile.Kind != Rac1AirborneJumpKind.WaterExit ||
        _specialAge > Math.Max(_specialAir.Profile.LaunchAge, _specialAir.Profile.ScriptEndAge) && _verticalStep <= 0;

    /// <summary>
    /// Extended regular-axis movement. Source profiles and admission predicates
    /// own special jumps and gliding; ordinary jump timing retains its witnessed
    /// recurrence. Input buffering and special-jump planar steering are bounded
    /// OBP adapters, not a claim of complete native dispatcher parity.
    /// </summary>
    public StepResult StepExtended(PlayerControlIntent input, Rac1PlayerContactResult contact,
        Rac1MovementContext context, Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw = null)
    {
        _nativeAirPlanar = true;
        try { return StepExtendedCore(input, contact, context, resolveNativeFacingYaw); }
        finally { _nativeAirPlanar = false; }
    }

    private StepResult StepExtendedCore(PlayerControlIntent input, Rac1PlayerContactResult contact,
        Rac1MovementContext context, Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (!double.IsFinite(context.FacingYaw) || !double.IsFinite(context.ControlYaw) ||
            !float.IsFinite(context.GroundClearance) || context.GroundClearance < 0 ||
            !float.IsFinite(context.ForwardClearance) || context.ForwardClearance < 0 ||
            !float.IsFinite(context.ForwardNormalAngle) || !float.IsFinite(context.NativePositionHeight))
            throw new ArgumentOutOfRangeException(nameof(context));
        AnalogueInput = Rac1AnalogueInput.ConditionUnitAxes(input.PlanarX, input.PlanarY);
        GroundAnimationEntry = null;
        UpdateExtendedInputBuffers(input, context);
        _specialLeftSupport |= !contact.IsGrounded && Phase != Rac1RatchetMovementPhase.Grounded;
        if (_landing is not null || TryBeginLanding(contact, context))
            return StepLanding(input, contact, context, resolveNativeFacingYaw);
        if (TryStepPostLandingCarry(input, contact, out var carryStep))
            return carryStep;

        if (contact.IsGrounded && Phase == Rac1RatchetMovementPhase.Grounded)
        {
            _wallLease = 0;
            _hasWallJumped = false;
        }
        else
            _wallLease = Math.Max(0, _wallLease - 1);
        if (context.WallProbe is { Outcome: Rac1AirborneWallProbeOutcome.Refreshed } wall)
        {
            _wallLease = wall.CandidateLeaseUpdates;
            _wallNormalYaw = wall.NormalYaw;
        }
        else if (context.WallProbe is { Outcome: Rac1AirborneWallProbeOutcome.ClearedForLedge })
            _wallLease = 0;
        int wallAge = _specialAir is not null ? _specialAge : _ordinaryAirAge;
        if (!contact.IsGrounded && _crossPressSerial > _launchPressSerial &&
            Rac1AirborneWallProbe.CanEnter(new(CurrentNativeMovementAction, wallAge, _wallLease,
                false, _crossPressAge < 7, _crossPressAge)))
        {
            _hasWallJumped = true;
            _previousWallNormalYaw = _wallNormalYaw;
            _wallLease = 0;
            BeginSpecialJump(Rac1AirborneJumpKind.WallJump, context, false);
        }

        if (_powerSlam is not null)
            return StepPowerSlam(input, contact, context, resolveNativeFacingYaw);
        int airAction = _specialAir is { } special ? (int)special.Profile.Kind : _airBoost is not null ? 14 : _gliding ? 8 :
            Phase != Rac1RatchetMovementPhase.Grounded && !IsOrdinaryEdgeFall ? 7 : 2;
        int age = _specialAir is not null || _airBoost is not null ? _specialAge : _ordinaryAirAge;
        int launchAge = _specialAir?.Profile.LaunchAge ?? (_airBoost is not null ? -1 : 5);
        int landingTicks = EstimateLandingTicks(context.GroundClearance, FallGravityPerTick);
        if (!contact.IsGrounded && Rac1AirbornePowerSlamSession.CanEnter(new(airAction, age, launchAge,
                _verticalStep <= 0, landingTicks, (float)AnalogueInput.Magnitude, _crouchPressAge < 9,
                context.ActivePackId, context.PackDisabled)))
        {
            _powerSlam = new((float)_planarX, (float)_planarY, (float)_verticalStep, (float)_planarMagnitude);
            _specialAir = null;
            _airBoost = null;
            _ordinaryAirAnimation = null;
            _gliding = false;
            _boostAvailable = false;
            _specialAge = 0;
            _traversalGeneration++;
            return StepPowerSlam(input, contact, context, resolveNativeFacingYaw);
        }

        if (_gliding)
        {
            if (contact.IsGrounded || !input.JumpHeld && _specialAge > 30 || context.ActivePackId is not (2 or 3) ||
                context.PackDisabled || !context.ClankAvailable)
            {
                _gliding = false;
                TraversalAnimation = null;
                _ordinaryEdgeFall = false;
            }
            else
                return StepGlide(input, context, resolveNativeFacingYaw);
        }
        if (_specialAir is not null)
            return StepSpecialAir(input, contact, context, resolveNativeFacingYaw);
        if (_airBoost is not null)
            return StepAirBoost(input, contact, context, resolveNativeFacingYaw);
        if (!contact.IsGrounded && _boostAvailable && _crossPressSerial > _launchPressSerial &&
            Rac1AirborneBoostSession.CanEnter(new(7, _ordinaryAirAge,
                _crossPressAge < Rac1AirborneBoostSession.CrossBufferWindow(_ordinaryAirAge), true, false, false,
                false, context.GroundClearance, Phase == Rac1RatchetMovementPhase.Falling, (float)_verticalStep)))
        {
            _boostAvailable = false;
            _airBoost = new((float)(_contactProcessedMotion?.Z ?? _verticalStep));
            _specialAge = 0;
            _traversalGeneration++;
            return StepAirBoost(input, contact, context, resolveNativeFacingYaw);
        }

        if (contact.IsGrounded && Phase == Rac1RatchetMovementPhase.Grounded && input.CrouchHeld)
        {
            _crouchAge++;
            var kind = SelectCrouchJump(input, context);
            if (kind is { } jump)
            {
                _crossBuffer = 0;
                if (jump is Rac1AirborneJumpKind.Stationary or Rac1AirborneJumpKind.Moving)
                {
                    // Native no-pack crouch/Cross falls back to ordinary jump.
                    _originalLaunchHeight = context.NativePositionHeight;
                    _boostAvailable = true;
                    _launchPressSerial = _crossPressSerial;
                    BeginOrdinaryAirAnimation();
                    _ordinaryAirAge = 1;
                    return Step(input with { CrouchHeld = false, JumpPressed = true, JumpHeld = true },
                        contact, resolveNativeFacingYaw);
                }
                BeginSpecialJump(jump, context, fastEntry: false);
                return StepSpecialAir(input, contact, context, resolveNativeFacingYaw);
            }
        }
        else
            _crouchAge = 0;

        var previousPhase = Phase;
        var ordinary = Step(input, contact, resolveNativeFacingYaw);
        if (previousPhase == Rac1RatchetMovementPhase.JumpAnticipation && Phase == Rac1RatchetMovementPhase.Rising)
            _originalLaunchHeight = context.NativePositionHeight;
        if (previousPhase == Rac1RatchetMovementPhase.Grounded && Phase == Rac1RatchetMovementPhase.JumpAnticipation)
        {
            _originalLaunchHeight = context.NativePositionHeight;
            _boostAvailable = true;
            _launchPressSerial = _crossPressSerial;
            BeginOrdinaryAirAnimation();
        }
        else if (Phase == Rac1RatchetMovementPhase.Grounded)
        {
            _boostAvailable = false;
            _ordinaryAirAnimation = null;
        }
        bool ordinaryAir = Phase != Rac1RatchetMovementPhase.Grounded && !IsOrdinaryEdgeFall;
        _ordinaryAirAge = ordinaryAir ? _ordinaryAirAge + 1 : 0;
        if (ordinaryAir && Rac1MovementEquipmentGlide.CanEnter(context.ActivePackId,
                context.ClankAvailable, context.PackDisabled, input.JumpHeld,
                _ordinaryAirAge, 30, context.GroundClearance, 7,
                recentCrossPress: _crossPressSerial > _launchPressSerial &&
                    _crossPressAge < Rac1AirborneBoostSession.CrossBufferWindow(_ordinaryAirAge)))
        {
            BeginGlide();
            return StepGlide(input, context, resolveNativeFacingYaw: null);
        }
        return ordinary;
    }

    /// <summary>Admitted ledge action 0x1c; the ledge session owns the decision.</summary>
    public void BeginLedgeJump(Rac1MovementContext context) =>
        BeginSpecialJump(Rac1AirborneJumpKind.LedgeJump, context, fastEntry: false);

    /// <summary>Water action owner admitted Cross; retain the native depth-specific action 12.</summary>
    public void BeginWaterExitJump(Rac1MovementContext context, float waterDepth) =>
        BeginSpecialJump(Rac1AirborneJumpKind.WaterExit, context, false, waterDepth);

    /// <summary>
    /// A traversal/contact adapter transfers its completed motion back to the
    /// ordinary controller. This is an OBP ownership seam, not a native entry.
    /// </summary>
    public void ResumeAfterTraversal(double planarX, double planarY, double verticalStep)
    {
        if (!double.IsFinite(planarX) || !double.IsFinite(planarY) || !double.IsFinite(verticalStep))
            throw new ArgumentOutOfRangeException(nameof(verticalStep));
        ResetExtendedMovement();
        _planarX = planarX;
        _planarY = planarY;
        _planarMagnitude = Math.Sqrt(planarX * planarX + planarY * planarY);
        _verticalStep = verticalStep;
        _anticipationTicks = 0;
        _jumpHeldTicks = 0;
        // External release momentum is not an ordinary held-jump launch.
        _heldRiseTicks = HeldRiseDecrements.Length;
        _contactProcessedMotion = null;
        _groundReleaseSampleIndex = -1;
        _wasGroundedCrouch = false;
        _ordinaryEdgeFall = false;
        Phase = verticalStep > 0 ? Rac1RatchetMovementPhase.Rising : Rac1RatchetMovementPhase.Falling;
        LocomotionState = verticalStep > 0 ? Rac1RatchetLocomotionState.Rising : Rac1RatchetLocomotionState.Falling;
        YawMode = Rac1RatchetYawMode.Air;
    }

    private void BeginSpecialJump(Rac1AirborneJumpKind kind, Rac1MovementContext context, bool fastEntry,
        float environmentDepth = 0)
    {
        if (kind == Rac1AirborneJumpKind.WallJump)
            _wallLaunchNormalYaw = _wallNormalYaw;
        int direction = ExtendedInputDirection(context);
        var profile = Rac1AirborneJumpProfile.Create(kind, fastEntry, direction, environmentDepth: environmentDepth);
        _specialAir = new Rac1AirborneMotionSession(profile, (float)(_contactProcessedMotion?.Z ?? _verticalStep));
        _ordinaryAirAnimation = null;
        _postLandingGroundAction = null;
        _heliLong = kind == Rac1AirborneJumpKind.HeliPackLong
            ? new((float)_planarMagnitude, default) : null;
        _specialEntryGroundClearance = context.GroundClearance;
        _specialEntryFacing = (float)context.FacingYaw;
        _launchPressSerial = _crossPressSerial;
        if (kind == Rac1AirborneJumpKind.DirectionalFlip)
        {
            float inputYaw = (float)Rac1RatchetYawController.BuildMovementTarget(
                AnalogueInput.X, AnalogueInput.Y, context.ControlYaw);
            float inherited = _contactProcessedMotion is { } flipContact
                ? (float)Math.Sqrt(flipContact.X * flipContact.X + flipContact.Y * flipContact.Y)
                : (float)_planarMagnitude;
            _flipPlanar = new(inputYaw, _historicalInputYaw, inherited, direction);
        }
        _specialAge = 0;
        _specialLeftSupport = false;
        _specialCeilingInterrupted = false;
        _ordinaryEdgeFall = false;
        _traversalGeneration++;
        Phase = Rac1RatchetMovementPhase.JumpAnticipation;
        TraversalAnimation = new((int)kind, profile.EntrySequence, _traversalGeneration,
            profile.EntryFrame, profile.EntryBlendTicks);
    }

    private StepResult StepSpecialAir(PlayerControlIntent input, Rac1PlayerContactResult contact,
        Rac1MovementContext context, Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw)
    {
        var session = _specialAir!;
        _specialCeilingInterrupted |= contact.HitCeiling;
        _specialLeftSupport |= !contact.IsGrounded;
        var vertical = session.Step(new(_specialAge++, input.JumpHeld,
            (float)(_contactProcessedMotion?.Z ?? _verticalStep), contact.HitCeiling,
            false));
        if (vertical.Finished || _specialCeilingInterrupted && contact.IsGrounded)
        {
            _specialAir = null;
            TraversalAnimation = null;
            Phase = Rac1RatchetMovementPhase.Grounded;
            _verticalStep = 0;
            return Step(input with { JumpPressed = false }, contact, resolveNativeFacingYaw);
        }
        if (session.Profile.Kind == Rac1AirborneJumpKind.WallJump)
        {
            // Source uses the cached outward wall-normal heading. Preserve
            // the inherited request during its nine-update anticipation.
            var basis = input.EffectiveNativePlanarBasis;
            double determinant = basis.RightX * basis.ForwardY - basis.ForwardX * basis.RightY;
            if (Math.Abs(determinant) < 1e-12d)
                throw new ArgumentException("Wall jumping requires an invertible native planar basis.", nameof(input));
            var nativeInput = new Rac1AirbornePlanarMotion.PlanarStep(
                (float)((_planarX * basis.ForwardY - _planarY * basis.ForwardX) / determinant),
                (float)((_planarY * basis.RightX - _planarX * basis.RightY) / determinant));
            SetNativePlanar(Rac1AirborneWallJump.Step(_specialAge - 1, (float)_wallLaunchNormalYaw, nativeInput), input);
        }
        else if (session.Profile.Kind == Rac1AirborneJumpKind.HeliPackLong)
        {
            SetNativePlanar(_heliLong!.Step(_specialAge - 1, (float)AnalogueInput.Magnitude,
                (float)context.FacingYaw,
                _contactProcessedMotion is { } heliContact
                    ? (float)Math.Sqrt(heliContact.X * heliContact.X + heliContact.Y * heliContact.Y) : null), input);
            _planarMagnitude = _heliLong.Scalar;
        }
        else if (session.Profile.Kind == Rac1AirborneJumpKind.ThrusterPackLong)
        {
            var planar = Rac1AirbornePlanarMotion.StepThrusterLong((float)_planarMagnitude,
                _specialAge - 1, _specialEntryGroundClearance, _specialEntryFacing);
            SetNativePlanar(planar, input);
        }
        else if (session.Profile.Kind == Rac1AirborneJumpKind.DirectionalFlip && _flipPlanar is not null)
        {
            // P+0x44c's exact landing prediction is still supplied by an OBP
            // ballistic estimate. The source planar response is kept separate.
            float gravity = session.GravityPerUpdate;
            double discriminant = _verticalStep * _verticalStep + 2 * gravity * context.GroundClearance;
            int landing = gravity > 0 ? Math.Max(0, (int)Math.Ceiling((_verticalStep + Math.Sqrt(discriminant)) / gravity)) : 0;
            SetNativePlanar(_flipPlanar.Step(vertical.HasLaunched && vertical.VerticalStep <= 0, landing), input);
            _planarMagnitude = _flipPlanar.ControlledScalar;
        }
        else
            UpdateSpecialAirPlanar(input, (int)session.Profile.Kind);
        _verticalStep = vertical.VerticalStep;
        Phase = !vertical.HasLaunched ? Rac1RatchetMovementPhase.JumpAnticipation
            : _verticalStep > 0 ? Rac1RatchetMovementPhase.Rising : Rac1RatchetMovementPhase.Falling;
        LocomotionState = !vertical.HasLaunched ? Rac1RatchetLocomotionState.JumpAnticipation
            : _verticalStep > 0 ? Rac1RatchetLocomotionState.Rising : Rac1RatchetLocomotionState.Falling;
        YawMode = Rac1RatchetYawMode.Air;
        if (session.Profile.Kind is not (Rac1AirborneJumpKind.ThrusterPackLong or Rac1AirborneJumpKind.DirectionalFlip or
            Rac1AirborneJumpKind.WallJump) &&
            (session.Profile.Kind != Rac1AirborneJumpKind.HeliPackLong || !vertical.HasLaunched))
            resolveNativeFacingYaw?.Invoke(YawMode);
        if (!contact.IsGrounded && Rac1MovementEquipmentGlide.CanEnter(context.ActivePackId,
                context.ClankAvailable, context.PackDisabled, input.JumpHeld, _specialAge,
                session.Profile.GlideAdmissionAge, context.GroundClearance, (int)session.Profile.Kind,
                recentCrossPress: _crossPressSerial > _launchPressSerial &&
                    _crossPressAge < Rac1AirborneBoostSession.CrossBufferWindow(_specialAge)))
        {
            BeginGlide();
            return StepGlide(input, context, resolveNativeFacingYaw: null);
        }
        return new(_planarX, _planarY, _verticalStep, Phase, LocomotionState, YawMode);
    }

    private StepResult StepAirBoost(PlayerControlIntent input, Rac1PlayerContactResult contact,
        Rac1MovementContext context, Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw)
    {
        _verticalStep = _airBoost!.Step(new(_specialAge++,
            (float)(_contactProcessedMotion?.Z ?? _verticalStep),
            context.NativePositionHeight - _originalLaunchHeight, context.ActivePackId,
            context.ClankAvailable, context.PackDisabled, contact.HitCeiling, false));
        if (_airBoost.Finished)
        {
            _airBoost = null;
            TraversalAnimation = null;
            Phase = Rac1RatchetMovementPhase.Grounded;
            return Step(input with { JumpPressed = false }, contact, resolveNativeFacingYaw);
        }
        UpdateSpecialAirPlanar(input, 14);
        Phase = _verticalStep > 0 ? Rac1RatchetMovementPhase.Rising : Rac1RatchetMovementPhase.Falling;
        LocomotionState = _verticalStep > 0 ? Rac1RatchetLocomotionState.Rising : Rac1RatchetLocomotionState.Falling;
        YawMode = Rac1RatchetYawMode.Air;
        resolveNativeFacingYaw?.Invoke(YawMode);
        TraversalAnimation = new(14, 22, _traversalGeneration, 3, 7, PlaybackMultiplier: .8f);
        if (Rac1MovementEquipmentGlide.CanEnter(context.ActivePackId, context.ClankAvailable,
            context.PackDisabled, input.JumpHeld, _specialAge, 17, context.GroundClearance, 14))
        {
            BeginGlide();
            return StepGlide(input, context, null);
        }
        return new(_planarX, _planarY, _verticalStep, Phase, LocomotionState, YawMode);
    }

    private void UpdateSpecialAirPlanar(PlayerControlIntent input, int action)
    {
        float maximum = Rac1AirbornePlanarMotion.MaximumTargetStep(action);
        var direction = input.EffectivePlanarBasis.Transform(AnalogueInput.Direction.X, AnalogueInput.Direction.Y);
        double norm = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        float target = AnalogueInput.IsActive ? maximum * (float)AnalogueInput.Magnitude : 0;
        var desired = norm > 0 ? new Rac1AirbornePlanarMotion.PlanarStep(
            (float)(direction.X / norm) * target, (float)(direction.Y / norm) * target) : default;
        var next = Rac1AirbornePlanarMotion.Converge(new((float)_planarX, (float)_planarY), desired,
            Rac1AirbornePlanarMotion.OrdinaryBaseAmount(target, nativeWallContact: false));
        if (action == 0x12 && next.Magnitude > 3.7f / 60f)
        {
            float scale = (3.7f / 60f) / next.Magnitude;
            next = new(next.X * scale, next.Y * scale);
        }
        _planarX = next.X;
        _planarY = next.Y;
        _planarMagnitude = next.Magnitude;
        TargetPlanarStep = target;
    }

    private void SetNativePlanar(Rac1AirbornePlanarMotion.PlanarStep native, PlayerControlIntent input)
    {
        var scene = input.EffectiveNativePlanarBasis.Transform(native.X, native.Y);
        _planarX = scene.X;
        _planarY = scene.Y;
        _planarMagnitude = Math.Sqrt(_planarX * _planarX + _planarY * _planarY);
    }

    private static Rac1AirbornePlanarMotion.PlanarStep ToNativePlanar(PlayerControlIntent input, double x, double y)
    {
        var basis = input.EffectiveNativePlanarBasis;
        double determinant = basis.RightX * basis.ForwardY - basis.ForwardX * basis.RightY;
        if (Math.Abs(determinant) < 1e-12d)
            throw new ArgumentException("Native motion requires an invertible planar basis.", nameof(input));
        return new((float)((x * basis.ForwardY - y * basis.ForwardX) / determinant),
            (float)((y * basis.RightX - x * basis.RightY) / determinant));
    }

    private void BeginOrdinaryAirAnimation()
    {
        bool moving = _planarMagnitude > 1e-12d;
        _ordinaryAirAnimation = new(moving ? 9 : 7, moving ? 8 : 7, ++_traversalGeneration, 0, 5);
        _specialLeftSupport = false;
        _postLandingGroundAction = null;
    }

    private bool TryBeginLanding(Rac1PlayerContactResult contact, Rac1MovementContext context)
    {
        int? action = _specialAir is { } special ? (int)special.Profile.Kind :
            _airBoost is not null ? 14 : _ordinaryAirAnimation?.NativeAction;
        if (action is null || _gliding || _powerSlam is not null || !_specialLeftSupport)
            return false;
        if (_specialAir is { HasLaunched: false })
            return false;
        if (!MayReenterWater) return false;
        int age = _specialAir is not null || _airBoost is not null ? _specialAge : _ordinaryAirAge;
        int launchAge = _specialAir?.Profile.LaunchAge ?? (_airBoost is not null ? -1 : 8);
        if (!Rac1AirborneLandingSession.CanBegin(new(action.Value, age, launchAge,
                contact.IsGrounded ? 0 : 1, _verticalStep <= 0, context.AuthoredAnimationTime)))
            return false;
        _landing = new(action.Value, (float)_planarMagnitude);
        return true;
    }

    private StepResult StepLanding(PlayerControlIntent input, Rac1PlayerContactResult contact,
        Rac1MovementContext context, Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw)
    {
        var contactMotion = _contactProcessedMotion ?? new(_planarX, _planarY, _verticalStep);
        var nativeContact = ToNativePlanar(input, contactMotion.X, contactMotion.Y);
        var nativeRequest = ToNativePlanar(input, _planarX, _planarY);
        float forward = MathF.Max(0, nativeRequest.X * MathF.Cos((float)context.FacingYaw) +
            nativeRequest.Y * MathF.Sin((float)context.FacingYaw));
        var landing = _landing!.Step(new((float)context.FacingYaw, (float)AnalogueInput.Magnitude,
            input.CrouchHeld, forward, new(nativeContact.X, nativeContact.Y, contactMotion.Z),
            context.NativeAnimationExitRequested));
        float gravity = _specialAir?.GravityPerUpdate ??
            29.7f * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate;
        SetNativePlanar(landing.Request, input);
        _planarMagnitude = landing.ControlledScalar;
        _verticalStep = -gravity;
        Phase = Rac1RatchetMovementPhase.Falling;
        LocomotionState = Rac1RatchetLocomotionState.Falling;
        YawMode = Rac1RatchetYawMode.Air;
        resolveNativeFacingYaw?.Invoke(YawMode);
        _specialAge++;
        _ordinaryAirAge++;
        var motion = new StepResult(_planarX, _planarY, _verticalStep, Phase, LocomotionState, YawMode);
        if (landing.GroundHandoff is not { } handoff)
            return motion;

        int previousSequence = (TraversalAnimation ?? _ordinaryAirAnimation)!.Value.Sequence;
        _specialAir = null;
        _airBoost = null;
        _heliLong = null;
        _flipPlanar = null;
        _landing = null;
        _ordinaryAirAnimation = null;
        TraversalAnimation = null;
        _boostAvailable = false;
        _ordinaryEdgeFall = false;
        _anticipationTicks = 0;
        _jumpHeldTicks = 0;
        _heldRiseTicks = HeldRiseDecrements.Length;
        _lastGroundedActiveSpeedBand = Rac1AnalogueSpeedBand.Inactive;
        _groundReleaseSampleIndex = -1;
        _wasGroundedCrouch = false;
        _crouchAge = 0;
        if (handoff.NextAction == 2)
        {
            _planarMagnitude = Math.Min(handoff.ControlledScalar, 7f * (1f / 60f));
            AlignGroundTranslationToFacing(context.FacingYaw, input.EffectiveNativePlanarBasis);
        }
        else
        {
            SetNativePlanar(new((float)handoff.ContactCarry.X, (float)handoff.ContactCarry.Y), input);
            _postLandingGroundAction = handoff.NextAction is 0 or 3 ? handoff.NextAction : null;
            // The crouch entry consumes this exact admitted contact vector once.
            _contactProcessedMotion = new(_planarX, _planarY, handoff.ContactCarry.Z);
        }
        _verticalStep = 0;
        TargetPlanarStep = 0;
        Phase = Rac1RatchetMovementPhase.Grounded;
        LocomotionState = _planarMagnitude > 1e-12 ? Rac1RatchetLocomotionState.Moving : Rac1RatchetLocomotionState.Idle;
        YawMode = Rac1RatchetYawMode.GroundStartup;
        GroundAnimationEntry = new(handoff.NextAction, handoff.Sequence ?? previousSequence,
            ++_traversalGeneration, handoff.EntryFrame ?? 0, handoff.BlendTicks);
        // Contact is supplied by the preceding completed host update. This
        // bounded adapter emits the landing request before next-update carry.
        return motion;
    }

    private bool TryStepPostLandingCarry(PlayerControlIntent input, Rac1PlayerContactResult contact, out StepResult step)
    {
        step = default;
        if (_postLandingGroundAction is not int action)
            return false;
        if (!contact.IsGrounded || input.JumpPressed || input.CrouchHeld || AnalogueInput.IsActive)
        {
            _postLandingGroundAction = null;
            return false;
        }
        var carry = Rac1AirborneLandingSession.BrakeGroundCarry(action,
            new((float)_planarX, (float)_planarY));
        _planarX = carry.X;
        _planarY = carry.Y;
        _planarMagnitude = carry.Magnitude;
        _verticalStep = 0;
        TargetPlanarStep = 0;
        Phase = Rac1RatchetMovementPhase.Grounded;
        LocomotionState = _planarMagnitude > 0 ? Rac1RatchetLocomotionState.Moving : Rac1RatchetLocomotionState.Idle;
        YawMode = Rac1RatchetYawMode.GroundStartup;
        if (_planarMagnitude == 0)
            _postLandingGroundAction = null;
        step = new(_planarX, _planarY, 0, Phase, LocomotionState, YawMode);
        return true;
    }

    private StepResult StepGlide(PlayerControlIntent input, Rac1MovementContext context,
        Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw)
    {
        var step = Rac1MovementEquipmentGlide.Step(context.ActivePackId,
            (float)_planarMagnitude, (float)AnalogueInput.Magnitude);
        _planarMagnitude = step.PlanarScalar;
        _verticalStep = step.VerticalStep;
        YawMode = Rac1RatchetYawMode.Air;
        double yaw = resolveNativeFacingYaw?.Invoke(YawMode) ?? context.FacingYaw;
        AlignGroundTranslationToFacing(yaw, input.EffectiveNativePlanarBasis);
        Phase = Rac1RatchetMovementPhase.Falling;
        LocomotionState = Rac1RatchetLocomotionState.Falling;
        TraversalAnimation = new(8, 19, _traversalGeneration, 0, -2, Loop: true);
        _specialAge++;
        return new(_planarX, _planarY, _verticalStep, Phase, LocomotionState, YawMode);
    }

    private void BeginGlide()
    {
        float contactLength = (float)(_contactProcessedMotion?.Length ??
            Math.Sqrt(_planarMagnitude * _planarMagnitude + _verticalStep * _verticalStep));
        _planarMagnitude = Rac1MovementEquipmentGlide.EnterScalar((float)_planarMagnitude, contactLength);
        _specialAir = null;
        _airBoost = null;
        _ordinaryAirAnimation = null;
        _gliding = true;
        _specialAge = 0;
        _traversalGeneration++;
    }

    private StepResult StepPowerSlam(PlayerControlIntent input, Rac1PlayerContactResult contact, Rac1MovementContext context,
        Func<Rac1RatchetYawMode, double>? resolveNativeFacingYaw)
    {
        int age = _specialAge++;
        var slam = _powerSlam!.Step(new(age, contact.IsGrounded ? 0 : 1,
            age > 0 && context.NativeAnimationExitRequested));
        if (slam.Finished)
        {
            _powerSlam = null;
            TraversalAnimation = null;
            _verticalStep = 0;
            Phase = contact.IsGrounded ? Rac1RatchetMovementPhase.Grounded : Rac1RatchetMovementPhase.Falling;
            return Step(input with { JumpPressed = false }, contact, resolveNativeFacingYaw);
        }
        _planarX = slam.PlanarX;
        _planarY = slam.PlanarY;
        _planarMagnitude = _powerSlam.PlanarScalar;
        _verticalStep = slam.Vertical;
        Phase = _verticalStep > 0 ? Rac1RatchetMovementPhase.Rising : Rac1RatchetMovementPhase.Falling;
        LocomotionState = _verticalStep > 0 ? Rac1RatchetLocomotionState.Rising : Rac1RatchetLocomotionState.Falling;
        YawMode = Rac1RatchetYawMode.Air;
        TraversalAnimation = new(0x22, 42, _traversalGeneration, 0, 6);
        return new(_planarX, _planarY, _verticalStep, Phase, LocomotionState, YawMode);
    }

    private int EstimateLandingTicks(double clearance, double gravity)
    {
        double discriminant = _verticalStep * _verticalStep + 2 * gravity * clearance;
        return Math.Max(0, (int)Math.Ceiling((_verticalStep + Math.Sqrt(discriminant)) / gravity));
    }

    private Rac1AirborneJumpKind? SelectCrouchJump(PlayerControlIntent input, Rac1MovementContext context)
    {
        var forward = input.EffectiveNativePlanarBasis.Transform(Math.Cos(context.FacingYaw), Math.Sin(context.FacingYaw));
        double norm = Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        float forwardRequest = norm > 0
            ? (float)Math.Max(0, (_planarX * forward.X + _planarY * forward.Y) / norm) : 0;
        return Rac1AirborneAdmission.SelectCrouchJump(new(
            4, _crouchAge, input.CrouchHeld, _crossBuffer > 0,
            _crossBuffer > 0 && _directionBuffer > 0, ExtendedInputDirection(context),
            context.ActivePackId, context.ClankAvailable, context.PackDisabled,
            context.ForwardClearance, context.ForwardNormalAngle, context.ForwardCrateFamily,
            (float)AnalogueInput.Magnitude, forwardRequest, ThrusterLongJumpGate: false)).Kind;
    }

    private int ExtendedInputDirection(Rac1MovementContext context)
    {
        if (!AnalogueInput.IsActive)
            return 2;
        double target = Rac1RatchetYawController.BuildMovementTarget(AnalogueInput.X, AnalogueInput.Y, context.ControlYaw);
        return Rac1AirborneAdmission.ClassifyFlipDirection((float)target,
            (float)Rac1RatchetYawController.WrapPi(context.FacingYaw));
    }

    private void UpdateExtendedInputBuffers(PlayerControlIntent input, Rac1MovementContext context)
    {
        _crossBuffer = input.JumpPressed ? 9 : Math.Max(0, _crossBuffer - 1);
        _crossPressAge = input.JumpPressed ? 0 : Math.Min(1000, _crossPressAge + 1);
        if (input.JumpPressed) _crossPressSerial++;
        _crouchPressAge = input.CrouchHeld && !_extendedCrouchWasHeld ? 0 : Math.Min(1000, _crouchPressAge + 1);
        _extendedCrouchWasHeld = input.CrouchHeld;
        int direction = ExtendedInputDirection(context);
        _directionBuffer = AnalogueInput.IsActive &&
            (!_previousExtendedInputActive || direction != _previousInputDirection)
            ? 12 : Math.Max(0, _directionBuffer - 1);
        _previousInputDirection = direction;
        _previousExtendedInputActive = AnalogueInput.IsActive;
        float yaw = AnalogueInput.IsActive
            ? (float)Rac1RatchetYawController.BuildMovementTarget(AnalogueInput.X, AnalogueInput.Y, context.ControlYaw)
            : (float)context.FacingYaw;
        _historicalInputYaw = _inputYawHistory.Count == 10 ? _inputYawHistory.Dequeue() : (float)context.FacingYaw;
        _inputYawHistory.Enqueue(yaw);
    }

    private void ResetExtendedMovement()
    {
        _specialAir = null;
        _airBoost = null;
        _powerSlam = null;
        _heliLong = null;
        _landing = null;
        _ordinaryAirAnimation = null;
        _postLandingGroundAction = null;
        GroundAnimationEntry = null;
        _boostAvailable = false;
        _flipPlanar = null;
        _inputYawHistory.Clear();
        _specialAge = 0;
        _specialLeftSupport = false;
        _gliding = false;
        _ordinaryAirAge = 0;
        _crouchAge = 0;
        _crossBuffer = 0;
        _crossPressAge = 1000;
        _crouchPressAge = 1000;
        _extendedCrouchWasHeld = false;
        _crossPressSerial = 0;
        _launchPressSerial = 0;
        _directionBuffer = 0;
        _previousInputDirection = 2;
        _previousExtendedInputActive = false;
        _wallLease = 0;
        _hasWallJumped = false;
        _previousWallNormalYaw = 0;
        TraversalAnimation = null;
    }
}

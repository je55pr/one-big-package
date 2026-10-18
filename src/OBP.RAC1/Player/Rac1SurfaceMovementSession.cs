namespace OBP.RAC1.Player;

public readonly record struct Rac1SurfaceMovementInput(
    Rac1SurfaceInteractionKind Interaction,
    bool IsGrounded,
    Rac1NativeVector3 Position,
    double SurfaceHeight,
    Rac1NativeVector3 SurfaceNormal,
    double DesiredYaw,
    double InputMagnitude,
    bool CrouchHeld = false,
    int? JumpPressAgeTicks = null,
    double? TerrainPitch = null,
    double? PreviousTerrainPitch = null);

public readonly record struct Rac1SurfaceMovementResult(
    Rac1NativeVector3 Motion,
    int NativeAction,
    int Sequence,
    int BlendTicks,
    int SelectionGeneration,
    int? RequestedJumpAction,
    bool RequestsEnvironmentalDeath,
    double EnvironmentDepth);

/// <summary>
/// Bounded static-surface motion for native ice action 0x2f, mud action 0x68,
/// and the existing class-zero/action-0x72 transition. Free swimming, Magneboot
/// orientation and moving-surface impulses have separate native controllers.
/// </summary>
public sealed class Rac1SurfaceMovementSession
{
    public const double SecondsPerUpdate = 1f / 60f;
    public const double SquaredUpdateScale = 1f / 3600f;
    public const double IceInputSpeed = 5.9f * (1f / 60f);
    public const double IceInputAcceleration = 2.45f * (1f / 3600f);
    public const double IceOppositionAcceleration = 2f * (1f / 3600f);
    public const double IceOverspeedDeceleration = 13f * (1f / 3600f);
    public const double IceSlopeThreshold = 0.0872664600610733f;
    public const double IceSlopeReference = 0.7853981852531433f;
    public const double IceSlopeSpeed = 8f * (1f / 60f);
    public const double IceSlopeAcceleration = 8.5f * (1f / 3600f);
    public const double IceSupportedDownwardRequest = 150f * (1f / 3600f);
    public const double IceCrouchBrake = 1.7f * (1f / 3600f);
    public const double OrdinaryCarryBrake = 6.48f * (1f / 3600f);

    private Rac1SurfaceInteractionKind _interaction;
    private Rac1NativeVector3 _planarContact;
    private Rac1NativeVector3 _controlledContact;
    private Rac1NativeVector3 _previousRequest;
    private Rac1NativeVector3 _previousFinalMotion;
    private Rac1NativeVector3 _crouchCarry;
    private bool _crouching;
    private int _sequence = -1;
    private int _generation;
    private int _age;
    private int _mudEscapeCount;

    public bool Active => _interaction != Rac1SurfaceInteractionKind.None;
    public int ActionAge => _age;
    public int MudEscapeCount => _mudEscapeCount;

    /// <summary>
    /// The seed must describe source contact-controlled motion, not external
    /// platform/conveyor translation. The application owns this boundary.
    /// </summary>
    public void Reset(Rac1NativeVector3 initialControlledMotion = default, int mudEscapeCount = 0)
    {
        if (!double.IsFinite(initialControlledMotion.Length) || mudEscapeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(initialControlledMotion));
        _interaction = Rac1SurfaceInteractionKind.None;
        _controlledContact = initialControlledMotion;
        _planarContact = initialControlledMotion with { Z = 0d };
        _previousRequest = initialControlledMotion;
        _previousFinalMotion = initialControlledMotion;
        _crouchCarry = default;
        _crouching = false;
        _age = 0;
        _mudEscapeCount = mudEscapeCount;
        _sequence = -1;
    }

    public Rac1SurfaceMovementResult Step(in Rac1SurfaceMovementInput input)
    {
        Validate(input);
        if (_interaction != input.Interaction)
        {
            _interaction = input.Interaction;
            _age = 0;
            _sequence = -1;
            _crouching = false;
        }
        _age++;
        return input.Interaction switch
        {
            Rac1SurfaceInteractionKind.IceSlide => StepIce(input),
            Rac1SurfaceInteractionKind.MudSink => StepMud(input),
            Rac1SurfaceInteractionKind.ShallowWaterWade => StepWade(input),
            _ => throw new NotSupportedException("This interaction has no recovered surface-motion controller."),
        };
    }

    /// <summary>
    /// Contact tail 0x213ae0..0x213c24 separately constructs P+0x110 in 3D
    /// and P+0x130 after zeroing Z. Pass only the primary contact delta.
    /// </summary>
    public void RecordPrimaryContact(Rac1NativeVector3 request, Rac1NativeVector3 actualMotion)
    {
        _controlledContact = Rac1ContactMotionProjection.Resolve(request, actualMotion);
        _planarContact = Rac1ContactMotionProjection.Resolve(
            request with { Z = 0d }, actualMotion with { Z = 0d });
        _previousRequest = request;
        _previousFinalMotion = actualMotion;
    }

    public void RecordMudEscape() => _mudEscapeCount++;

    private Rac1SurfaceMovementResult StepIce(in Rac1SurfaceMovementInput input)
    {
        int? jumpAction = input.JumpPressAgeTicks is >= 0 and < 9 && input.IsGrounded ? 7 : null;
        if (input.CrouchHeld)
        {
            if (!_crouching)
                _crouchCarry = Rac1ContactMotionProjection.EnterCrouch(_controlledContact) with { Z = 0d };
            _crouching = true;
            _crouchCarry = DecayCarry(_crouchCarry, IceCrouchBrake);
            var crouchMotion = _crouchCarry with
            {
                Z = input.IsGrounded
                    ? -Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick
                    : _previousFinalMotion.Z - 25f * SquaredUpdateScale,
            };
            return Result(crouchMotion, 4, 13, 9, jumpAction, false, 0d);
        }
        _crouching = false;

        var target = new Rac1NativeVector3(
            Math.Cos(input.DesiredYaw) * input.InputMagnitude * IceInputSpeed,
            Math.Sin(input.DesiredYaw) * input.InputMagnitude * IceInputSpeed,
            0d);
        double previousLength = Math.Sqrt(_previousRequest.X * _previousRequest.X + _previousRequest.Y * _previousRequest.Y);
        double targetLength = target.Length;
        double opposition = previousLength > 0d && targetLength > 0d
            ? Math.Clamp(-(_previousRequest.X * target.X + _previousRequest.Y * target.Y) / (previousLength * targetLength), 0d, 1d)
            : 0d;
        double acceleration = IceInputAcceleration * input.InputMagnitude + IceOppositionAcceleration * opposition;
        // P+0x160 is a stored single at 0x213c38. Keep that source cell's
        // rounding boundary: double reconstruction of an exactly-at-target
        // projected vector can otherwise spuriously select overspeed braking.
        if ((float)_controlledContact.Length > (float)IceInputSpeed)
            acceleration = IceOverspeedDeceleration;

        var planar = Approach(_planarContact, target, acceleration);
        double slope = Math.Atan2(
            Math.Sqrt(input.SurfaceNormal.X * input.SurfaceNormal.X + input.SurfaceNormal.Y * input.SurfaceNormal.Y),
            input.SurfaceNormal.Z);
        if (slope > IceSlopeThreshold)
        {
            double downhillYaw = Math.Atan2(input.SurfaceNormal.Y, input.SurfaceNormal.X);
            double slopeScale = slope / IceSlopeReference;
            var downhill = new Rac1NativeVector3(
                Math.Cos(downhillYaw) * IceSlopeSpeed * slopeScale,
                Math.Sin(downhillYaw) * IceSlopeSpeed * slopeScale,
                0d);
            planar = Approach(planar, downhill, IceSlopeAcceleration * slopeScale);
        }
        if (input.PreviousTerrainPitch is double oldPitch && input.TerrainPitch is double pitch &&
            oldPitch < 0.01745329238474369f && pitch > IceSlopeThreshold)
        {
            double cosine = Math.Cos(pitch);
            planar = new(planar.X * cosine, planar.Y * cosine, 0d);
        }

        var motion = planar with
        {
            Z = input.IsGrounded
                ? -IceSupportedDownwardRequest
                : _previousFinalMotion.Z - 25f * SquaredUpdateScale,
        };
        // Source entry selects55; the neutral109/standing variants depend on
        // the native animation transition state and remain presentation input.
        return Result(motion, 0x2f, 55, 8, jumpAction, false, 0d);
    }

    private Rac1SurfaceMovementResult StepMud(in Rac1SurfaceMovementInput input)
    {
        double depth = Math.Max(0d, input.SurfaceHeight - input.Position.Z);
        double vertical = -SecondsPerUpdate;
        if (depth > (_mudEscapeCount + 1d) * .5f)
            vertical = -.25f * SecondsPerUpdate;
        int? jump = _mudEscapeCount < 2 && _age > 15 + 25 * _mudEscapeCount &&
            input.JumpPressAgeTicks is >= 0 and < 7 ? 0x69 : null;
        return Result(new(0d, 0d, vertical), 0x68, 113, 8, jump,
            depth > 1.2f || _age > 170, depth);
    }

    private Rac1SurfaceMovementResult StepWade(in Rac1SurfaceMovementInput input)
    {
        // Entry0x227344/domain9 has no freely controlled walking law. The
        // shared idle/stop path consumes existing P+0x150 and admits no input
        // acceleration. This bounded state is not free-swim reconstruction.
        if (_age == 1)
            _crouchCarry = _controlledContact with { Z = 0d };
        _crouchCarry = DecayCarry(_crouchCarry, OrdinaryCarryBrake);
        return Result(_crouchCarry with
        {
            Z = input.IsGrounded
                ? -Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick
                : _previousFinalMotion.Z - 25f * SquaredUpdateScale,
        }, 0x72, 0, 10, null, false,
            Math.Max(0d, input.SurfaceHeight - input.Position.Z));
    }

    private Rac1SurfaceMovementResult Result(
        Rac1NativeVector3 motion, int action, int sequence, int blend,
        int? jumpAction, bool death, double depth)
    {
        if (_sequence != sequence)
        {
            _sequence = sequence;
            _generation++;
        }
        // Unobstructed propagation until a primary-contact observation replaces
        // it. An application that executes collision must call RecordPrimaryContact.
        _previousRequest = motion;
        _previousFinalMotion = motion;
        _controlledContact = motion with { Z = 0d };
        _planarContact = motion with { Z = 0d };
        return new(motion, action, sequence, blend, _generation, jumpAction, death, depth);
    }

    private static Rac1NativeVector3 Approach(Rac1NativeVector3 current, Rac1NativeVector3 target, double limit)
    {
        var delta = new Rac1NativeVector3(target.X - current.X, target.Y - current.Y, 0d);
        double length = delta.Length;
        if (length <= limit)
            return target with { Z = 0d };
        double scale = length > 0d ? limit / length : 0d;
        return new(current.X + delta.X * scale, current.Y + delta.Y * scale, 0d);
    }

    private static Rac1NativeVector3 DecayCarry(Rac1NativeVector3 current, double brake)
    {
        double length = Math.Sqrt(current.X * current.X + current.Y * current.Y);
        double remaining = Math.Max(0d, length - brake);
        if (remaining < .001f || length == 0d)
            return default;
        double scale = remaining / length;
        return new(current.X * scale, current.Y * scale, 0d);
    }

    private static void Validate(in Rac1SurfaceMovementInput input)
    {
        if (!double.IsFinite(input.Position.Length) || !double.IsFinite(input.SurfaceHeight) ||
            !double.IsFinite(input.SurfaceNormal.Length) || input.SurfaceNormal.Length == 0d ||
            !double.IsFinite(input.DesiredYaw) || !double.IsFinite(input.InputMagnitude) ||
            input.InputMagnitude is < 0d or > 1.01d || input.JumpPressAgeTicks < 0 ||
            input.TerrainPitch is double pitch && !double.IsFinite(pitch) ||
            input.PreviousTerrainPitch is double previousPitch && !double.IsFinite(previousPitch))
            throw new ArgumentOutOfRangeException(nameof(input));
    }
}

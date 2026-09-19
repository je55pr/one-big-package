using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public enum Rac1RatchetMovementPhase
{
    Grounded,
    JumpAnticipation,
    Rising,
    Falling,
}

/// <summary>
/// Presentation-facing locomotion state derived from the recovered controller.
/// CrouchTurning is the retail zero-translation directional R1 state; it does
/// not imply a recovered moving-strafe law.
/// </summary>
public enum Rac1RatchetLocomotionState
{
    Idle,
    Moving,
    Crouched,
    CrouchTurning,
    JumpAnticipation,
    Rising,
    Falling,
}

/// <summary>
/// Bounded retail-backed R&amp;C1 Ratchet controller core.
/// Values are native-world displacement per NTSC update, not engine velocity.
/// Collision response remains owned by the host.
/// </summary>
/// <remarks>
/// Constants and recurrences are frozen from
/// research/generated/rac1-ratchet-movement-controller.json (SCUS-97199).
/// The recovered raw-axis conditioner and walk/run magnitude witnesses are
/// applied before native acceleration. The locomotion-state yaw recurrence is
/// recovered separately in <see cref="Rac1RatchetYawController"/>.
/// </remarks>
public sealed class Rac1RatchetMovementController
{
    public const double UpdateHz = 60d;
    public const double MaximumPlanarStep = 0.09500919d;
    // Retained live walk plateau. No exact retail literal has yet been identified.
    public const double WalkPlanarStep = 0.015d;
    public const double GroundAccelerationPerTick = 1d / 480d;
    public const double GroundDecelerationPerTick = 1d / 300d;
    public const double AirAccelerationPerTick = 1d / 180d;
    public const double AirDecelerationPerTick = 1d / 1200d;
    public const double CrouchDecelerationPerTick = 0.001802944d;

    // Fixed yaw witnesses remain in startup sequence 3 at 0.037499697 and
    // enter run sequence 4 at 0.039582664 native unit/tick.
    public const double GroundRunYawMinimumPlanarStep = 0.039582664d;

    public const int JumpAnticipationTicks = 8;

    // Controlled Veldin taps establish this launch table. The first two held
    // ticks share the minimum launch; seven or more reach the observed cap.
    private static readonly double[] LaunchStepByHeldTicks =
    [
        0d,
        0.13816452026367188d,
        0.13816452026367188d,
        0.14207458496093750d,
        0.14589500427246094d,
        0.14962959289550781d,
        0.15328598022460938d,
        0.15686607360839844d,
    ];

    // While Cross remains held after launch, retail uses a softer rising
    // recurrence for nine observed updates, then returns to the normal rise
    // decrement even if the button remains held.
    private static readonly double[] HeldRiseDecrements =
    [
        0.004741668701171875d,
        0.004806518554687500d,
        0.0048694610595703125d,
        0.004928588867187500d,
        0.0049839019775390625d,
        0.005039215087890625d,
        0.0050907135009765625d,
        0.005138397216796875d,
        0.0051860809326171875d,
    ];

    public const double ReleasedRiseGravityPerTick = 0.00825d;
    public const double FallGravityPerTick = 0.009652d;

    private double _planarX;
    private double _planarY;
    private double _verticalStep;
    private int _anticipationTicks;
    private int _jumpHeldTicks;
    private int _heldRiseTicks;

    public Rac1RatchetMovementPhase Phase { get; private set; } = Rac1RatchetMovementPhase.Grounded;
    public Rac1RatchetLocomotionState LocomotionState { get; private set; } = Rac1RatchetLocomotionState.Idle;
    public Rac1RatchetYawMode YawMode { get; private set; } = Rac1RatchetYawMode.GroundStartup;
    public double PlanarX => _planarX;
    public double PlanarY => _planarY;
    public double VerticalStep => _verticalStep;

    public readonly record struct StepResult(
        double PlanarX,
        double PlanarY,
        double Vertical,
        Rac1RatchetMovementPhase Phase,
        Rac1RatchetLocomotionState LocomotionState,
        Rac1RatchetYawMode YawMode)
    {
        public double PlanarMagnitude =>
            System.Math.Sqrt((PlanarX * PlanarX) + (PlanarY * PlanarY));
    }

    public StepResult Step(PlayerControlIntent input, PlayerContactFacts contact)
    {
        var analogue = Rac1AnalogueInput.ConditionUnitAxes(input.PlanarX, input.PlanarY);
        UpdatePlanar(input, analogue, contact.IsGrounded);
        UpdateVertical(input, contact);
        UpdateLocomotionState(input, analogue, contact);
        UpdateYawMode(input, analogue, contact);
        return new StepResult(_planarX, _planarY, _verticalStep, Phase, LocomotionState, YawMode);
    }

    public void Reset()
    {
        _planarX = 0d;
        _planarY = 0d;
        _verticalStep = 0d;
        _anticipationTicks = 0;
        _jumpHeldTicks = 0;
        _heldRiseTicks = 0;
        Phase = Rac1RatchetMovementPhase.Grounded;
        LocomotionState = Rac1RatchetLocomotionState.Idle;
        YawMode = Rac1RatchetYawMode.GroundStartup;
    }

    private void UpdatePlanar(
        PlayerControlIntent input,
        Rac1AnalogueInput.Conditioned analogue,
        bool grounded)
    {
        bool crouching = grounded && input.CrouchHeld;
        bool hasIntent = !crouching && analogue.IsActive;
        var desired = analogue.Direction;
        var output = input.EffectivePlanarBasis.Transform(desired.X, desired.Y);
        double outputLength = System.Math.Sqrt((output.X * output.X) + (output.Y * output.Y));
        if (hasIntent && outputLength <= 1e-12d)
            throw new ArgumentException("Active planar input requires a non-degenerate control basis.", nameof(input));

        double targetStep = grounded && analogue.SpeedBand == Rac1AnalogueSpeedBand.Walk
            ? WalkPlanarStep
            : MaximumPlanarStep;
        double targetX = hasIntent ? (output.X / outputLength) * targetStep : 0d;
        double targetY = hasIntent ? (output.Y / outputLength) * targetStep : 0d;
        double amount = crouching
            ? CrouchDecelerationPerTick
            : hasIntent
                ? grounded ? GroundAccelerationPerTick : AirAccelerationPerTick
                : grounded ? GroundDecelerationPerTick : AirDecelerationPerTick;

        MoveToward(ref _planarX, ref _planarY, targetX, targetY, amount);
    }

    private static void MoveToward(
        ref double x,
        ref double y,
        double targetX,
        double targetY,
        double amount)
    {
        double dx = targetX - x;
        double dy = targetY - y;
        double distance = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= amount || distance <= 1e-12)
        {
            x = targetX;
            y = targetY;
            return;
        }

        double scale = amount / distance;
        x += dx * scale;
        y += dy * scale;
    }

    private void UpdateLocomotionState(
        PlayerControlIntent input,
        Rac1AnalogueInput.Conditioned analogue,
        PlayerContactFacts contact)
    {
        LocomotionState = Phase switch
        {
            Rac1RatchetMovementPhase.JumpAnticipation => Rac1RatchetLocomotionState.JumpAnticipation,
            Rac1RatchetMovementPhase.Rising => Rac1RatchetLocomotionState.Rising,
            Rac1RatchetMovementPhase.Falling => Rac1RatchetLocomotionState.Falling,
            _ when contact.IsGrounded && input.CrouchHeld =>
                analogue.IsActive
                    ? Rac1RatchetLocomotionState.CrouchTurning
                    : Rac1RatchetLocomotionState.Crouched,
            _ when System.Math.Abs(_planarX) > 1e-12 || System.Math.Abs(_planarY) > 1e-12 =>
                Rac1RatchetLocomotionState.Moving,
            _ => Rac1RatchetLocomotionState.Idle,
        };
    }

    private void UpdateYawMode(
        PlayerControlIntent input,
        Rac1AnalogueInput.Conditioned analogue,
        PlayerContactFacts contact)
    {
        bool airborne = !contact.IsGrounded ||
            Phase is Rac1RatchetMovementPhase.JumpAnticipation or
                Rac1RatchetMovementPhase.Rising or
                Rac1RatchetMovementPhase.Falling;

        if (airborne)
        {
            YawMode = Rac1RatchetYawMode.Air;
            return;
        }

        if (input.CrouchHeld && analogue.IsActive)
        {
            YawMode = Rac1RatchetYawMode.CrouchTurn;
            return;
        }

        double planarMagnitude = System.Math.Sqrt((_planarX * _planarX) + (_planarY * _planarY));
        YawMode = planarMagnitude >= GroundRunYawMinimumPlanarStep
            ? Rac1RatchetYawMode.GroundRun
            : Rac1RatchetYawMode.GroundStartup;
    }

    private void UpdateVertical(PlayerControlIntent input, PlayerContactFacts contact)
    {
        if (contact.IsGrounded && Phase is Rac1RatchetMovementPhase.Rising or Rac1RatchetMovementPhase.Falling)
        {
            _verticalStep = 0d;
            _heldRiseTicks = 0;
            Phase = Rac1RatchetMovementPhase.Grounded;
        }

        if (Phase == Rac1RatchetMovementPhase.Grounded)
        {
            _verticalStep = 0d;
            if (!contact.IsGrounded)
            {
                _verticalStep = -FallGravityPerTick;
                Phase = Rac1RatchetMovementPhase.Falling;
                return;
            }

            if (input.JumpPressed && input.JumpHeld && !input.CrouchHeld)
            {
                Phase = Rac1RatchetMovementPhase.JumpAnticipation;
                _anticipationTicks = 0;
                _jumpHeldTicks = 0;
            }
        }

        if (Phase == Rac1RatchetMovementPhase.JumpAnticipation)
        {
            StepJumpAnticipation(input, contact);
            return;
        }

        if (Phase == Rac1RatchetMovementPhase.Rising)
        {
            if (contact.HitCeiling)
            {
                _verticalStep = 0d;
                Phase = Rac1RatchetMovementPhase.Falling;
                return;
            }

            if (input.JumpHeld && _heldRiseTicks < HeldRiseDecrements.Length)
            {
                _verticalStep -= HeldRiseDecrements[_heldRiseTicks++];
            }
            else
            {
                _verticalStep -= ReleasedRiseGravityPerTick;
            }

            if (_verticalStep <= 0d)
                Phase = Rac1RatchetMovementPhase.Falling;
            return;
        }

        if (Phase == Rac1RatchetMovementPhase.Falling)
            _verticalStep -= FallGravityPerTick;
    }

    private void StepJumpAnticipation(PlayerControlIntent input, PlayerContactFacts contact)
    {
        if (!contact.IsGrounded)
        {
            _verticalStep = 0d;
            Phase = Rac1RatchetMovementPhase.Falling;
            return;
        }

        if (input.JumpHeld)
            _jumpHeldTicks++;
        _anticipationTicks++;
        if (_anticipationTicks < JumpAnticipationTicks)
            return;

        int held = System.Math.Clamp(_jumpHeldTicks, 1, LaunchStepByHeldTicks.Length - 1);
        _verticalStep = LaunchStepByHeldTicks[held];
        _heldRiseTicks = 0;
        Phase = Rac1RatchetMovementPhase.Rising;
    }
}

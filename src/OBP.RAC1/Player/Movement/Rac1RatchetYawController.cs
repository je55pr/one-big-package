namespace OBP.RAC1.Player;

/// <summary>
/// Deterministic R&amp;C1 movement-facing controller recovered from fixed
/// NTSC-U retail traces. Retail witnesses pin both the control-heading/stick
/// target construction and the locomotion-state yaw recurrence. The host still
/// supplies the presentation camera/control heading itself.
/// </summary>
public sealed class Rac1RatchetYawController
{
    public const double GroundStartupErrorGain = 0.019d;
    public const double GroundStartupVelocityDamping = 0.10d;
    public const double GroundRunErrorGain = 0.00800000038d;
    public const double GroundRunVelocityDamping = 0.150000006d;
    public const double CrouchTurnErrorGain = 0.002d;
    public const double CrouchTurnVelocityDamping = 0.07d;
    public const double GroundMaximumStep = 0.165806278d;
    public const double AirErrorGain = 0.04d;
    public const double AirVelocityDamping = 0.20d;
    public const double AirMaximumStep = 0.250163853d;

    public double ControlYaw { get; private set; }
    public double TargetYaw { get; private set; }
    public double CurrentYaw { get; private set; }
    public double YawVelocity { get; private set; }
    public Rac1RatchetYawMode Mode { get; private set; } = Rac1RatchetYawMode.GroundStartup;

    public readonly record struct StepResult(
        double ControlYaw,
        double TargetYaw,
        double CurrentYaw,
        double YawVelocity,
        Rac1RatchetYawMode Mode);

    public readonly record struct RecurrenceStep(
        double CurrentYaw,
        double YawVelocity);

    public Rac1RatchetYawController(double initialYaw = 0d)
    {
        Reset(initialYaw);
    }

    public StepResult Step(
        double inputX,
        double inputY,
        double controlYaw,
        Rac1RatchetYawMode mode)
    {
        if (!double.IsFinite(inputX)) throw new ArgumentOutOfRangeException(nameof(inputX));
        if (!double.IsFinite(inputY)) throw new ArgumentOutOfRangeException(nameof(inputY));
        if (!double.IsFinite(controlYaw)) throw new ArgumentOutOfRangeException(nameof(controlYaw));

        ControlYaw = WrapPi(controlYaw);
        var analogue = Rac1AnalogueInput.ConditionUnitAxes(inputX, inputY);
        TargetYaw = analogue.IsActive
            ? BuildMovementTarget(analogue.X, analogue.Y, ControlYaw)
            : CurrentYaw;
        Mode = mode;

        var next = AdvanceRecurrence(CurrentYaw, YawVelocity, TargetYaw, mode);
        CurrentYaw = next.CurrentYaw;
        YawVelocity = next.YawVelocity;
        return Snapshot();
    }

    public void Reset(double currentYaw = 0d)
    {
        if (!double.IsFinite(currentYaw)) throw new ArgumentOutOfRangeException(nameof(currentYaw));

        CurrentYaw = WrapPi(currentYaw);
        ControlYaw = CurrentYaw;
        TargetYaw = CurrentYaw;
        YawVelocity = 0d;
        Mode = Rac1RatchetYawMode.GroundStartup;
    }

    /// <summary>
    /// Advance one recovered retail yaw update from explicit native state.
    /// This pure recurrence is exposed so payload-free fixed-trace witnesses can
    /// be replayed without depending on host input or camera construction.
    /// </summary>
    public static RecurrenceStep AdvanceRecurrence(
        double currentYaw,
        double yawVelocity,
        double targetYaw,
        Rac1RatchetYawMode mode)
    {
        if (!double.IsFinite(currentYaw)) throw new ArgumentOutOfRangeException(nameof(currentYaw));
        if (!double.IsFinite(yawVelocity)) throw new ArgumentOutOfRangeException(nameof(yawVelocity));
        if (!double.IsFinite(targetYaw)) throw new ArgumentOutOfRangeException(nameof(targetYaw));

        var (gain, damping, maximumStep) = Coefficients(mode);
        currentYaw = WrapPi(currentYaw);
        targetYaw = WrapPi(targetYaw);

        double error = WrapPi(targetYaw - currentYaw);
        double velocity = yawVelocity + (gain * error) - (damping * yawVelocity);
        velocity = Math.Clamp(velocity, -maximumStep, maximumStep);

        // Fixed retail traces prove hard overshoot protection and that turn
        // velocity is cleared when the target is reached. No separate 1%-of-cap
        // early-snap threshold has a retail witness.
        if (error == 0d ||
            (Math.Sign(velocity) == Math.Sign(error) && Math.Abs(velocity) >= Math.Abs(error)))
        {
            return new RecurrenceStep(targetYaw, 0d);
        }

        return new RecurrenceStep(WrapPi(currentYaw + velocity), velocity);
    }

    /// <summary>
    /// Build the recovered control-heading target from an already conditioned,
    /// active right/forward stick vector.
    /// </summary>
    public static double BuildMovementTarget(double inputX, double inputY, double controlYaw)
    {
        if (!double.IsFinite(inputX)) throw new ArgumentOutOfRangeException(nameof(inputX));
        if (!double.IsFinite(inputY)) throw new ArgumentOutOfRangeException(nameof(inputY));
        if (!double.IsFinite(controlYaw)) throw new ArgumentOutOfRangeException(nameof(controlYaw));
        if ((inputX * inputX) + (inputY * inputY) <= 1e-12d)
            throw new ArgumentException("Movement target requires non-zero planar input.");

        // Retail DS2 convention: forward is stick angle 0, right is +pi/2,
        // and G+0x100 subtracts that planar angle from the control heading.
        double stickAngle = Math.Atan2(inputX, inputY);
        return WrapPi(controlYaw - stickAngle);
    }

    public static double WrapPi(double angle)
    {
        if (!double.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        double wrapped = (angle + Math.PI) % Math.Tau;
        if (wrapped < 0d) wrapped += Math.Tau;
        return wrapped - Math.PI;
    }

    private static (double Gain, double Damping, double MaximumStep) Coefficients(
        Rac1RatchetYawMode mode) =>
        mode switch
        {
            Rac1RatchetYawMode.GroundStartup =>
                (GroundStartupErrorGain, GroundStartupVelocityDamping, GroundMaximumStep),
            Rac1RatchetYawMode.GroundRun =>
                (GroundRunErrorGain, GroundRunVelocityDamping, GroundMaximumStep),
            Rac1RatchetYawMode.CrouchTurn =>
                (CrouchTurnErrorGain, CrouchTurnVelocityDamping, GroundMaximumStep),
            Rac1RatchetYawMode.Air =>
                (AirErrorGain, AirVelocityDamping, AirMaximumStep),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    private StepResult Snapshot() =>
        new(ControlYaw, TargetYaw, CurrentYaw, YawVelocity, Mode);
}

public enum Rac1RatchetYawMode
{
    GroundStartup,
    GroundRun,
    CrouchTurn,
    Air,
}

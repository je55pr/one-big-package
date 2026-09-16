namespace OBP.RAC1.Player;

/// <summary>
/// Retail-backed R&amp;C1 ordinary movement-facing recurrence.
/// Camera/control yaw, movement target yaw, live player yaw, and yaw velocity
/// remain distinct so presentation input cannot directly rewrite combat facing.
/// </summary>
public sealed class Rac1RatchetYawController
{
    public const double GroundErrorGain = 0.00800000038d;
    public const double GroundVelocityDamping = 0.150000006d;
    public const double GroundMaximumStep = 0.165806278d;
    public const double AirErrorGain = 0.04d;
    public const double AirVelocityDamping = 0.20d;
    public const double AirMaximumStep = 0.250163853d;
    public const double SnapStepFraction = 0.01d;

    public double ControlYaw { get; private set; }
    public double TargetYaw { get; private set; }
    public double CurrentYaw { get; private set; }
    public double YawVelocity { get; private set; }

    public readonly record struct StepResult(
        double ControlYaw,
        double TargetYaw,
        double CurrentYaw,
        double YawVelocity);

    public Rac1RatchetYawController(double initialYaw = 0d)
    {
        Reset(initialYaw);
    }

    public StepResult Step(double inputX, double inputY, double controlYaw, bool grounded)
    {
        if (!double.IsFinite(inputX)) throw new ArgumentOutOfRangeException(nameof(inputX));
        if (!double.IsFinite(inputY)) throw new ArgumentOutOfRangeException(nameof(inputY));
        if (!double.IsFinite(controlYaw)) throw new ArgumentOutOfRangeException(nameof(controlYaw));

        ControlYaw = WrapPi(controlYaw);
        TargetYaw = (inputX * inputX) + (inputY * inputY) > 1e-12d
            ? BuildMovementTarget(inputX, inputY, ControlYaw)
            : CurrentYaw;

        double gain = grounded ? GroundErrorGain : AirErrorGain;
        double damping = grounded ? GroundVelocityDamping : AirVelocityDamping;
        double maximumStep = grounded ? GroundMaximumStep : AirMaximumStep;
        Advance(gain, damping, maximumStep);
        return Snapshot();
    }

    public void Reset(double currentYaw = 0d)
    {
        if (!double.IsFinite(currentYaw)) throw new ArgumentOutOfRangeException(nameof(currentYaw));

        CurrentYaw = WrapPi(currentYaw);
        ControlYaw = CurrentYaw;
        TargetYaw = CurrentYaw;
        YawVelocity = 0d;
    }

    public static double BuildMovementTarget(double inputX, double inputY, double controlYaw)
    {
        if (!double.IsFinite(inputX)) throw new ArgumentOutOfRangeException(nameof(inputX));
        if (!double.IsFinite(inputY)) throw new ArgumentOutOfRangeException(nameof(inputY));
        if (!double.IsFinite(controlYaw)) throw new ArgumentOutOfRangeException(nameof(controlYaw));
        if ((inputX * inputX) + (inputY * inputY) <= 1e-12d)
            throw new ArgumentException("Movement target requires non-zero planar input.");

        return WrapPi(Math.Atan2(-inputY, -inputX) + controlYaw);
    }

    public static double WrapPi(double angle)
    {
        if (!double.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        double wrapped = (angle + Math.PI) % Math.Tau;
        if (wrapped < 0d) wrapped += Math.Tau;
        return wrapped - Math.PI;
    }

    private void Advance(double gain, double damping, double maximumStep)
    {
        double error = WrapPi(TargetYaw - CurrentYaw);
        double velocity = YawVelocity + (gain * error) - (damping * YawVelocity);
        velocity = Math.Clamp(velocity, -maximumStep, maximumStep);

        if (error == 0d ||
            (Math.Sign(velocity) == Math.Sign(error) && Math.Abs(velocity) >= Math.Abs(error)))
        {
            velocity = error;
        }

        double nextYaw = WrapPi(CurrentYaw + velocity);
        double remaining = WrapPi(TargetYaw - nextYaw);
        if (Math.Abs(remaining) < SnapStepFraction * maximumStep)
        {
            nextYaw = TargetYaw;
            velocity = 0d;
        }

        CurrentYaw = nextYaw;
        YawVelocity = velocity;
    }

    private StepResult Snapshot() =>
        new(ControlYaw, TargetYaw, CurrentYaw, YawVelocity);
}

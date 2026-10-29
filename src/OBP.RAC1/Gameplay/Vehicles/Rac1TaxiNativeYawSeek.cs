namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native yaw_seek (0x0025be00, common R&amp;C1 engine), used by
/// Kerwan taxi path steering at L3:0x002d3b4c. The original call
/// uses an acceleration/deceleration of 45 degrees times frame
/// delta, with a 2*pi times frame-delta spin cap. The accumulated
/// angular speed is persistent PVar+0x68 in the taxi path helper.
/// </summary>
public sealed class Rac1TaxiNativeYawSeek
{
    public const int NativeFramesPerSecond = 60;
    public const double AccelerationDegreesPerSecond = 45d;
    public const double MaxSpinDegreesPerSecond = 360d;

    private const double Frame = 1d / NativeFramesPerSecond;
    private const double Acceleration =
        Math.PI / 4d / NativeFramesPerSecond;
    private const double MaxSpin =
        2d * Math.PI / NativeFramesPerSecond;

    private double _remainder;

    public Rac1TaxiNativeYawSeek(double initialHeadingRadians)
    {
        if (!double.IsFinite(initialHeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(initialHeadingRadians));
        HeadingRadians = Normalize(initialHeadingRadians);
    }

    public double HeadingRadians { get; private set; }
    public double SpinRadiansPerFrame { get; private set; }

    public void Advance(double targetRadians, double elapsedSeconds)
    {
        if (!double.IsFinite(targetRadians))
            throw new ArgumentOutOfRangeException(nameof(targetRadians));
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        _remainder += elapsedSeconds;
        while (_remainder + 1e-10 >= Frame)
        {
            _remainder = Math.Max(0, _remainder - Frame);
            Tick(targetRadians);
        }
    }

    private void Tick(double targetRadians)
    {
        double target = Normalize(targetRadians);
        double error = Normalize(target - HeadingRadians);
        double velocity = SpinRadiansPerFrame;

        // yaw_seek first brakes angular velocity that points the wrong
        // way (or if already aimed); it may continue moving while
        // braking, just as the native recurrence does.
        if (velocity * error < 0 || error == 0)
        {
            velocity = Approach(velocity, 0, Acceleration);
            SpinRadiansPerFrame = velocity;
            HeadingRadians = Normalize(HeadingRadians + velocity);
            return;
        }

        double stoppingDistance = velocity * velocity / (2d * Acceleration);
        if (Math.Abs(error) < stoppingDistance)
        {
            double run = Math.Abs(error) + Math.Abs(velocity);
            double deceleration = stoppingDistance < run
                ? Acceleration : Acceleration * 1.1d;
            velocity = Approach(velocity, 0, deceleration);
        }
        else
        {
            // The translated helper has sqrt(2*decel*err), while also
            // branching on err<0: negative err requires a magnitude for
            // that square root. Preserve its symmetric steering intent,
            // avoiding NaN for counter-clockwise headings.
            double requested = Math.Min(MaxSpin,
                Math.Sqrt(2d * Acceleration * Math.Abs(error)));
            velocity = Approach(velocity, Math.CopySign(requested, error),
                Acceleration);
        }

        SpinRadiansPerFrame = velocity;
        if (Math.Abs(velocity) < Math.Abs(error))
            HeadingRadians = Normalize(HeadingRadians + velocity);
        else
            HeadingRadians = target;
    }

    private static double Approach(double value, double target, double step) =>
        value + Math.Clamp(target - value, -step, step);

    private static double Normalize(double radians) =>
        Math.IEEERemainder(radians, 2d * Math.PI);
}

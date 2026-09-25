namespace OBP.RAC1.Player;

public enum Rac1AnalogueSpeedBand
{
    Inactive,
    Walk,
    Run,
}

/// <summary>
/// Recovered R&C1 left-stick conditioning, independent of any host input API.
/// Authority is the SCUS-97199 loaded path documented in RAC1_PLAYER_MOVEMENT.md.
/// </summary>
public static class Rac1AnalogueInput
{
    public const int NeutralRawByte = 127;
    public const double HostAxisCountScale = 128d;
    public const double ComponentDeadbandCounts = 48d;
    public const double ComponentScaleCounts = 76d;
    public const double ActivationMagnitude = 0.25d;

    // Godot/SDL host sticks use a usability envelope before entering the
    // recovered controller. This intentionally stays separate from the literal
    // DualShock 2 byte conditioner below: 10% is neutral, 90% is full-scale,
    // and the radial span between them is linear.
    public const double HostInnerDeadZone = 0.10d;
    public const double HostOuterDeadZone = 0.10d;

    // The exact translational walk/run selector is still unresolved. Production
    // uses the first observed cardinal run witness as the deterministic boundary;
    // do not relabel this value as an exact recovered retail comparison.
    public const double FirstObservedRunMagnitude = 63d / ComponentScaleCounts;

    public readonly record struct Conditioned(
        double X,
        double Y,
        double UncappedMagnitude,
        double Magnitude,
        bool IsActive,
        Rac1AnalogueSpeedBand SpeedBand)
    {
        public (double X, double Y) Direction
        {
            get
            {
                if (!IsActive || UncappedMagnitude <= 1e-12d)
                    return (0d, 0d);
                return (X / UncappedMagnitude, Y / UncappedMagnitude);
            }
        }
    }

    /// <summary>
    /// Condition neutral signed axes whose full positive travel is +1. The
    /// 128-count scale mirrors raw byte displacement from centre; saturation
    /// occurs before either physical endpoint, so the low-side one-count
    /// asymmetry cannot affect conditioned full-stick output.
    /// </summary>
    public static Conditioned ConditionUnitAxes(double right, double forward)
    {
        ValidateFinite(right, nameof(right));
        ValidateFinite(forward, nameof(forward));
        return ConditionCenteredCounts(
            Math.Clamp(right, -1d, 1d) * HostAxisCountScale,
            Math.Clamp(forward, -1d, 1d) * HostAxisCountScale);
    }

    /// <summary>
    /// Condition a modern host stick with radial lower/upper dead zones. This is
    /// the playable adapter used by Godot; it is not presented as a recovered
    /// retail DS2 byte law.
    /// </summary>
    public static Conditioned ConditionHostAxes(double right, double forward)
    {
        ValidateFinite(right, nameof(right));
        ValidateFinite(forward, nameof(forward));

        right = Math.Clamp(right, -1d, 1d);
        forward = Math.Clamp(forward, -1d, 1d);
        double sourceMagnitude = Math.Sqrt((right * right) + (forward * forward));
        if (sourceMagnitude <= 1e-12d)
            return default;

        double cappedMagnitude = Math.Min(sourceMagnitude, 1d);
        double activeSpan = 1d - HostInnerDeadZone - HostOuterDeadZone;
        double magnitude = Math.Clamp(
            (cappedMagnitude - HostInnerDeadZone) / activeSpan,
            0d,
            1d);
        if (magnitude <= 1e-12d)
            return default;

        double directionScale = magnitude / sourceMagnitude;
        double x = right * directionScale;
        double y = forward * directionScale;
        var band = magnitude >= FirstObservedRunMagnitude
            ? Rac1AnalogueSpeedBand.Run
            : Rac1AnalogueSpeedBand.Walk;
        return new Conditioned(x, y, magnitude, magnitude, true, band);
    }

    /// <summary>Condition literal DualShock 2 left-stick bytes.</summary>
    public static Conditioned ConditionRawLeftStick(byte rawX, byte rawY) =>
        ConditionCenteredCounts(
            rawX - NeutralRawByte,
            NeutralRawByte - rawY);

    private static Conditioned ConditionCenteredCounts(double rightCounts, double forwardCounts)
    {
        double x = ConditionAxis(rightCounts);
        double y = ConditionAxis(forwardCounts);
        double length = Math.Sqrt((x * x) + (y * y));
        bool active = length >= ActivationMagnitude;
        Rac1AnalogueSpeedBand band = !active
            ? Rac1AnalogueSpeedBand.Inactive
            : length >= FirstObservedRunMagnitude
                ? Rac1AnalogueSpeedBand.Run
                : Rac1AnalogueSpeedBand.Walk;
        return new Conditioned(
            x,
            y,
            length,
            Math.Min(length, 1d),
            active,
            band);
    }

    private static double ConditionAxis(double centeredCounts)
    {
        double magnitude = Math.Max(0d, Math.Abs(centeredCounts) - ComponentDeadbandCounts);
        magnitude = Math.Min(magnitude / ComponentScaleCounts, 1d);
        return Math.CopySign(magnitude, centeredCounts);
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

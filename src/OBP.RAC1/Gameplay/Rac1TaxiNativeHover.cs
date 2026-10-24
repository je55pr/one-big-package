namespace OBP.RAC1.Gameplay;

/// <summary>Initial retail PVar angular phases, in radians.</summary>
public readonly record struct Rac1TaxiHoverPhases(double Height, double Tilt, double Spin);

/// <summary>
/// Native Kerwan taxi bob and sway from the shared 0x00262b00 and
/// 0x00262b80 helpers, called by class-816 at 0x002d3c40 and
/// class-1012 at 0x002dd0f0. Both retail callers supply:
/// height 0.2, height phase 120 degrees/sec, tilt 4.5 degrees,
/// tilt phase 70 degrees/sec and spin phase 100 degrees/sec.
/// This owns presentation offsets only, never authored route geometry.
/// </summary>
public sealed class Rac1TaxiNativeHover
{
    public const int NativeFramesPerSecond = 60;
    public const double HeightAmplitude = 0.2d;
    public const double TiltAmplitudeRadians = Math.PI / 40d;
    private const double NativeFrameDuration = 1d / NativeFramesPerSecond;
    private const double HeightPhaseStep = 2d * Math.PI / 3d * NativeFrameDuration;
    private const double TiltPhaseStep = 7d * Math.PI / 18d * NativeFrameDuration;
    private const double SpinPhaseStep = 5d * Math.PI / 9d * NativeFrameDuration;

    private double _heightPhase;
    private double _tiltPhase;
    private double _spinPhase;
    private double _remainder;

    public Rac1TaxiNativeHover(Rac1TaxiHoverPhases phases)
    {
        if (!double.IsFinite(phases.Height) || !double.IsFinite(phases.Tilt) ||
            !double.IsFinite(phases.Spin))
            throw new ArgumentOutOfRangeException(nameof(phases));

        _heightPhase = Normalize(phases.Height);
        _tiltPhase = Normalize(phases.Tilt);
        _spinPhase = Normalize(phases.Spin);
        SetTilt();
        HeightOffset = HeightAmplitude * Math.Sin(_heightPhase);
    }

    public double HeightOffset { get; private set; }
    public double PitchRadians { get; private set; }
    public double RollRadians { get; private set; }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _remainder += seconds;
        while (_remainder + 1e-10 >= NativeFrameDuration)
        {
            _remainder = Math.Max(0, _remainder - NativeFrameDuration);
            // The retail height helper advances phase first and replaces its
            // last offset; angle helper writes tilt before advancing phases.
            _heightPhase = Normalize(_heightPhase + HeightPhaseStep);
            HeightOffset = HeightAmplitude * Math.Sin(_heightPhase);
            SetTilt();
            _tiltPhase = Normalize(_tiltPhase + TiltPhaseStep);
            _spinPhase = Normalize(_spinPhase + SpinPhaseStep);
        }
    }

    private void SetTilt()
    {
        double radial = TiltAmplitudeRadians * Math.Sin(_tiltPhase);
        PitchRadians = radial * Math.Sin(_spinPhase);
        RollRadians = radial * Math.Cos(_spinPhase);
    }

    private static double Normalize(double radians) =>
        Math.IEEERemainder(radians, 2d * Math.PI);
}

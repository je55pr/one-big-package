using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed first Aranos lift slice. LEVEL0 class 2753 instance 164 is the
/// thin moving platform immediately beside Ratchet's opening spawn.
///
/// Retail v1.01 live authority establishes:
/// - lower platform Z = the authored instance Z (~50.031)
/// - upper target Z = 100.0
/// - state-2 rise acceleration = 0.0019444445 units/tick²
/// - state-2 maximum rise step = 0.1 units/tick
/// - 60 native ticks/second
///
/// The first playable slice only needs the opening upward trip. Returning the
/// platform is deliberately left for the later route-controller recovery rather
/// than guessed here.
/// </summary>
public sealed class GcAranosOpeningLiftSession
{
    public const int NativeClassId = 2753;
    public const int OpeningInstanceIndex = 164;
    public const int NativeTicksPerSecond = 60;
    public const double UpperNativeZ = 100.0;
    public const double RiseAccelerationPerTick = 0.0019444444915279746;
    public const double MaxRiseStepPerTick = 0.10000000894069672;

    private double _currentNativeZ;
    private double _riseStep;

    public GcAranosOpeningLiftSession(RuntimeDynamicObject source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.SourceGame != "rac2" ||
            source.NativeClassId != NativeClassId ||
            source.InstanceIndex != OpeningInstanceIndex)
        {
            throw new InvalidDataException(
                $"Expected Aranos class-{NativeClassId} instance {OpeningInstanceIndex}, got " +
                $"{source.SourceGame}:{source.NativeClassId}:{source.InstanceIndex}.");
        }

        if (source.Transform.Matrix is not { Length: 16 } matrix ||
            !double.IsFinite(matrix[13]))
        {
            throw new InvalidDataException("Aranos opening lift has no usable authored transform.");
        }

        LowerNativeZ = matrix[13];
        _currentNativeZ = LowerNativeZ;
        if (!(UpperNativeZ > LowerNativeZ))
        {
            throw new InvalidDataException(
                $"Aranos opening lift lower Z {LowerNativeZ} is not below upper target {UpperNativeZ}.");
        }
    }

    public RuntimeDynamicObject Source { get; }
    public double LowerNativeZ { get; }
    public double CurrentNativeZ => _currentNativeZ;
    public double RiseStepPerTick => _riseStep;
    public GcAranosOpeningLiftPhase Phase { get; private set; } = GcAranosOpeningLiftPhase.LowerIdle;

    /// <summary>
    /// The retail platform does not commit to the climb without Ratchet riding it.
    /// The host supplies the platform-contact witness; once admitted, the opening
    /// trip remains latched so ordinary character motion cannot cancel it through
    /// a one-frame floor/contact wobble.
    /// </summary>
    public bool TryBeginRise(bool riderPresent)
    {
        if (Phase != GcAranosOpeningLiftPhase.LowerIdle || !riderPresent)
        {
            return false;
        }

        Phase = GcAranosOpeningLiftPhase.Rising;
        return true;
    }

    public GcAranosOpeningLiftStep AdvanceNativeTick()
    {
        double before = _currentNativeZ;
        if (Phase == GcAranosOpeningLiftPhase.Rising)
        {
            _riseStep = Math.Min(
                MaxRiseStepPerTick,
                _riseStep + RiseAccelerationPerTick);
            _currentNativeZ = Math.Min(UpperNativeZ, _currentNativeZ + _riseStep);
            if (_currentNativeZ >= UpperNativeZ)
            {
                _currentNativeZ = UpperNativeZ;
                _riseStep = 0d;
                Phase = GcAranosOpeningLiftPhase.UpperIdle;
            }
        }

        return new GcAranosOpeningLiftStep(
            Phase,
            _currentNativeZ,
            _currentNativeZ - before);
    }
}

public enum GcAranosOpeningLiftPhase
{
    LowerIdle,
    Rising,
    UpperIdle,
}

public sealed record GcAranosOpeningLiftStep(
    GcAranosOpeningLiftPhase Phase,
    double NativeZ,
    double DeltaNativeZ);

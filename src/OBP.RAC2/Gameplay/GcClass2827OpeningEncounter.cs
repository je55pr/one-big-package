namespace OBP.RAC2.Gameplay;

/// <summary>
/// Bounded projection of the retail Aranos opening MSR I approach. The shared
/// GC Moby physics layer is not reproduced here; only the source-tick planar
/// acceleration/cap measured on opening instances 205/206 is preserved.
/// </summary>
public sealed class GcClass2827ApproachSession
{
    public const int NativeTicksPerSecond = 60;
    public const double PlanarAccelerationPerTick = 0.005d;
    public const double PlanarMaxStepPerTick = 0.1d;

    private double _planarStep;

    public double PlanarStep => _planarStep;

    public void Reset() => _planarStep = 0d;

    public GcClass2827ApproachStep Advance(
        double x,
        double z,
        double targetX,
        double targetZ,
        int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (int i = 0; i < ticks; i++)
        {
            double dx = targetX - x;
            double dz = targetZ - z;
            double distance = Math.Sqrt((dx * dx) + (dz * dz));
            if (!double.IsFinite(distance) || distance <= GcClass2827HostileSession.AttackEntryDistanceExclusive)
            {
                break;
            }

            _planarStep = Math.Min(
                PlanarMaxStepPerTick,
                _planarStep + PlanarAccelerationPerTick);

            double remaining = distance - GcClass2827HostileSession.AttackEntryDistanceExclusive;
            double step = Math.Min(_planarStep, remaining);
            if (step <= 0d)
            {
                break;
            }

            x += (dx / distance) * step;
            z += (dz / distance) * step;
        }

        return new GcClass2827ApproachStep(x, z, _planarStep);
    }
}

public readonly record struct GcClass2827ApproachStep(
    double X,
    double Z,
    double PlanarStep);

/// <summary>
/// Shared-volume boundary recovered from the class-2827 state-13 path. Retail
/// emits four joint volumes into common collision accumulation and Ratchet later
/// performs one mask-1 damage query. Therefore one native attack cycle may
/// publish at most one player damage record here, regardless of overlapping
/// joint count or repeated contact frames.
/// </summary>
public sealed class GcClass2827AttackCycleSession
{
    public const int ObservedPlayerContactStateTick = 45;
    public const int ObservedSequence16DurationTicks = 100;
    public const int ObservedSequence27DurationTicks = 114;
    public const int ObservedRecoveryDurationTicks = 90;

    private int? _nativeSequence;
    private bool _contactCommitted;

    public int? NativeSequence => _nativeSequence;
    public bool ContactCommitted => _contactCommitted;

    public void Begin(int nativeSequence)
    {
        if (nativeSequence is not (
            GcClass2827HostileSession.AttackSequence16 or
            GcClass2827HostileSession.AttackSequence27))
        {
            throw new ArgumentOutOfRangeException(nameof(nativeSequence));
        }

        _nativeSequence = nativeSequence;
        _contactCommitted = false;
    }

    public GcPlayerDamageRecord? TryAggregatePlayerContact(
        IReadOnlyList<GcClass2827AttackContact> overlappingContacts)
    {
        ArgumentNullException.ThrowIfNull(overlappingContacts);
        if (_nativeSequence is null || _contactCommitted || overlappingContacts.Count == 0)
        {
            return null;
        }

        float damageHp = overlappingContacts.Max(contact => contact.DamageHp);
        if (!float.IsFinite(damageHp) || damageHp <= 0f)
        {
            return null;
        }

        _contactCommitted = true;
        return new GcPlayerDamageRecord(
            GcRatchetNanotechSession.DamageQueryMask,
            damageHp);
    }

    public int ObservedDurationTicks() => _nativeSequence switch
    {
        GcClass2827HostileSession.AttackSequence16 => ObservedSequence16DurationTicks,
        GcClass2827HostileSession.AttackSequence27 => ObservedSequence27DurationTicks,
        _ => throw new InvalidOperationException("No class-2827 attack cycle is active."),
    };

    public void End()
    {
        _nativeSequence = null;
        _contactCommitted = false;
    }
}

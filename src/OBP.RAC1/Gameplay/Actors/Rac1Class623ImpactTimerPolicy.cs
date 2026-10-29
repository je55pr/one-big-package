namespace OBP.RAC1.Gameplay;

/// <summary>
/// The original shared impact service calls 0x22dc50 with the native
/// collision slot's signed halfword +0x1c (Rilgar 0x284b30).
/// This is a source countdown callback, not a collision test or
/// final response classifier.
/// </summary>
public static class Rac1Class623ImpactTimerPolicy
{
    public static Rac1ImpactCountdownResult Tick(short value)
    {
        if (value == 0)
            return new(0, 1);

        // The PS2 PMAXW at 0x22dc5c clamps the nonzero signed
        // halfword to at least one before the decrement.
        short remaining = (short)(Math.Max(value, (short)1) - 1);
        return new(remaining, remaining == 0 ? 2 : 0);
    }

    /// <summary>
    /// The separate callback-result-0 path (active countdown) uses
    /// original table 0x2176a0 and the source collision flag mask
    /// held in s8 at Rilgar 0x284bc4–0x284c24. Status and response
    /// below are intermediate generic-service values. This does not
    /// reconstruct a final collision, owner or damage event.
    /// </summary>
    public static Rac1ActiveImpactTimerResult ResolveActiveCountdown(
        int nativeStatus, int responseIndex, uint collisionFlags)
    {
        if (responseIndex == 11)
            return new(nativeStatus, 12, false);

        uint primary;
        uint secondary;
        switch (responseIndex)
        {
            case 0:
            case 1:
                primary = 0x10;
                secondary = 0x20;
                break;
            case 4:
                primary = 0x01;
                secondary = 0x02;
                break;
            case 7:
                primary = 0x04;
                secondary = 0x08;
                break;
            default:
                return new(nativeStatus, 13, true);
        }

        int nextResponse = (collisionFlags & primary) == 0
            ? 11 : responseIndex;
        int nextStatus = (collisionFlags & secondary) == 0
            ? 1 : nativeStatus;
        return new(nextStatus, nextResponse, true);
    }

    /// <summary>
    /// Native allocator 0x22dbd8 converts duration to float, loads a
    /// source-owned scale from gp-0x7e98, forms ACC=0.25f+0.25f,
    /// computes ACC + duration*scale using the EE MADD.S instruction,
    /// then CVT.W.S converts the result toward zero.
    /// The native scale is NOT recoverable from these instructions and
    /// must be supplied from the actual source runtime. Finite-range
    /// arithmetic here is a bounded host approximation, not bit-exact
    /// EE FPU rounding, denormal handling or overflow saturation.
    /// </summary>
    public static int? TicksForDuration(int duration, float? nativeGlobalScale)
    {
        if (duration < 0 ||
            nativeGlobalScale is not float scale ||
            !float.IsFinite(scale) ||
            scale <= 0f)
            return null;

        float accumulator = 0.25f + 0.25f;
        float scaled = (float)duration * scale + accumulator;
        // Original selected durations 15/30/37/45/60 and ordinary
        // positive source scales fit signed 32-bit timer storage.
        if (!float.IsFinite(scaled) || scaled >= 2147483648f)
            return null;
        return (int)MathF.Truncate(scaled);
    }

    /// <summary>
    /// Native response index selects the proposed timer *duration*
    /// through jump table 0x217670 only when countdown result != 0.
    /// The timer allocator can then convert this selected duration
    /// only if the real native global scale is observed. Index 7
    /// needs the real byte at 0x13e533;
    /// absence means unknown, never an inferred duration.
    /// </summary>
    public static int? DurationForResponseIndex(int responseIndex, bool? alternateDurationFlag)
    {
        return responseIndex switch
        {
            0 => 37,
            1 => 15,
            4 or 11 => 60,
            7 => alternateDurationFlag is null ? null : alternateDurationFlag.Value ? 45 : 60,
            _ => 30
        };
    }
}

public readonly record struct Rac1ImpactCountdownResult(
    short Remaining, int CallbackResult);

/// <param name="Status">Source status after the active-countdown path.</param>
/// <param name="ResponseIndex">Source response after this branch, not
/// proof of impact ownership/admission.</param>
/// <param name="ClearsScalar">Whether the branch writes zero to the
/// native scalar slot at Moby +0x2c. All paths clear the native
/// collision halfwords +0x14/+0x16 before return.</param>
public readonly record struct Rac1ActiveImpactTimerResult(
    int Status, int ResponseIndex, bool ClearsScalar);

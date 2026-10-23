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
    /// Native response index selects the proposed timer *duration*
    /// through jump table 0x217670 only when countdown result != 0.
    /// Actual timer value is written by a separate 0x22dbd8 helper
    /// not represented here. Index 7 needs the real byte at 0x13e533;
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

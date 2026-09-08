namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed class-500 Bolt reward mechanics recovered from the loaded
/// Going Commando overlay. This intentionally models only the pieces whose
/// semantics are proved: authored-value percentage scaling, the deferred
/// reservoir, progression/RNG physical-pickup budget, and denomination partition.
/// Runtime table initialization and unrelated reward modes stay outside this type.
/// </summary>
public static class GcBoltReward
{
    public static readonly int[] Denominations = [1000, 500, 100, 50, 20, 5, 1];

    /// <summary>
    /// Reproduce loaded 0x002D9528: select-table percentage times the authored
    /// Moby bolt value, divided by 100 with integer truncation.
    /// </summary>
    public static int ScaleAuthoredValue(int authoredValue, int percentage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(authoredValue);
        if ((uint)percentage > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Native percentage table entries are bytes.");
        }

        return checked(authoredValue * percentage) / 100;
    }

    /// <summary>
    /// Reproduce the class-500 budget passed from loaded 0x00392158 to
    /// 0x0030F920. <paramref name="progressionLikeInput"/> is deliberately not
    /// semantically named: retail proves its thresholds but not its game symbol.
    /// <paramref name="rngMod2"/> is the proven 0/1 result of 0x003104E8(2).
    /// </summary>
    public static int PhysicalPieceBudget(int progressionLikeInput, int rngMod2)
    {
        if ((uint)rngMod2 > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rngMod2), "Native RNG(2) result must be 0 or 1.");
        }

        int baseBudget = progressionLikeInput < 11 ? 2 : progressionLikeInput < 21 ? 1 : 0;
        return baseBudget + rngMod2;
    }

    /// <summary>
    /// Reproduce the flags&amp;0x8 branch of loaded 0x003177C8. Greedy physical
    /// pickups are emitted in native denomination order until the piece budget
    /// is exhausted; any remaining value is returned separately for the native
    /// deferred-reward reservoir at loaded address 0x001A7A14.
    /// </summary>
    public static GcBoltRewardPlan PartitionPhysicalPieces(int rewardValue, int maxPhysicalPieces)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rewardValue);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPhysicalPieces);

        int remainingValue = rewardValue;
        int remainingPieces = maxPhysicalPieces;
        var physical = new List<int>(Math.Min(maxPhysicalPieces, 16));

        foreach (int denomination in Denominations)
        {
            if (remainingPieces == 0 || remainingValue == 0)
            {
                break;
            }

            int count = Math.Min(remainingPieces, remainingValue / denomination);
            for (int i = 0; i < count; i++)
            {
                physical.Add(denomination);
            }

            remainingValue -= count * denomination;
            remainingPieces -= count;
        }

        return new GcBoltRewardPlan(physical, remainingValue);
    }

    /// <summary>
    /// Reproduce the loaded 0x0030FCA0 reservoir drain. Each reward event releases
    /// ceil(pool / 50) into the current reward and leaves the rest deferred.
    /// </summary>
    public static GcBoltDeferredRelease ReleaseDeferred(int deferredPool)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deferredPool);

        int released = deferredPool / 50 + (deferredPool % 50 == 0 ? 0 : 1);
        return new GcBoltDeferredRelease(released, deferredPool - released);
    }
}

public sealed record GcBoltRewardPlan(IReadOnlyList<int> PhysicalDenominations, int DeferredValue)
{
    public int PhysicalValue => PhysicalDenominations.Sum();
    public int TotalValue => PhysicalValue + DeferredValue;
}

public sealed record GcBoltDeferredRelease(int ReleasedValue, int RemainingValue)
{
    public int OriginalValue => ReleasedValue + RemainingValue;
}

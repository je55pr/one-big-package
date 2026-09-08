namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed class-500 Bolt reward mechanics recovered from the loaded
/// Going Commando overlay. This intentionally models only the pieces whose
/// semantics are proved: authored-value percentage scaling, the retail percentage
/// banks, the deferred reservoir, progression/RNG physical-pickup budget, and
/// denomination partition. Unrelated reward modes stay outside this type.
/// </summary>
public static class GcBoltReward
{
    public static readonly int[] Denominations = [1000, 500, 100, 50, 20, 5, 1];

    public const uint AuthoredBank0Address = 0x001A89C8;
    public const uint AuthoredBank1Address = 0x001A89D0;
    public const uint EmissionBank0Address = 0x001A89D8;
    public const uint EmissionBank1Address = 0x001A89E0;
    public const uint PackedSelectorBaseAddress = 0x0019B4A8;
    public const int PackedSelectorTableBytes = 0x400;

    // Loaded .lit data at 0x001A89C8..0x001A89E7. The first pair is consumed
    // by the authored Moby +0xB4 reward path; the second pair is used by the
    // earlier reward-emission branch. Selector bit 3 chooses bank 1.
    private static readonly byte[] AuthoredBank0Data = [100, 50, 40, 30, 25, 20, 15, 10];
    private static readonly byte[] AuthoredBank1Data = [100, 100, 100, 100, 100, 100, 100, 100];
    private static readonly byte[] EmissionBank0Data = [100, 50, 40, 30, 25, 20, 15, 10];
    private static readonly byte[] EmissionBank1Data = [100, 30, 10, 10, 10, 10, 10, 10];

    public static ReadOnlySpan<byte> AuthoredBank0 => AuthoredBank0Data;
    public static ReadOnlySpan<byte> AuthoredBank1 => AuthoredBank1Data;
    public static ReadOnlySpan<byte> EmissionBank0 => EmissionBank0Data;
    public static ReadOnlySpan<byte> EmissionBank1 => EmissionBank1Data;

    /// <summary>Address one 0x400-byte selector block for the native runtime context index.</summary>
    public static uint PackedSelectorTableAddress(int contextIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contextIndex);
        return checked(PackedSelectorBaseAddress + (uint)contextIndex * PackedSelectorTableBytes);
    }

    /// <summary>Extract one 4-bit native selector from a packed runtime-context UID table.</summary>
    public static int ReadPackedSelector(ReadOnlySpan<byte> packedSelectors, int uid)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uid);
        int byteIndex = uid >> 1;
        if ((uint)byteIndex >= (uint)packedSelectors.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(uid), "UID lies outside the supplied packed selector table.");
        }

        int packed = packedSelectors[byteIndex];
        return (uid & 1) == 0 ? packed & 0x0f : packed >> 4;
    }

    /// <summary>Resolve the retail percentage used by the authored Moby reward path.</summary>
    public static int AuthoredPercentageForSelector(int selector)
    {
        if ((uint)selector > 0x0f)
        {
            throw new ArgumentOutOfRangeException(nameof(selector), "Native reward selector must be a 4-bit value.");
        }

        return (selector & 0x08) == 0
            ? AuthoredBank0Data[selector & 0x07]
            : AuthoredBank1Data[selector & 0x07];
    }

    public static int ScaleAuthoredValueForSelector(int authoredValue, int selector) =>
        ScaleAuthoredValue(authoredValue, AuthoredPercentageForSelector(selector));

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

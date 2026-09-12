using System.Buffers.Binary;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail-backed R&C1 class-500 Bolt Crate behavior. Native record offsets,
/// states, persistence identity and reward selection remain game-specific.
/// </summary>
public static class Rac1BoltCrate
{
    public const int NativeClassId = 500;
    public const int ActiveNativeState = 1;
    public const int BreakTransitionNativeState = 3;
    public const int DisabledNativeState = 0xfd;
    public const string InstancePayloadFormat = "rac1-moby-instance-0x78";
    public const string PVarPayloadFormat = "rac1-pvar";

    public static bool ShouldBreak(double nativeDamage)
    {
        if (!double.IsFinite(nativeDamage))
            throw new ArgumentOutOfRangeException(nameof(nativeDamage));
        return nativeDamage > 0;
    }

    public static Rac1BoltCrateAuthoredState? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != NativeClassId) return null;
        byte[]? raw = source.NativePayloads?.FirstOrDefault(p => p.Format == InstancePayloadFormat)?.Data;
        if (raw is null || raw.Length < Rac1Instances.MobyRecordSize) return null;

        int uid = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0x0c, 2));
        int rewardCentre = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x10, 4));
        if (source.NativeUid is int runtimeUid && runtimeUid != uid)
            throw new InvalidDataException($"R&C1 class-500 UID mismatch: authored {uid}, runtime {runtimeUid}.");
        if (rewardCentre < 0)
            throw new InvalidDataException($"R&C1 class-500 reward centre {rewardCentre} is negative.");

        byte[]? pvar = source.NativePayloads?.FirstOrDefault(p => p.Format == PVarPayloadFormat)?.Data;
        return new Rac1BoltCrateAuthoredState(uid, rewardCentre, pvar?.Length);
    }

    public static Rac1BoltRewardRange RewardRange(int rewardCentre)
    {
        if (rewardCentre != 10)
            throw new NotSupportedException(
                $"R&C1 Bolt Crate reward centre {rewardCentre} is not yet admitted; centre 10 is the retail-backed representative.");
        return new Rac1BoltRewardRange(10, 3, 7, 13);
    }

    public static int ValueForNativePickupClass(int nativeClassId) => nativeClassId switch
    {
        13 => 1,
        14 => 5,
        15 => 20,
        16 => 50,
        _ => throw new ArgumentOutOfRangeException(nameof(nativeClassId)),
    };

    /// <summary>
    /// Exact low-value branch used by the Veldin class-500 specimens. Retail
    /// repeatedly emits 5-value pieces while value >= 7, then 1-value pieces.
    /// High-value piece-budget behavior is intentionally not generalized here.
    /// </summary>
    public static IReadOnlyList<int> PartitionRepresentativeTotal(int selectedTotal)
    {
        if (selectedTotal < 1 || selectedTotal >= 20)
            throw new ArgumentOutOfRangeException(nameof(selectedTotal),
                "Representative R&C1 class-500 partition is proven only for totals 1..19.");

        var values = new List<int>();
        int remaining = selectedTotal;
        while (remaining >= 7)
        {
            values.Add(5);
            remaining -= 5;
        }
        while (remaining-- > 0) values.Add(1);
        return values;
    }
}

public sealed record Rac1BoltCrateAuthoredState(int Uid, int RewardCentre, int? PVarSize);

public sealed record Rac1BoltRewardRange(int Centre, int Jitter, int Minimum, int Maximum)
{
    public bool Contains(int value) => value >= Minimum && value <= Maximum;
}

public sealed record Rac1BoltPickup(int PickupId, int NativeClassId, int Value);

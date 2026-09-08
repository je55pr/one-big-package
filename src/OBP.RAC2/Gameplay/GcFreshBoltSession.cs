using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Fresh-showcase bridge for the first playable GC Bolt-Crate milestone.
/// The retail executable image carries zero-filled packed selector storage, but
/// pre-level runtime history can mutate it before a level runs and OBP does not
/// reconstruct that state yet. Selector zero is therefore an explicit OBP
/// initial condition inside the recovered native domain, not an arbitrary-save claim.
/// </summary>
public sealed class GcFreshBoltSession
{
    private readonly HashSet<int> _paidUids = [];
    private readonly Dictionary<int, int> _outstandingPickups = [];
    private int _nextPickupId = 1;

    public int CollectedBolts { get; private set; }
    public int DeferredBolts { get; private set; }
    public int OutstandingPickupCount => _outstandingPickups.Count;

    /// <summary>
    /// Plan one class-500 payout from the explicit fresh-showcase selector (zero).
    /// The native reward centre is used as a deterministic showcase choice;
    /// native RNG sequence reproduction remains outside this session.
    /// </summary>
    public GcClass500Payout PlanClass500Payout(
        int uid, int authoredBolts, int rewardMultiplierByte,
        int progressionLikeInput, int rngMod2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uid);
        ArgumentOutOfRangeException.ThrowIfNegative(authoredBolts);
        if ((uint)rewardMultiplierByte > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rewardMultiplierByte), "Native reward multiplier input is a byte.");
        }

        if ((uint)rngMod2 > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rngMod2), "Native RNG(2) result must be 0 or 1.");
        }

        if (!_paidUids.Add(uid))
        {
            throw new InvalidOperationException($"UID {uid} already paid out in this fresh session.");
        }

        const int selector = 0;
        int percentage = GcBoltReward.AuthoredPercentageForSelector(selector);
        int percentageScaled = GcBoltReward.ScaleAuthoredValueForSelector(authoredBolts, selector);
        int effectiveMultiplier = Math.Max(1, rewardMultiplierByte);
        int scaled = checked(percentageScaled * effectiveMultiplier);
        var released = GcBoltReward.ReleaseDeferred(DeferredBolts);
        DeferredBolts = released.RemainingValue;
        int rewardCentre = checked(scaled + released.ReleasedValue);
        int pieceBudget = GcBoltReward.PhysicalPieceBudget(progressionLikeInput, rngMod2);
        var partition = GcBoltReward.PartitionPhysicalPieces(rewardCentre, pieceBudget);

        var pickups = new List<GcBoltPickup>(partition.PhysicalDenominations.Count);
        foreach (int denomination in partition.PhysicalDenominations)
        {
            int id = _nextPickupId++;
            _outstandingPickups.Add(id, denomination);
            pickups.Add(new GcBoltPickup(id, denomination));
        }

        DeferredBolts = checked(DeferredBolts + partition.DeferredValue);
        return new GcClass500Payout(
            uid, authoredBolts, selector, percentage,
            percentageScaled, rewardMultiplierByte, effectiveMultiplier, scaled,
            released.ReleasedValue, rewardCentre, pieceBudget,
            pickups, partition.DeferredValue);
    }

    public int CollectPickup(int pickupId)
    {
        if (!_outstandingPickups.Remove(pickupId, out int denomination))
        {
            throw new InvalidOperationException($"Bolt pickup {pickupId} is not outstanding.");
        }

        CollectedBolts = checked(CollectedBolts + denomination);
        return denomination;
    }
}
public sealed record GcBoltPickup(int PickupId, int Denomination);

public sealed record GcClass500Payout(
    int Uid,
    int AuthoredBolts,
    int Selector,
    int Percentage,
    int PercentageScaledValue,
    int RewardMultiplierByte,
    int EffectiveMultiplier,
    int ScaledRewardValue,
    int ReleasedDeferredValue,
    int RewardCentreValue,
    int PhysicalPieceBudget,
    IReadOnlyList<GcBoltPickup> PhysicalPickups,
    int DeferredValue)
{
    public int PhysicalValue => PhysicalPickups.Sum(pickup => pickup.Denomination);
    public int TotalValue => PhysicalValue + DeferredValue;
}

/// <summary>
/// Reads class-500 authored fields from the opaque runtime copy without leaking
/// GC packed-instance offsets into Godot/application code.
/// </summary>
public static class GcClass500Authority
{
    private const string InstancePayloadFormat = "rac2-moby-instance-0x88";
    private const string PVarPayloadFormat = "rac2-pvar";

    public static GcClass500AuthoredState? Read(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac2" || source.NativeClassId != 500)
        {
            return null;
        }

        var raw = source.NativePayloads?.FirstOrDefault(payload => payload.Format == InstancePayloadFormat)?.Data;
        if (raw is null || raw.Length < 0x18)
        {
            return null;
        }
        int uid = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x10, 4));
        int authoredBolts = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x14, 4));
        var pvar = source.NativePayloads?.FirstOrDefault(payload => payload.Format == PVarPayloadFormat)?.Data;
        byte? pvarC8 = pvar is { Length: > 0xC8 } ? pvar[0xC8] : null;

        return new GcClass500AuthoredState(uid, authoredBolts, pvarC8);
    }
}

public sealed record GcClass500AuthoredState(int Uid, int AuthoredBolts, byte? PvarC8);

using OBP.Core;
using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Fresh-showcase bridge for the common RAC2 crate Bolt reward path.
/// The retail executable image carries zero-filled packed selector storage, but
/// pre-level runtime history can mutate it before a level runs and OBP does not
/// reconstruct that state yet. Selector zero is therefore an explicit OBP
/// initial condition inside the recovered native domain, not an arbitrary-save claim.
/// </summary>
public sealed class Rac2FreshBoltSession
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
    public Rac2CratePayout PlanClass500Payout(
        int uid, int authoredBolts, int rewardMultiplierByte,
        int progressionLikeInput, int rngMod2) =>
        PlanCratePayout(uid, authoredBolts, rewardMultiplierByte, progressionLikeInput, rngMod2);

    /// <summary>The common authored-bolt path also serves class 505. Validate and calculate the entire
    /// payout before committing UID, deferred balance or pickup identities, so rejected plans leave the session unchanged.</summary>
    public Rac2CratePayout PlanCratePayout(
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

        if (_paidUids.Contains(uid))
        {
            throw new InvalidOperationException($"UID {uid} already paid out in this fresh session.");
        }

        const int selector = 0;
        int percentage = Rac2BoltReward.AuthoredPercentageForSelector(selector);
        int percentageScaled = Rac2BoltReward.ScaleAuthoredValueForSelector(authoredBolts, selector);
        int effectiveMultiplier = Math.Max(1, rewardMultiplierByte);
        int scaled = checked(percentageScaled * effectiveMultiplier);
        var released = Rac2BoltReward.ReleaseDeferred(DeferredBolts);
        int rewardCentre = checked(scaled + released.ReleasedValue);
        int pieceBudget = Rac2BoltReward.PhysicalPieceBudget(progressionLikeInput, rngMod2);
        var partition = Rac2BoltReward.PartitionPhysicalPieces(rewardCentre, pieceBudget);

        int deferred = checked(released.RemainingValue + partition.DeferredValue);
        int nextPickup = checked(_nextPickupId + partition.PhysicalDenominations.Count);
        var pickups = new List<Rac2BoltPickup>(partition.PhysicalDenominations.Count);
        int pickupId = _nextPickupId;
        foreach (int denomination in partition.PhysicalDenominations)
        {
            pickups.Add(new Rac2BoltPickup(pickupId++, denomination));
        }

        _paidUids.Add(uid);
        foreach (var pickup in pickups) _outstandingPickups.Add(pickup.PickupId, pickup.Denomination);
        _nextPickupId = nextPickup;
        DeferredBolts = deferred;
        return new Rac2CratePayout(
            uid, authoredBolts, selector, percentage,
            percentageScaled, rewardMultiplierByte, effectiveMultiplier, scaled,
            released.ReleasedValue, rewardCentre, pieceBudget,
            pickups, partition.DeferredValue);
    }

    public int CollectPickup(int pickupId) => CollectPickup(pickupId, _ => true);

    /// <summary>
    /// Offer an outstanding denomination to the host before committing collection.
    /// A rejected or throwing, non-reentrant acceptance callback leaves this session unchanged.
    /// Returns zero on rejection; an already consumed pickup remains an error.
    /// </summary>
    public int CollectPickup(int pickupId, Func<int, bool> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        if (!_outstandingPickups.TryGetValue(pickupId, out int denomination))
        {
            throw new InvalidOperationException($"Bolt pickup {pickupId} is not outstanding.");
        }

        int collected = checked(CollectedBolts + denomination);
        if (!accept(denomination)) return 0;
        _outstandingPickups.Remove(pickupId);
        CollectedBolts = collected;
        return denomination;
    }
}
public sealed record Rac2BoltPickup(int PickupId, int Denomination);

public sealed record Rac2CratePayout(
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
    IReadOnlyList<Rac2BoltPickup> PhysicalPickups,
    int DeferredValue)
{
    public int PhysicalValue => PhysicalPickups.Sum(pickup => pickup.Denomination);
    public int TotalValue => PhysicalValue + DeferredValue;
}

/// <summary>
/// Reads class-500 authored fields from the opaque runtime copy without leaking
/// GC packed-instance offsets into Godot/application code.
/// </summary>
public static class Rac2Class500Authority
{
    private const string InstancePayloadFormat = "rac2-moby-instance-0x88";
    private const string PVarPayloadFormat = "rac2-pvar";

    public static Rac2Class500AuthoredState? Read(RuntimeDynamicObject source)
    {
        if (source.SourceGame != ObpSourceGame.Rac2 || source.NativeClassId != 500)
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

        return new Rac2Class500AuthoredState(uid, authoredBolts, pvarC8);
    }
}

public sealed record Rac2Class500AuthoredState(int Uid, int AuthoredBolts, byte? PvarC8);

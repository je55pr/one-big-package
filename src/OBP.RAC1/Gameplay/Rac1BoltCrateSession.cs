using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Deterministic host for the first recovered R&C1 Bolt Crate loop. The caller
/// supplies the native RNG-selected total; this class validates it against the
/// recovered retail range and reproduces the proven low-value partition.
/// </summary>
public sealed class Rac1BoltCrateSession
{
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Rac1MobyPersistenceSession _persistence;
    private readonly Dictionary<int, Rac1BoltPickup> _outstanding = [];
    private int _nextPickupId = 1;

    public Rac1BoltCrateSession()
        : this(
            new Rac1MobyRuntimeSession(),
            new Rac1MobyPersistenceSession(levelId: 0))
    {
    }

    public Rac1BoltCrateSession(Rac1MobyPersistenceSession persistence)
        : this(new Rac1MobyRuntimeSession(), persistence)
    {
    }

    public Rac1BoltCrateSession(
        Rac1MobyRuntimeSession runtime,
        Rac1MobyPersistenceSession persistence)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public int CollectedBolts { get; private set; }
    public int DestroyedCrateCount =>
        _persistence.UidStates.Count(pair =>
            pair.Key.NativeClassId == Rac1BoltCrate.NativeClassId &&
            pair.Value == Rac1MobyUidPersistenceBits.BothSet);
    public int OutstandingPickupCount => _outstanding.Count;

    public Rac1MobyRuntimeInstance Register(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        if (source.SourceGame != "rac1" ||
            source.NativeClassId != Rac1BoltCrate.NativeClassId)
            throw new ArgumentException(
                "Source is not an R&C1 class-500 Bolt Crate.",
                nameof(source));
        current.EnsureMatches(source);
        _ = Rac1BoltCrate.ReadAuthored(source)
            ?? throw new InvalidDataException(
                "R&C1 class-500 authored authority state is unavailable.");

        var key = new Rac1MobyRuntimeKey(
            source.NativeClassId,
            source.InstanceIndex);
        if (_runtime.TryGet(key, out var existing))
        {
            _ = _runtime.Require(source);
            return existing!;
        }

        return _runtime.Register(
            source,
            current,
            Rac1BoltCrate.ActiveNativeState,
            Rac1BoltCrate.RequirePVar(source));
    }

    public Rac1BoltCrateBreakResult? ApplyDamage(
        RuntimeDynamicObject source,
        RuntimeEntityState current,
        Rac1GameplayDamageEvent damage,
        int selectedTotal)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var target = new Rac1MobyRuntimeKey(
            source.NativeClassId,
            source.InstanceIndex);
        if (!damage.Target.MatchesMoby(target))
            throw new ArgumentException(
                "R&C1 Bolt Crate damage target does not match the supplied runtime entity.",
                nameof(damage));

        return ApplyDamage(
            source,
            current,
            damage.NativeDamage,
            selectedTotal);
    }

    public Rac1BoltCrateBreakResult? ApplyDamage(
        RuntimeDynamicObject source,
        RuntimeEntityState current,
        double nativeDamage,
        int selectedTotal)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != Rac1BoltCrate.NativeClassId)
            throw new ArgumentException("Source is not an R&C1 class-500 Bolt Crate.", nameof(source));
        current.EnsureMatches(source);
        if (!Rac1BoltCrate.ShouldBreak(nativeDamage)) return null;

        var authored = Rac1BoltCrate.ReadAuthored(source)
            ?? throw new InvalidDataException("R&C1 class-500 authored authority state is unavailable.");
        var range = Rac1BoltCrate.RewardRange(authored.RewardCentre);
        if (!range.Contains(selectedTotal))
            throw new ArgumentOutOfRangeException(nameof(selectedTotal),
                $"Native selected total {selectedTotal} is outside recovered range {range.Minimum}..{range.Maximum}.");

        var values = Rac1BoltCrate.PartitionRepresentativeTotal(selectedTotal);
        var persisted = _persistence.QueryUid(source);
        if (persisted.LevelIndexedMap || persisted.LocalSessionMap)
            throw new InvalidOperationException(
                $"Bolt Crate UID {authored.Uid} already has recovered persistence state in level {_persistence.LevelId}.");

        var runtimeInstance = Register(source, current);
        if (!runtimeInstance.IsActive ||
            runtimeInstance.State.NativeState != Rac1BoltCrate.ActiveNativeState)
            throw new InvalidOperationException(
                "Inactive or non-active-state Bolt Crate cannot break again.");

        _persistence.UpdateUid(source, Rac1MobyUidPersistenceBits.BothSet);
        var pickups = new List<Rac1BoltPickup>(values.Count);
        foreach (int value in values)
        {
            int nativeClassId = value switch
            {
                1 => 13,
                5 => 14,
                _ => throw new InvalidOperationException($"Unexpected representative denomination {value}."),
            };
            var pickup = new Rac1BoltPickup(_nextPickupId++, nativeClassId, value);
            _outstanding.Add(pickup.PickupId, pickup);
            pickups.Add(pickup);
        }

        _runtime.SetNativeState(
            runtimeInstance,
            Rac1BoltCrate.BreakTransitionNativeState);
        _runtime.Terminalize(
            runtimeInstance,
            Rac1BoltCrate.DisabledNativeState);

        return new Rac1BoltCrateBreakResult(
            authored, range, selectedTotal, Rac1BoltCrate.ActiveNativeState,
            Rac1BoltCrate.BreakTransitionNativeState, Rac1BoltCrate.DisabledNativeState,
            pickups, runtimeInstance.EntityState);
    }

    public int CollectPickup(int pickupId)
    {
        if (!_outstanding.Remove(pickupId, out var pickup))
            throw new InvalidOperationException($"R&C1 bolt pickup {pickupId} is not outstanding.");

        int retailValue = Rac1BoltCrate.ValueForNativePickupClass(pickup.NativeClassId);
        if (retailValue != pickup.Value)
            throw new InvalidDataException($"Pickup class/value mismatch: {pickup.NativeClassId} != {pickup.Value}.");
        CollectedBolts = checked(CollectedBolts + retailValue);
        return retailValue;
    }
}

/// <summary>
/// Native transition evidence paired with the neutral deactivation projection.
/// UID persistence bits are native R&C1 state and therefore remain represented
/// here rather than in <see cref="RuntimeEntityState"/>.
/// </summary>
public sealed record Rac1BoltCrateBreakResult(
    Rac1BoltCrateAuthoredState Authored,
    Rac1BoltRewardRange RewardRange,
    int SelectedTotal,
    int NativeStateBefore,
    int NativeBreakTransitionState,
    int NativeDisabledState,
    IReadOnlyList<Rac1BoltPickup> Pickups,
    RuntimeEntityState EntityState)
{
    public int PhysicalValue => Pickups.Sum(p => p.Value);
}

namespace OBP.RAC3.Gameplay;

/// <summary>
/// One resolved UYA indexed-resource slot. Human item/ammo names remain unknown;
/// these fields mirror only the TABLE1 selector/add-service dataflow.
/// </summary>
public sealed record UyaIndexedResourceSlot(
    int ResourceIndex,
    bool Available,
    int ItemDefinitionIndex,
    int Current,
    int PickupAmount,
    int Capacity);

public sealed record UyaClass3291PickupPlan(
    int ResourceIndex,
    int Amount)
{
    public const int NativeClassId = 3291;
}

public sealed record UyaClass500ResourceDropPlan(
    int? SelectedResourceIndex,
    bool UsedUnderCapacityPool,
    IReadOnlyList<UyaClass3291PickupPlan> Pickups)
{
    public bool HasDrop => Pickups.Count > 0;
}
/// <summary>
/// Retail-backed TABLE1 class-500 indexed-resource drop selector.
///
/// Native 0x00440E98 scans 156 slots, filters unavailable/zero-capacity and an
/// exact exclusion set, prefers candidates below capacity, selects by a native
/// bounded RNG result, then returns one class-3291 spawn except when a separate
/// rand(5) result is zero, where it returns two.
/// </summary>
public static class UyaClass500ResourceDropPlanner
{
    public const int ResourceSlotCount = 0x9c;
    public const int PickupCountRandomBound = 5;

    private static readonly HashSet<int> Excluded =
    [
        0x16, 0x57, 0x6f, 0x97, 0xa7,
        0xc0, 0xc1, 0xc2, 0xc3, 0xc4,
    ];

    public static IReadOnlySet<int> ExcludedResourceIndices => Excluded;

    public static UyaClass500ResourceDropPlan Plan(
        IReadOnlyList<UyaIndexedResourceSlot> slots,
        int resourceSelectionRoll,
        int pickupCountRoll)
    {
        ValidateSlots(slots);

        var eligible = slots
            .Where(IsEligible)
            .OrderBy(slot => slot.ResourceIndex)
            .ToArray();
        if (eligible.Length == 0)
            return new UyaClass500ResourceDropPlan(null, false, []);
        var underCapacity = eligible
            .Where(slot => slot.Current < slot.Capacity)
            .ToArray();
        bool usedUnderCapacityPool = underCapacity.Length > 0;
        var candidates = usedUnderCapacityPool ? underCapacity : eligible;

        if (resourceSelectionRoll < 0 || resourceSelectionRoll >= candidates.Length)
            throw new ArgumentOutOfRangeException(
                nameof(resourceSelectionRoll),
                $"UYA resource selection roll must be in [0,{candidates.Length}).");
        if (pickupCountRoll < 0 || pickupCountRoll >= PickupCountRandomBound)
            throw new ArgumentOutOfRangeException(
                nameof(pickupCountRoll),
                "UYA class-500 pickup-count roll must be a native rand(5) result.");

        var selected = candidates[resourceSelectionRoll];
        int pickupCount = pickupCountRoll == 0 ? 2 : 1;
        var pickups = Enumerable.Range(0, pickupCount)
            .Select(_ => new UyaClass3291PickupPlan(
                selected.ResourceIndex,
                selected.PickupAmount))
            .ToArray();

        return new UyaClass500ResourceDropPlan(
            selected.ResourceIndex,
            usedUnderCapacityPool,
            pickups);
    }

    public static bool IsEligible(UyaIndexedResourceSlot slot) =>
        slot.Available &&
        slot.Capacity != 0 &&
        !Excluded.Contains(slot.ResourceIndex);
    private static void ValidateSlots(IReadOnlyList<UyaIndexedResourceSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != ResourceSlotCount)
            throw new ArgumentException(
                $"UYA indexed-resource selection requires exactly {ResourceSlotCount} slots.",
                nameof(slots));

        var seen = new bool[ResourceSlotCount];
        foreach (var slot in slots)
        {
            if (slot.ResourceIndex is < 0 or >= ResourceSlotCount)
                throw new ArgumentOutOfRangeException(
                    nameof(slots),
                    $"UYA resource index {slot.ResourceIndex} is outside the native 156-slot table.");
            if (seen[slot.ResourceIndex])
                throw new ArgumentException(
                    $"UYA resource index {slot.ResourceIndex} is duplicated.",
                    nameof(slots));
            seen[slot.ResourceIndex] = true;

            if (slot.ItemDefinitionIndex is < byte.MinValue or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(slots),
                    "UYA item-definition indices are native bytes.");
            if (slot.Current < 0 || slot.PickupAmount < 0 || slot.Capacity < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(slots),
                    "UYA indexed-resource counts cannot be negative.");
        }
    }
}
public sealed record UyaClass3291RuntimePickup(
    int PickupId,
    int ResourceIndex,
    int Amount,
    bool Collected = false)
{
    public int NativeClassId => UyaClass3291PickupPlan.NativeClassId;
}

public sealed record UyaIndexedResourceCollection(
    UyaClass3291RuntimePickup Pickup,
    int CurrentBefore,
    int CurrentAfter)
{
    public int Added => CurrentAfter - CurrentBefore;
}

/// <summary>
/// Runtime projection of class 3291 collection and the UYA indexed-resource
/// add/clamp service at 0x0050D980. Presentation/magnet motion stay outside.
/// </summary>
public sealed class UyaIndexedResourceSession
{
    private sealed record MutableSlot(int Current, int Capacity);

    private readonly Dictionary<int, MutableSlot> _slots;
    private readonly Dictionary<int, UyaClass3291RuntimePickup> _pickups = [];
    private int _nextPickupId = 1;
    public UyaIndexedResourceSession(IReadOnlyList<UyaIndexedResourceSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        _slots = slots.ToDictionary(
            slot => slot.ResourceIndex,
            slot =>
            {
                if (slot.Current < 0 || slot.Capacity < 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(slots),
                        "UYA indexed-resource counts cannot be negative.");
                return new MutableSlot(slot.Current, slot.Capacity);
            });
    }

    public IReadOnlyCollection<UyaClass3291RuntimePickup> Pickups => _pickups.Values;

    public IReadOnlyList<UyaClass3291RuntimePickup> Spawn(UyaClass500ResourceDropPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var spawned = new List<UyaClass3291RuntimePickup>(plan.Pickups.Count);
        foreach (var pickup in plan.Pickups)
        {
            if (!_slots.ContainsKey(pickup.ResourceIndex))
                throw new InvalidOperationException(
                    $"UYA class-3291 pickup targets unknown resource index {pickup.ResourceIndex}.");
            if (pickup.Amount < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(plan),
                    "UYA class-3291 pickup amount cannot be negative.");

            var runtime = new UyaClass3291RuntimePickup(
                checked(_nextPickupId++),
                pickup.ResourceIndex,
                pickup.Amount);
            _pickups.Add(runtime.PickupId, runtime);
            spawned.Add(runtime);
        }
        return spawned;
    }
    public UyaIndexedResourceCollection Collect(int pickupId)
    {
        if (!_pickups.TryGetValue(pickupId, out var pickup))
            throw new KeyNotFoundException($"Unknown UYA class-3291 pickup {pickupId}.");
        if (pickup.Collected)
            throw new InvalidOperationException(
                $"UYA class-3291 pickup {pickupId} was already consumed.");

        var slot = _slots[pickup.ResourceIndex];
        int before = slot.Current;
        int after = checked(before + pickup.Amount);
        if (slot.Capacity != 0 && after > slot.Capacity)
            after = slot.Capacity;

        _slots[pickup.ResourceIndex] = slot with { Current = after };
        var consumed = pickup with { Collected = true };
        _pickups[pickupId] = consumed;
        return new UyaIndexedResourceCollection(consumed, before, after);
    }

    public int Current(int resourceIndex) =>
        _slots.TryGetValue(resourceIndex, out var slot)
            ? slot.Current
            : throw new KeyNotFoundException(
                $"Unknown UYA indexed-resource slot {resourceIndex}.");
}

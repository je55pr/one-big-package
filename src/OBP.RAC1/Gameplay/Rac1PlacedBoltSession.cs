using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Live authored bolts share the Moby store but do not use crate UID bitfields.
/// Collection writes native object-state bytes at0x2a6ab0..0x2a6b28 before
/// removal. This session retains that lifetime through player death; native
/// sector-state encoding and cross-level/save restoration remain separate.
/// </summary>
public sealed class Rac1PlacedBoltSession(Rac1MobyRuntimeSession runtime)
{
    public sealed record Registration(int PickupId, Rac1PlacedBolt.Authored Authored, Rac1MobyRuntimeInstance Instance);
    public sealed record Collection(int Value, int AuthoredUid, RuntimeEntityState EntityState);
    private readonly Dictionary<int, Registration> _placed = [];
    private readonly HashSet<int> _collected = [];
    public IReadOnlyCollection<Registration> Registrations => _placed.Values;
    public int CollectedCount => _collected.Count;
    public int CollectedBolts { get; private set; }

    // Crate emission ids are positive. Authored ids occupy a disjoint source-owned range.
    public static int PickupId(int instanceIndex) => instanceIndex >= 0
        ? checked(-instanceIndex - 1) : throw new ArgumentOutOfRangeException(nameof(instanceIndex));

    public Registration Register(RuntimeDynamicObject source, RuntimeEntityState state)
    {
        var authored = Rac1PlacedBolt.ReadAuthored(source)
            ?? throw new ArgumentException("Source is not an R&C1 placed bolt.", nameof(source));
        state.EnsureMatches(source);
        int id = PickupId(source.InstanceIndex);
        if (_placed.TryGetValue(id, out var existing))
        {
            existing.Instance.EntityState.EnsureMatches(source);
            return existing;
        }
        var key = new Rac1MobyRuntimeKey(source.NativeClassId, source.InstanceIndex);
        var instance = runtime.TryGet(key, out var found) ? found! : runtime.Register(source, state, 3,
            source.NativePayloads!.Single(payload => payload.Format == Rac1BoltCrate.PVarPayloadFormat).Data);
        var registration = new Registration(id, authored, instance);
        _placed.Add(id, registration);
        return registration;
    }

    public bool TryCollect(int pickupId, out Collection? collection)
    {
        collection = null;
        if (!_placed.TryGetValue(pickupId, out var placed)) return false;
        if (_collected.Contains(pickupId) || !placed.Instance.IsActive)
            throw new InvalidOperationException("R&C1 placed bolt cannot be collected more than once.");
        int value = Rac1BoltCrate.ValueForNativePickupClass(placed.Authored.NativeClassId);
        // Arrival belongs to state4; the allocator's exact0xfd/0xfe choice is
        // not inferred from a host node. Use common neutral terminalization.
        runtime.SetNativeState(placed.Instance, 4);
        var state = runtime.Terminalize(placed.Instance).EntityState;
        _collected.Add(pickupId);
        CollectedBolts = checked(CollectedBolts + value);
        collection = new(value, placed.Authored.Uid, state);
        return true;
    }

    public bool IsCollected(int pickupId) => _collected.Contains(pickupId);
}

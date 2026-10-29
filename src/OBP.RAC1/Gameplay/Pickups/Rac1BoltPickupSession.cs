namespace OBP.RAC1.Gameplay;

/// <summary>Shared identities and arrival accounting for emitted retail bolt denominations.</summary>
public sealed class Rac1BoltPickupSession
{
    private readonly Dictionary<int, Rac1BoltPickup> _outstanding = [];
    private int _nextId = 1;
    public int OutstandingCount => _outstanding.Count;

    public IReadOnlyList<Rac1BoltPickup> Emit(int total)
    {
        var result = new List<Rac1BoltPickup>();
        foreach (int value in Rac1BoltCrate.PartitionTotal(total))
        {
            int nativeClass = value switch { 1 => 13, 5 => 14, 20 => 15, 50 => 16, _ => throw new InvalidDataException("Unknown bolt denomination.") };
            var pickup = new Rac1BoltPickup(checked(_nextId++), nativeClass, value);
            _outstanding.Add(pickup.PickupId, pickup);
            result.Add(pickup);
        }
        return result;
    }

    public int Collect(int id)
    {
        if (!_outstanding.Remove(id, out var pickup)) throw new InvalidOperationException($"Bolt pickup {id} is not outstanding.");
        return Rac1BoltCrate.ValueForNativePickupClass(pickup.NativeClassId);
    }

    public bool Discard(int id) => _outstanding.Remove(id);
}

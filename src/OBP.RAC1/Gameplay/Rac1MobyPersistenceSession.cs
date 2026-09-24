using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Stable per-level authored identity used by recovered R&C1 Moby persistence.
/// Native UID remains optional on the neutral object definition, but a UID-backed
/// persistence query requires one explicitly.
/// </summary>
public readonly record struct Rac1MobyPersistenceKey(
    int LevelId,
    int NativeClassId,
    int InstanceIndex,
    int NativeUid);

/// <summary>
/// The two independently observed R&C1 UID persistence channels. Their broader
/// lifetime/meaning is intentionally unnamed; both are retained separately.
/// </summary>
public readonly record struct Rac1MobyUidPersistenceBits(
    bool LevelIndexedMap,
    bool LocalSessionMap)
{
    public static Rac1MobyUidPersistenceBits Clear { get; } = new(false, false);
    public static Rac1MobyUidPersistenceBits BothSet { get; } = new(true, true);
}

/// <summary>
/// One per-level R&C1 UID persistence boundary. Class-local controllers decide
/// what setting/querying these bits means. This store does not implement mission
/// scripting, checkpoint policy, or cross-level serialization.
/// </summary>
public sealed class Rac1MobyPersistenceSession
{
    private readonly Dictionary<Rac1MobyPersistenceKey, Rac1MobyUidPersistenceBits> _uidStates = [];

    public Rac1MobyPersistenceSession(int levelId)
    {
        if (levelId is < 0 or > 18)
            throw new ArgumentOutOfRangeException(nameof(levelId));

        LevelId = levelId;
    }

    public int LevelId { get; }
    public int TrackedUidCount => _uidStates.Count;
    public IReadOnlyDictionary<Rac1MobyPersistenceKey, Rac1MobyUidPersistenceBits> UidStates => _uidStates;

    public Rac1MobyPersistenceKey RequireKey(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac1")
            throw new ArgumentException("Source is not an R&C1 Moby.", nameof(source));
        if (source.NativeUid is not int uid)
            throw new InvalidOperationException(
                $"R&C1 Moby class {source.NativeClassId} instance {source.InstanceIndex} has no authored native UID.");

        return new Rac1MobyPersistenceKey(
            LevelId,
            source.NativeClassId,
            source.InstanceIndex,
            uid);
    }

    public Rac1MobyUidPersistenceBits QueryUid(RuntimeDynamicObject source)
    {
        var key = RequireKey(source);
        return _uidStates.TryGetValue(key, out var state)
            ? state
            : Rac1MobyUidPersistenceBits.Clear;
    }

    public Rac1MobyUidPersistenceBits UpdateUid(
        RuntimeDynamicObject source,
        Rac1MobyUidPersistenceBits state)
    {
        var key = RequireKey(source);
        if (state == Rac1MobyUidPersistenceBits.Clear)
            _uidStates.Remove(key);
        else
            _uidStates[key] = state;

        return state;
    }
}

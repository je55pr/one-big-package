using System.Numerics;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

/// <summary>OBP-authored placement. Native payloads and source initialisation remain in registered adapters.</summary>
public sealed record ActorPlacement(string Id, FusionSourceIdentity Source, int LevelId, int NativeClassId,
    int? SourceInstanceIndex, RuntimeObjectTransform Transform, string AdapterId, string NativeContextId,
    int InitialNativeState, string ScenarioGroup)
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;

    public void Validate()
    {
        if (Source is null) throw new ArgumentException("Actor placement is missing source provenance.");
        Source.Validate();
        if (Version != CurrentVersion || string.IsNullOrWhiteSpace(Id) || Source.Game is not ("rac1" or "rac2" or "rac3") ||
            LevelId < 0 || NativeClassId < 0 || SourceInstanceIndex < 0 || InitialNativeState < 0 || string.IsNullOrWhiteSpace(AdapterId) ||
            string.IsNullOrWhiteSpace(NativeContextId) || string.IsNullOrWhiteSpace(ScenarioGroup) ||
            Transform?.Matrix is not { Length: 16 } matrix || matrix.Any(value => !double.IsFinite(value)) ||
            Math.Abs(matrix[3]) > 1e-8 || Math.Abs(matrix[7]) > 1e-8 || Math.Abs(matrix[11]) > 1e-8 || Math.Abs(matrix[15] - 1) > 1e-8)
            throw new ArgumentException("Invalid source-qualified actor placement, context or affine transform.");
        var m = matrix;
        double determinant = m[0] * (m[5] * m[10] - m[9] * m[6]) - m[4] * (m[1] * m[10] - m[9] * m[2]) + m[8] * (m[1] * m[6] - m[5] * m[2]);
        if (Math.Abs(determinant) < 1e-8) throw new ArgumentException("Actor placement has a singular transform.");
        for (int column = 0; column < 3; column++)
            for (int other = column; other < 3; other++)
            {
                double dot = Enumerable.Range(0, 3).Sum(row => m[column * 4 + row] * m[other * 4 + row]);
                if (Math.Abs(dot - (column == other ? 1 : 0)) > 1e-5 || determinant < 0)
                    throw new ArgumentException("Actor placements currently support rigid, orientation-preserving transforms only.");
            }
    }
}

public readonly record struct ActorTickFacts(long Tick, Vector3 PlayerPosition, bool PlayerAlive);
public sealed record ActorObservation(Vector3 Position, bool Alive, bool Retired, double? Health, int NativeState, int? NativeSequence);
public sealed record ActorHitResult(bool Applied, bool Terminal, string? Reason = null);

/// <summary>Native adapter port. Fusion owns ordering/admission; it never advances a native formula itself.</summary>
public interface IRegisteredActor : IDisposable
{
    ActorRegistration Registration { get; }
    ActorObservation Observe();
    void Tick(ActorTickFacts facts);
    ActorHitResult Apply(WeaponDamageIntent damage);
}

/// <summary>Several registered actors can share one source-owned controller store without double ticking it.</summary>
public sealed class ActorUpdateGroup(Action<ActorTickFacts> update)
{
    private long _lastTick = -1;
    public void Step(ActorTickFacts facts)
    {
        if (facts.Tick == _lastTick) return;
        if (facts.Tick < _lastTick) throw new InvalidOperationException("Actor clock moved backwards.");
        _lastTick = facts.Tick;
        update(facts);
    }
}

/// <summary>One world-lease registry, shared by imported worlds and authored encounters.</summary>
public sealed class ActorRuntimeSet(string worldInstanceId, long generation) : IDisposable
{
    private readonly Dictionary<WeaponTargetKey, IRegisteredActor> _actors = [];
    private readonly Dictionary<SourceWeaponKey, (string Build, Func<bool> Available)> _weapons = [];
    private readonly SortedDictionary<string, ActorUpdateGroup> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<(WeaponTargetKey Target, WeaponShotKey Shot, string Kind)> _receipts = [];
    private readonly HashSet<WeaponTargetKey> _retiredTargets = [];
    private bool _disposed;
    private long _tick;
    private int _nextInstance = 1_000_000;
    public string WorldInstanceId { get; } = !string.IsNullOrWhiteSpace(worldInstanceId) ? worldInstanceId : throw new ArgumentException("Missing world instance.");
    public long Generation { get; } = generation >= 0 ? generation : throw new ArgumentOutOfRangeException(nameof(generation));
    public long TickCount => _tick;
    public int Count => _actors.Count;
    public IReadOnlyList<IRegisteredActor> Ordered => _actors.Values.OrderBy(actor => actor.Registration.Instance.Target.SourceGame, StringComparer.Ordinal)
        .ThenBy(actor => actor.Registration.Instance.Target.NativeClassId).ThenBy(actor => actor.Registration.Instance.Target.InstanceIndex).ToArray();

    public ActorInstanceKey Allocate(string sourceGame, int nativeClass)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (sourceGame is not ("rac1" or "rac2" or "rac3") || nativeClass < 0) throw new ArgumentException("Unsupported native actor identity.");
        return new(WorldInstanceId, Generation, new(sourceGame, nativeClass, checked(_nextInstance++)));
    }

    public void Register(IRegisteredActor actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(actor);
        actor.Registration.Validate();
        var key = actor.Registration.Instance;
        if (key.WorldInstanceId != WorldInstanceId || key.Generation != Generation || _retiredTargets.Contains(key.Target) || !_actors.TryAdd(key.Target, actor))
            throw new ArgumentException("Actor belongs to a different/retired lease or duplicates a target identity.");
    }

    public IRegisteredActor? Find(WeaponTargetKey key) => _disposed ? null : _actors.GetValueOrDefault(key);

    public void RegisterWeapon(SourceWeaponKey key, string build, Func<bool> available)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        key.Validate(); ArgumentNullException.ThrowIfNull(available);
        if (string.IsNullOrWhiteSpace(build) || !_weapons.TryAdd(key, (build, available)))
            throw new ArgumentException("Duplicate or unqualified weapon admission.");
    }

    public void RegisterUpdateGroup(string sourceQualifiedId, Action<ActorTickFacts> update)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(sourceQualifiedId) || !_groups.TryAdd(sourceQualifiedId, new(update)))
            throw new ArgumentException("Duplicate or empty source update group.");
    }

    public void Step(Vector3 playerPosition, bool playerAlive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(playerPosition.X) || !float.IsFinite(playerPosition.Y) || !float.IsFinite(playerPosition.Z))
            throw new ArgumentException("Actor observations require a finite player position.");
        var facts = new ActorTickFacts(++_tick, playerPosition, playerAlive);
        foreach (var group in _groups.Values) group.Step(facts);
        foreach (var actor in Ordered)
            if (!actor.Observe().Retired) actor.Tick(facts);
    }

    public ActorHitResult Apply(ActorInstanceKey instance, WeaponDamageIntent damage)
    {
        if (_disposed || instance.WorldInstanceId != WorldInstanceId || instance.Generation != Generation || instance.Target != damage.Target ||
            !_actors.TryGetValue(instance.Target, out var actor)) return new(false, false, "Target belongs to an unavailable or retired world lease.");
        var observation = actor.Observe();
        if (!observation.Alive || observation.Retired) return new(false, true, "Target is no longer alive.");
        damage.Shot.Weapon.Validate();
        if (!_weapons.TryGetValue(damage.Shot.Weapon, out var weapon) || !weapon.Available())
            return new(false, false, "Weapon controller/source is not available in this registration.");
        if (!float.IsFinite(damage.SourceDamage) || damage.SourceDamage <= 0 || damage.Shot.Generation < 0 || damage.Shot.Sequence < 0 || string.IsNullOrWhiteSpace(damage.Kind))
            return new(false, false, "Unsupported source damage payload.");
        var receipt = (instance.Target, damage.Shot, damage.Kind);
        if (_receipts.Contains(receipt)) return new(false, false, "Duplicate source shot/contact.");
        var result = actor.Apply(damage);
        if (result.Applied) _receipts.Add(receipt);
        return result;
    }

    public void Retire(ActorInstanceKey key)
    {
        if (key.WorldInstanceId != WorldInstanceId || key.Generation != Generation) return;
        if (_actors.Remove(key.Target, out var actor)) { _retiredTargets.Add(key.Target); actor.Dispose(); }
        _receipts.RemoveWhere(receipt => receipt.Target == key.Target);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var actor in Ordered) actor.Dispose();
        _actors.Clear(); _receipts.Clear(); _groups.Clear(); _weapons.Clear(); _retiredTargets.Clear();
    }
}

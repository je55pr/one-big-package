using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class-local update hook. The shared runtime selects a controller only by
/// authored native class; the controller alone interprets its update facts.
/// </summary>
public interface IRac1MobyClassController
{
    int NativeClassId { get; }
    object Update(RuntimeDynamicObject source, object facts);
}

/// <summary>
/// Class-local native damage consumer. Registration is independent from update
/// dispatch because recovered damage semantics need not imply recovered AI/state
/// update logic for the same Moby class.
/// </summary>
public interface IRac1MobyDamageConsumer
{
    int NativeClassId { get; }

    bool CanApplyDamage(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage);

    object ApplyDamage(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage);
}

/// <summary>
/// One live authored R&C1 Moby. Identity and placement are immutable; PVar,
/// native state and neutral presentation/lifecycle are live runtime state.
/// </summary>
public sealed class Rac1MobyRuntimeInstance
{
    private readonly byte[] _pvar;

    internal Rac1MobyRuntimeInstance(
        RuntimeDynamicObject source,
        ReadOnlySpan<byte> pvar,
        Rac1MobyRuntimeState state)
    {
        Source = source; _pvar = pvar.ToArray();
        State = state;
    }

    public RuntimeDynamicObject Source { get; }
    public Rac1MobyRuntimeKey Key => State.Key;
    public RuntimeEntityIdentity Identity => State.EntityState.Identity;
    public int NativeClassId => Key.NativeClassId;
    public int InstanceIndex => Key.InstanceIndex;
    public int? NativeUid => Identity.NativeUid;
    public RuntimeObjectTransform AuthoredTransform => Source.Transform;
    public ReadOnlyMemory<byte> PVar => _pvar;
    public Rac1MobyRuntimeState State { get; internal set; }
    public RuntimeEntityState EntityState => State.EntityState;
    public RuntimeEntityPresence Presence => EntityState.Presentation.Presence;
    public bool IsActive =>
        !State.IsTerminalized &&
        Presence == RuntimeEntityPresence.Active;

    internal byte[] MutablePVar => _pvar;
}

/// <summary>
/// Shared live-Moby store for R&C1. Unknown classes may be registered and
/// observed without gaining guessed behaviour; update dispatch requires an
/// explicitly registered class controller.
/// </summary>
public sealed class Rac1MobyRuntimeSession
{
    private readonly Dictionary<Rac1MobyRuntimeKey, Rac1MobyRuntimeInstance> _instances = [];
    private readonly Dictionary<int, IRac1MobyClassController> _controllers = [];
    private readonly Dictionary<int, IRac1MobyDamageConsumer> _damageConsumers = [];

    public int RegisteredCount => _instances.Count;
    public IReadOnlyCollection<Rac1MobyRuntimeInstance> Instances => _instances.Values;

    public void RegisterController(IRac1MobyClassController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (!_controllers.TryAdd(controller.NativeClassId, controller))
            throw new InvalidOperationException(
                $"R&C1 Moby class {controller.NativeClassId} already has a runtime controller.");
    }

    public void RegisterDamageConsumer(IRac1MobyDamageConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (!_damageConsumers.TryAdd(consumer.NativeClassId, consumer))
            throw new InvalidOperationException(
                $"R&C1 Moby class {consumer.NativeClassId} already has a native damage consumer.");
    }

    public Rac1MobyRuntimeInstance Register(
        RuntimeDynamicObject source,
        RuntimeEntityState current,
        int nativeState,
        ReadOnlySpan<byte> pvar = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        current.EnsureMatches(source);

        var state = Rac1MobyRuntime.Create(source, nativeState, current);
        var instance = new Rac1MobyRuntimeInstance(source, pvar, state);
        if (!_instances.TryAdd(state.Key, instance))
            throw new InvalidOperationException(
                $"R&C1 Moby class {source.NativeClassId} instance {source.InstanceIndex} is already registered.");
        return instance;
    }

    public Rac1MobyRuntimeInstance Require(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = new Rac1MobyRuntimeKey(source.NativeClassId, source.InstanceIndex);
        if (!_instances.TryGetValue(key, out var instance))
            throw new InvalidOperationException(
                $"R&C1 Moby class {source.NativeClassId} instance {source.InstanceIndex} is not registered.");

        instance.EntityState.EnsureMatches(source);
        return instance;
    }

    public bool TryGet(Rac1MobyRuntimeKey key, out Rac1MobyRuntimeInstance? instance) =>
        _instances.TryGetValue(key, out instance);

    public Rac1MobyRuntimeState SetNativeState(
        Rac1MobyRuntimeInstance instance,
        int nativeState)
    {
        RequireOwned(instance);
        instance.State = Rac1MobyRuntime.WithNativeState(instance.State, nativeState);
        return instance.State;
    }

    public RuntimeEntityState SetTransform(
        Rac1MobyRuntimeInstance instance,
        RuntimeObjectTransform transform)
    {
        RequireOwned(instance);
        ArgumentNullException.ThrowIfNull(transform);
        if (transform.Matrix.Length != 16)
            throw new ArgumentException(
                "R&C1 Moby runtime transform must be a 4x4 affine matrix.",
                nameof(transform));

        instance.State = instance.State with
        {
            EntityState = instance.EntityState.WithTransform(transform),
        };
        return instance.EntityState;
    }

    public RuntimeEntityState SetPresence(
        Rac1MobyRuntimeInstance instance,
        RuntimeEntityPresence presence)
    {
        RequireOwned(instance);
        if (instance.State.IsTerminalized &&
            presence == RuntimeEntityPresence.Active)
            throw new InvalidOperationException(
                "A terminalized R&C1 Moby cannot be reactivated by presentation presence.");

        instance.State = instance.State with
        {
            EntityState = instance.EntityState.WithPresence(presence),
        };
        return instance.EntityState;
    }

    public Rac1MobyRuntimeState Terminalize(
        Rac1MobyRuntimeInstance instance)
    {
        RequireOwned(instance);
        instance.State = Rac1MobyRuntime.Terminalize(instance.State);
        return instance.State;
    }

    public Rac1MobyRuntimeState Terminalize(
        Rac1MobyRuntimeInstance instance,
        int nativeState)
    {
        RequireOwned(instance);
        instance.State = Rac1MobyRuntime.Terminalize(instance.State, nativeState);
        return instance.State;
    }

    public TOutput DispatchUpdate<TOutput>(RuntimeDynamicObject source, object facts)
    {
        _ = Require(source);
        if (!_controllers.TryGetValue(source.NativeClassId, out var controller))
            throw new NotSupportedException(
                $"R&C1 Moby class {source.NativeClassId} has no recovered runtime controller.");

        object result = controller.Update(source, facts);
        if (result is not TOutput typed)
            throw new InvalidOperationException(
                $"R&C1 Moby class {source.NativeClassId} returned {result?.GetType().Name ?? "null"}, expected {typeof(TOutput).Name}.");
        return typed;
    }

    public bool TryResolveDamageTarget(
        Rac1GameplayDamageEvent damage,
        out Rac1MobyRuntimeInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(damage);
        instance = null;
        if (damage.Target.Kind != Rac1GameplayEntityKind.Moby ||
            damage.Target.RuntimeId is < int.MinValue or > int.MaxValue)
            return false;

        var key = new Rac1MobyRuntimeKey(
            damage.Target.NativeClassId,
            checked((int)damage.Target.RuntimeId));
        return _instances.TryGetValue(key, out instance);
    }

    public bool CanDispatchDamage(Rac1GameplayDamageEvent damage) =>
        TryResolveDamageTarget(damage, out var instance) &&
        instance is not null &&
        CanDispatchDamage(instance.Source, damage);

    public bool TryDispatchDamage<TOutput>(
        Rac1GameplayDamageEvent damage,
        out TOutput? output)
    {
        output = default;
        if (!TryResolveDamageTarget(damage, out var instance) ||
            instance is null)
            return false;

        return TryDispatchDamage(instance.Source, damage, out output);
    }

    public TOutput DispatchDamage<TOutput>(Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (!TryResolveDamageTarget(damage, out var instance) ||
            instance is null)
            throw new InvalidOperationException(
                "R&C1 damage event target is not a registered runtime Moby.");

        return DispatchDamage<TOutput>(instance.Source, damage);
    }

    public bool CanDispatchDamage(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var instance = Require(source);
        if (!damage.Target.MatchesMoby(instance.Key))
            throw new ArgumentException(
                "R&C1 damage event target does not match the supplied runtime Moby.",
                nameof(damage));

        return _damageConsumers.TryGetValue(
                   source.NativeClassId,
                   out var consumer) &&
               consumer.CanApplyDamage(source, damage);
    }

    public bool TryDispatchDamage<TOutput>(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage,
        out TOutput? output)
    {
        output = default;
        if (!CanDispatchDamage(source, damage))
            return false;

        var consumer = _damageConsumers[source.NativeClassId];
        object result = consumer.ApplyDamage(source, damage);
        if (result is not TOutput typed)
            throw new InvalidOperationException(
                $"R&C1 Moby class {source.NativeClassId} damage consumer returned " +
                $"{result?.GetType().Name ?? "null"}, expected {typeof(TOutput).Name}.");

        output = typed;
        return true;
    }

    public TOutput DispatchDamage<TOutput>(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var instance = Require(source);
        if (!damage.Target.MatchesMoby(instance.Key))
            throw new ArgumentException(
                "R&C1 damage event target does not match the supplied runtime Moby.",
                nameof(damage));

        if (!_damageConsumers.TryGetValue(source.NativeClassId, out var consumer))
            throw new NotSupportedException(
                $"R&C1 Moby class {source.NativeClassId} has no recovered native damage consumer.");
        if (!consumer.CanApplyDamage(source, damage))
            throw new NotSupportedException(
                $"R&C1 Moby class {source.NativeClassId} does not admit this recovered native damage event.");

        object result = consumer.ApplyDamage(source, damage);
        if (result is not TOutput typed)
            throw new InvalidOperationException(
                $"R&C1 Moby class {source.NativeClassId} damage consumer returned {result?.GetType().Name ?? "null"}, expected {typeof(TOutput).Name}.");
        return typed;
    }

    private void RequireOwned(Rac1MobyRuntimeInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!_instances.TryGetValue(instance.Key, out var registered) ||
            !ReferenceEquals(instance, registered))
            throw new InvalidOperationException("R&C1 Moby runtime instance belongs to another session.");
    }
}
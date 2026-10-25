using OBP.Core;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Stable authored identity for one live UYA Moby. Native class plus authored
/// instance index is sufficient for runtime routing; class semantics stay local.
/// </summary>
public readonly record struct Rac3MobyRuntimeKey(int NativeClassId, int InstanceIndex);

public enum Rac3MobyLifecycleState
{
    Live,
    Terminalized,
}

public sealed record Rac3MobyRuntimeState(
    Rac3MobyRuntimeKey Key,
    RuntimeEntityState EntityState,
    Rac3MobyLifecycleState Lifecycle = Rac3MobyLifecycleState.Live)
{
    public bool IsTerminalized => Lifecycle == Rac3MobyLifecycleState.Terminalized;
}

/// <summary>
/// UYA-common lifetime projection. No native UYA terminal-state value is claimed
/// here; class controllers may add one only after retail evidence establishes it.
/// </summary>
public static class Rac3MobyRuntime
{
    public static Rac3MobyRuntimeState Create(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(current);
        if (source.SourceGame != ObpSourceGame.Rac3)
            throw new ArgumentException("Source is not a UYA Moby.", nameof(source));
        current.EnsureMatches(source);
        return new Rac3MobyRuntimeState(
            new Rac3MobyRuntimeKey(source.NativeClassId, source.InstanceIndex),
            current);
    }

    public static Rac3MobyRuntimeState Terminalize(Rac3MobyRuntimeState current) =>
        current with
        {
            EntityState = current.EntityState.WithPresence(RuntimeEntityPresence.Inactive),
            Lifecycle = Rac3MobyLifecycleState.Terminalized,
        };
}
/// <summary>
/// Class-local update hook. Unknown UYA classes remain inert unless retail
/// evidence registers a controller for that exact native class.
/// </summary>
public interface IRac3MobyClassController
{
    int NativeClassId { get; }
    object Update(RuntimeDynamicObject source, object facts);
}

/// <summary>
/// One mutable live view of an immutable authored UYA Moby definition. PVar bytes
/// are copied into source-owned runtime storage so later class controllers can
/// mutate live state without modifying the imported retail definition.
/// </summary>
public sealed class Rac3MobyRuntimeInstance
{
    private readonly byte[] _pvar;

    internal Rac3MobyRuntimeInstance(
        RuntimeDynamicObject source,
        ReadOnlySpan<byte> pvar,
        Rac3MobyRuntimeState state)
    {
        Source = source;
        _pvar = pvar.ToArray();
        State = state;
    }

    public RuntimeDynamicObject Source { get; }
    public Rac3MobyRuntimeKey Key => State.Key;
    public RuntimeEntityIdentity Identity => State.EntityState.Identity;
    public ReadOnlyMemory<byte> PVar => _pvar;
    public Rac3MobyRuntimeState State { get; internal set; }
    public RuntimeEntityState EntityState => State.EntityState;
    public bool IsActive =>
        !State.IsTerminalized &&
        EntityState.Presentation.Presence == RuntimeEntityPresence.Active;

    internal byte[] MutablePVar => _pvar;
}

/// <summary>
/// Class-local native damage consumer. Registration is independent from authored
/// population so unknown UYA classes remain inert rather than gaining policy
/// from another Ratchet game.
/// </summary>
public interface IRac3MobyDamageConsumer
{
    int NativeClassId { get; }

    bool CanApplyDamage(
        RuntimeDynamicObject source,
        Rac3GameplayDamageEvent damage);

    object ApplyDamage(
        RuntimeDynamicObject source,
        Rac3GameplayDamageEvent damage);
}

/// <summary>
/// Reusable UYA live-Moby store. Unknown classes are admitted as authored
/// entities but gain no guessed behavior. Class-specific controllers can build
/// on this store once their native semantics are recovered.
/// </summary>
public sealed class Rac3MobyRuntimeSession
{
    public const string InstancePayloadFormat = "rac3-moby-instance-gc-layout-compat";
    public const string PVarPayloadFormat = "rac3-pvar-gc-layout-compat";
    private readonly Dictionary<Rac3MobyRuntimeKey, Rac3MobyRuntimeInstance> _instances = [];
    private readonly Dictionary<int, IRac3MobyClassController> _controllers = [];
    private readonly Dictionary<int, IRac3MobyDamageConsumer> _damageConsumers = [];

    public int RegisteredCount => _instances.Count;
    public IReadOnlyCollection<Rac3MobyRuntimeInstance> Instances => _instances.Values;

    public void RegisterController(IRac3MobyClassController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (!_controllers.TryAdd(controller.NativeClassId, controller))
            throw new InvalidOperationException(
                $"UYA Moby class {controller.NativeClassId} already has a runtime controller.");
    }

    public void RegisterDamageConsumer(IRac3MobyDamageConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (!_damageConsumers.TryAdd(consumer.NativeClassId, consumer))
            throw new InvalidOperationException(
                $"UYA Moby class {consumer.NativeClassId} already has a native damage consumer.");
    }

    public Rac3MobyRuntimeInstance Register(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var pvar = source.NativePayloads?
            .SingleOrDefault(payload => payload.Format == PVarPayloadFormat)?.Data
            ?? Array.Empty<byte>();
        var state = Rac3MobyRuntime.Create(source, RuntimeEntityState.FromAuthored(source));
        var instance = new Rac3MobyRuntimeInstance(source, pvar, state);
        if (!_instances.TryAdd(state.Key, instance))
            throw new InvalidOperationException(
                $"UYA Moby class {source.NativeClassId} instance {source.InstanceIndex} is already registered.");
        return instance;
    }
    public void RegisterWorld(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Game != ObpSourceGame.Rac3)
            throw new ArgumentException("World is not a UYA world.", nameof(world));
        foreach (var source in world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            Register(source);
    }

    public bool TryGet(Rac3MobyRuntimeKey key, out Rac3MobyRuntimeInstance? instance) =>
        _instances.TryGetValue(key, out instance);

    public Rac3MobyRuntimeInstance Require(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = new Rac3MobyRuntimeKey(source.NativeClassId, source.InstanceIndex);
        if (!_instances.TryGetValue(key, out var instance))
            throw new InvalidOperationException(
                $"UYA Moby class {source.NativeClassId} instance {source.InstanceIndex} is not registered.");
        instance.EntityState.EnsureMatches(source);
        return instance;
    }

    public Rac3MobyRuntimeState Terminalize(Rac3MobyRuntimeInstance instance)
    {
        RequireOwned(instance);
        instance.State = Rac3MobyRuntime.Terminalize(instance.State);
        return instance.State;
    }

    public TOutput DispatchUpdate<TOutput>(
        RuntimeDynamicObject source,
        object facts)
    {
        var instance = Require(source);
        if (!instance.IsActive)
            throw new InvalidOperationException(
                "A terminalized UYA Moby cannot run a class update.");
        if (!_controllers.TryGetValue(source.NativeClassId, out var controller))
            throw new NotSupportedException(
                $"UYA Moby class {source.NativeClassId} has no recovered runtime controller.");

        object result = controller.Update(source, facts);
        if (result is not TOutput typed)
            throw new InvalidOperationException(
                $"UYA Moby class {source.NativeClassId} returned " +
                $"{result?.GetType().Name ?? "null"}, expected {typeof(TOutput).Name}.");
        return typed;
    }

    public bool TryResolveDamageTarget(
        Rac3GameplayDamageEvent damage,
        out Rac3MobyRuntimeInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(damage);
        instance = null;
        if (damage.Target.Kind != Rac3GameplayEntityKind.Moby ||
            damage.Target.RuntimeId is < int.MinValue or > int.MaxValue)
            return false;

        var key = new Rac3MobyRuntimeKey(
            damage.Target.NativeClassId,
            checked((int)damage.Target.RuntimeId));
        return _instances.TryGetValue(key, out instance);
    }

    public bool CanDispatchDamage(Rac3GameplayDamageEvent damage)
    {
        if (!TryResolveDamageTarget(damage, out var instance) ||
            instance is null ||
            !instance.IsActive)
            return false;

        return _damageConsumers.TryGetValue(
                   instance.Source.NativeClassId,
                   out var consumer) &&
               consumer.CanApplyDamage(instance.Source, damage);
    }

    public bool TryDispatchDamage<TOutput>(
        Rac3GameplayDamageEvent damage,
        out TOutput? output)
    {
        output = default;
        if (!CanDispatchDamage(damage) ||
            !TryResolveDamageTarget(damage, out var instance) ||
            instance is null)
            return false;

        object result = _damageConsumers[instance.Source.NativeClassId]
            .ApplyDamage(instance.Source, damage);
        if (result is not TOutput typed)
            throw new InvalidOperationException(
                $"UYA Moby class {instance.Source.NativeClassId} damage consumer returned " +
                $"{result?.GetType().Name ?? "null"}, expected {typeof(TOutput).Name}.");

        output = typed;
        return true;
    }

    public TOutput DispatchDamage<TOutput>(Rac3GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (!TryResolveDamageTarget(damage, out var instance) ||
            instance is null)
            throw new InvalidOperationException(
                "UYA damage event target is not a registered runtime Moby.");
        if (!instance.IsActive)
            throw new InvalidOperationException(
                "UYA damage event target is no longer an active runtime Moby.");
        if (!_damageConsumers.TryGetValue(instance.Source.NativeClassId, out var consumer))
            throw new NotSupportedException(
                $"UYA Moby class {instance.Source.NativeClassId} has no recovered native damage consumer.");
        if (!consumer.CanApplyDamage(instance.Source, damage))
            throw new NotSupportedException(
                $"UYA Moby class {instance.Source.NativeClassId} does not admit this recovered native damage event.");

        object result = consumer.ApplyDamage(instance.Source, damage);
        if (result is not TOutput typed)
            throw new InvalidOperationException(
                $"UYA Moby class {instance.Source.NativeClassId} damage consumer returned " +
                $"{result?.GetType().Name ?? "null"}, expected {typeof(TOutput).Name}.");
        return typed;
    }

    public RuntimeEntityState SetTransform(
        Rac3MobyRuntimeInstance instance,
        RuntimeObjectTransform transform)
    {
        RequireOwned(instance);
        ArgumentNullException.ThrowIfNull(transform);
        if (transform.Matrix.Length != 16)
            throw new ArgumentException(
                "UYA Moby runtime transform must be a 4x4 affine matrix.",
                nameof(transform));
        instance.State = instance.State with
        {
            EntityState = instance.EntityState.WithTransform(transform),
        };
        return instance.EntityState;
    }

    private void RequireOwned(Rac3MobyRuntimeInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!_instances.TryGetValue(instance.Key, out var registered) ||
            !ReferenceEquals(instance, registered))
            throw new InvalidOperationException(
                "UYA Moby runtime instance belongs to another session.");
    }
}

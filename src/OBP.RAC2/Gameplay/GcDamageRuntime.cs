using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

public enum GcGameplayEntityKind
{
    Player,
    Moby,
}

/// <summary>
/// Stable GC gameplay identity used by source-game event transport.
/// Moby runtime ids are authored instance indices.
/// </summary>
public readonly record struct GcGameplayEntityRef(
    GcGameplayEntityKind Kind,
    int NativeClassId,
    int RuntimeId)
{
    public static GcGameplayEntityRef Player { get; } =
        new(GcGameplayEntityKind.Player, NativeClassId: 0, RuntimeId: 0);

    public static GcGameplayEntityRef Moby(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac2")
        {
            throw new ArgumentException(
                "Gameplay entity is not owned by Going Commando.",
                nameof(source));
        }

        return new(
            GcGameplayEntityKind.Moby,
            source.NativeClassId,
            source.InstanceIndex);
    }

    public bool Matches(RuntimeDynamicObject source) =>
        Kind == GcGameplayEntityKind.Moby
        && source.SourceGame == "rac2"
        && NativeClassId == source.NativeClassId
        && RuntimeId == source.InstanceIndex;
}

/// <summary>
/// One recovered GC collision-damage event with explicit source and target.
/// Consequences remain class-local and are not applied by transport.
/// </summary>
public sealed record GcGameplayDamageEvent(
    GcGameplayEntityRef Source,
    GcGameplayEntityRef Target,
    GcCollisionDamage Damage);

public sealed record GcGameplayDamageDispatch(
    long Sequence,
    GcGameplayDamageEvent Damage);

/// <summary>
/// Sequenced, synchronous GC damage transport. Publishing does not change
/// entity lifetime or presentation; RAC2 class controllers consume events.
/// </summary>
public sealed class GcDamageTransportSession
{
    private long _sequence;

    public long Sequence => _sequence;

    public event Action<GcGameplayDamageDispatch>? Published;

    public GcGameplayDamageDispatch Publish(GcGameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var dispatch = new GcGameplayDamageDispatch(
            checked(++_sequence),
            damage);
        Published?.Invoke(dispatch);
        return dispatch;
    }
}

/// <summary>
/// Source-game construction and consequence routing for the currently recovered
/// GC damage slice.
/// </summary>
public static class GcDamageRuntime
{
    public static GcGameplayDamageEvent FromPlayerState20(
        RuntimeDynamicObject target) =>
        new(
            GcGameplayEntityRef.Player,
            GcGameplayEntityRef.Moby(target),
            GcPlayerAttackDamage.State20);

    public static GcClass500LifecycleResult? ApplyClass500Consequence(
        RuntimeDynamicObject target,
        RuntimeEntityState current,
        GcGameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(damage);

        if (!damage.Target.Matches(target))
        {
            throw new InvalidOperationException(
                $"Damage target does not match {target.InteractionId}.");
        }

        if (!GcCrateInteraction.ShouldBreakClass500(damage.Damage))
        {
            return null;
        }

        return GcClass500Lifecycle.ApplyRecoveredBreak(target, current);
    }
}

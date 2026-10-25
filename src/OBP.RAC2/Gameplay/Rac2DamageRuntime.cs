using OBP.Core;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

public enum Rac2GameplayEntityKind
{
    Player,
    Moby,
}

/// <summary>
/// Stable GC gameplay identity used by source-game event transport.
/// Moby runtime ids are authored instance indices.
/// </summary>
public readonly record struct Rac2GameplayEntityRef(
    Rac2GameplayEntityKind Kind,
    int NativeClassId,
    int RuntimeId)
{
    public static Rac2GameplayEntityRef Player { get; } =
        new(Rac2GameplayEntityKind.Player, NativeClassId: 0, RuntimeId: 0);

    public static Rac2GameplayEntityRef Moby(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != ObpSourceGame.Rac2)
        {
            throw new ArgumentException(
                "Gameplay entity is not owned by Going Commando.",
                nameof(source));
        }

        return new(
            Rac2GameplayEntityKind.Moby,
            source.NativeClassId,
            source.InstanceIndex);
    }

    public bool Matches(RuntimeDynamicObject source) =>
        Kind == Rac2GameplayEntityKind.Moby
        && source.SourceGame == ObpSourceGame.Rac2
        && NativeClassId == source.NativeClassId
        && RuntimeId == source.InstanceIndex;
}

/// <summary>
/// One recovered GC collision-damage event with explicit source and target.
/// Consequences remain class-local and are not applied by transport.
/// </summary>
public sealed record Rac2GameplayDamageEvent(
    Rac2GameplayEntityRef Source,
    Rac2GameplayEntityRef Target,
    Rac2CollisionDamage Damage);

public sealed record Rac2GameplayDamageDispatch(
    long Sequence,
    Rac2GameplayDamageEvent Damage);

/// <summary>
/// Sequenced, synchronous GC damage transport. Publishing does not change
/// entity lifetime or presentation; RAC2 class controllers consume events.
/// </summary>
public sealed class Rac2DamageTransportSession
{
    private long _sequence;

    public long Sequence => _sequence;

    public event Action<Rac2GameplayDamageDispatch>? Published;

    public Rac2GameplayDamageDispatch Publish(Rac2GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var dispatch = new Rac2GameplayDamageDispatch(
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
public static class Rac2DamageRuntime
{
    public static Rac2GameplayDamageEvent FromPlayerState20(
        RuntimeDynamicObject target) =>
        new(
            Rac2GameplayEntityRef.Player,
            Rac2GameplayEntityRef.Moby(target),
            Rac2PlayerAttackDamage.State20);

    public static Rac2Class500LifecycleResult? ApplyClass500Consequence(
        RuntimeDynamicObject target,
        RuntimeEntityState current,
        Rac2GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(damage);

        if (!damage.Target.Matches(target))
        {
            throw new InvalidOperationException(
                $"Damage target does not match {target.InteractionId}.");
        }

        if (!Rac2CrateInteraction.ShouldBreakClass500(damage.Damage))
        {
            return null;
        }

        return Rac2Class500Lifecycle.ApplyRecoveredBreak(target, current);
    }
}

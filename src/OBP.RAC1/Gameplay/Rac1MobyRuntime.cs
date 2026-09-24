using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Stable identity for one live R&C1 Moby runtime entry. The engine-common
/// contract uses native class plus authored instance index; UID semantics stay
/// class-specific.
/// </summary>
public readonly record struct Rac1MobyRuntimeKey(int NativeClassId, int InstanceIndex);

/// <summary>
/// Recovered engine-common live-Moby state boundary. This deliberately models
/// only the native state byte and the independently justified neutral lifetime
/// projection. Health, targeting and locomotion policy remain class-owned.
/// </summary>
public static class Rac1MobyRuntime
{
    public const int TerminalNativeStateFd = 0xfd;
    public const int TerminalNativeStateFe = 0xfe;

    public static Rac1MobyRuntimeState Create(
        RuntimeDynamicObject source,
        int nativeState,
        RuntimeEntityState current)
    {
        if (source.SourceGame != "rac1")
            throw new ArgumentException("Source is not an R&C1 Moby.", nameof(source));
        current.EnsureMatches(source);
        ValidateNativeState(nativeState);
        if (IsTerminalState(nativeState))
            current = current.WithPresence(RuntimeEntityPresence.Inactive);

        return new Rac1MobyRuntimeState(
            new Rac1MobyRuntimeKey(source.NativeClassId, source.InstanceIndex),
            nativeState,
            current,
            IsTerminalState(nativeState)
                ? Rac1MobyLifecycleState.Terminalized
                : Rac1MobyLifecycleState.Live,
            IsTerminalState(nativeState) ? nativeState : null);
    }

    public static Rac1MobyRuntimeState WithNativeState(
        Rac1MobyRuntimeState current,
        int nativeState)
    {
        ValidateNativeState(nativeState);
        if (IsTerminalState(nativeState))
            return Terminalize(current, nativeState);
        if (current.IsTerminalized)
            throw new InvalidOperationException(
                "A terminalized R&C1 Moby cannot re-enter a live native state.");
        return current with { NativeState = nativeState };
    }

    public static Rac1MobyRuntimeState Terminalize(
        Rac1MobyRuntimeState current)
    {
        return current with
        {
            EntityState = current.EntityState.WithPresence(RuntimeEntityPresence.Inactive),
            Lifecycle = Rac1MobyLifecycleState.Terminalized,
            NativeTerminalState = null,
        };
    }

    public static Rac1MobyRuntimeState Terminalize(
        Rac1MobyRuntimeState current,
        int nativeState)
    {
        if (!IsTerminalState(nativeState))
            throw new ArgumentOutOfRangeException(nameof(nativeState));

        return Terminalize(current) with
        {
            NativeState = nativeState,
            NativeTerminalState = nativeState,
        };
    }

    public static bool IsTerminalState(int nativeState) =>
        nativeState is TerminalNativeStateFd or TerminalNativeStateFe;

    private static void ValidateNativeState(int nativeState)
    {
        if (nativeState is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(nativeState));
    }
}

public enum Rac1MobyLifecycleState
{
    Live,
    Terminalized,
}

public sealed record Rac1MobyRuntimeState(
    Rac1MobyRuntimeKey Key,
    int NativeState,
    RuntimeEntityState EntityState,
    Rac1MobyLifecycleState Lifecycle = Rac1MobyLifecycleState.Live,
    int? NativeTerminalState = null)
{
    public bool IsTerminalized => Lifecycle == Rac1MobyLifecycleState.Terminalized;
}

/// <summary>
/// Copyright-safe envelope for the recovered native damage transport inputs.
/// Victim identity is attached only when a class runtime consumes the record.
/// </summary>
public readonly record struct Rac1NativeDamageEnvelope
{
    public Rac1NativeDamageEnvelope(double nativeDamage, uint nativeDamageFlags)
    {
        if (!double.IsFinite(nativeDamage))
            throw new ArgumentOutOfRangeException(nameof(nativeDamage));

        NativeDamage = nativeDamage;
        NativeDamageFlags = nativeDamageFlags;
    }

    public double NativeDamage { get; }
    public uint NativeDamageFlags { get; }
}

public interface IRac1MobyHostIntent
{
}

public interface IRac1MobyHostEvent
{
}

/// <summary>
/// Native state-byte transition surfaced to a host without assigning semantics
/// that belong to a particular Moby class.
/// </summary>
public sealed record Rac1MobyNativeStateChangedEvent(
    Rac1MobyRuntimeKey Key,
    int NativeStateBefore,
    int NativeStateAfter) : IRac1MobyHostEvent;

/// <summary>
/// One recovered native damage envelope consumed by a class runtime.
/// The common engine transports the record; class code owns its consequence.
/// </summary>
public sealed record Rac1MobyDamageConsumedEvent(
    Rac1MobyRuntimeKey Victim,
    Rac1NativeDamageEnvelope Damage) : IRac1MobyHostEvent;

/// <summary>
/// Common terminalization projected to neutral inactive presence. The exact
/// native 0xfd/0xfe state is optional when the recovered terminalizer is proven
/// but its pool-side selector is not. This does not claim immediate deallocation.
/// </summary>
public sealed record Rac1MobyTerminalizedEvent(
    Rac1MobyRuntimeKey Key,
    int? NativeTerminalState) : IRac1MobyHostEvent
{
    public bool HasRecoveredNativeTerminalState => NativeTerminalState.HasValue;
}

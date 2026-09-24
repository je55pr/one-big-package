using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Player;

/// <summary>
/// Native R&C1 Ratchet action domain recovered as a 131-slot dispatcher.
/// Unknown slots remain numeric and gain no inferred semantics.
/// </summary>
public static class Rac1PlayerActionDomain
{
    public const int ActionCount = 131;
    public const int Neutral = 0x00;
    public const int Crouch = 0x04;
    public const int Jump = 0x07;
    public const int Wrench = 0x13;
    public const int FirstRangedFire = 0x23;
    public const int IceSlide = 0x2f;
    public const int Magneboot = 0x3f;
    public const int Mud = 0x68;
    public const int Drown = 0x6a;
    public const int EnvironmentalFallDeath = 0x77;

    public static Rac1PlayerActionDescriptor Describe(int nativeState)
    {
        Validate(nativeState);
        return new Rac1PlayerActionDescriptor(
            nativeState,
            RecoveredName(nativeState));
    }
    public static IReadOnlyList<Rac1PlayerActionDescriptor> Catalogue { get; } =
        Enumerable.Range(0, ActionCount).Select(Describe).ToArray();

    public static void Validate(int nativeState)
    {
        if (nativeState is < 0 or >= ActionCount)
            throw new ArgumentOutOfRangeException(nameof(nativeState));
    }

    private static string? RecoveredName(int nativeState) => nativeState switch
    {
        Neutral => "neutral",
        Crouch => "crouch",
        Jump => "jump",
        Wrench => "wrench",
        FirstRangedFire => "first-ranged-fire",
        IceSlide => "ice-slide",
        Magneboot => "magneboot",
        Mud => "mud",
        Drown => "drown",
        EnvironmentalFallDeath => "environmental-fall-death",
        _ => null,
    };
}

public sealed record Rac1PlayerActionDescriptor(
    int NativeState,
    string? RecoveredName)
{
    public string DisplayName =>
        RecoveredName ?? $"state-0x{NativeState:x2}";
}
public enum Rac1OrdinaryMovementPolicy
{
    Unknown,
    Allowed,
    Suppressed,
}

/// <summary>
/// Current native player-action state. Entry-owned sequence selection is kept
/// separate from per-frame update accounting.
/// </summary>
public sealed record Rac1PlayerActionSnapshot(
    int CurrentNativeState,
    int? PreviousNativeState,
    Rac1PlayerActionDescriptor Descriptor,
    int? NativeSequence,
    int? NativeSequenceFrame,
    Rac1OrdinaryMovementPolicy OrdinaryMovement,
    long EntryGeneration,
    long UpdateGeneration)
{
    public bool AllowsOrdinaryCharacterMovement =>
        OrdinaryMovement == Rac1OrdinaryMovementPolicy.Allowed;
}

public interface IRac1PlayerActionHandler
{
    int NativeState { get; }

    Rac1PlayerActionEntry Initialize(
        Rac1PlayerActionSnapshot previous);

    Rac1PlayerActionSnapshot Update(
        Rac1PlayerActionSnapshot current);
}
public sealed record Rac1PlayerActionEntry(
    int? NativeSequence,
    int? NativeSequenceFrame,
    Rac1OrdinaryMovementPolicy OrdinaryMovement);

/// <summary>
/// Shared R&C1 player action lifecycle. Native entry initialization and
/// per-frame update are intentionally distinct dispatches.
/// </summary>
public sealed class Rac1PlayerActionRuntimeSession
{
    private readonly Dictionary<int, IRac1PlayerActionHandler> _handlers = [];
    private Rac1PlayerActionSnapshot _current;

    public Rac1PlayerActionRuntimeSession()
    {
        RegisterHandler(new NeutralActionHandler());
        RegisterHandler(new WrenchActionHandler());
        RegisterHandler(new FirstRangedFireActionHandler());
        RegisterHandler(new EnvironmentalFallDeathActionHandler());
        _current = new Rac1PlayerActionSnapshot(
            Rac1PlayerActionDomain.Neutral,
            null,
            Rac1PlayerActionDomain.Describe(Rac1PlayerActionDomain.Neutral),
            null,
            null,
            Rac1OrdinaryMovementPolicy.Allowed,
            EntryGeneration: 0,
            UpdateGeneration: 0);
    }

    public Rac1PlayerActionSnapshot Probe() => _current;
    public void RegisterHandler(IRac1PlayerActionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Rac1PlayerActionDomain.Validate(handler.NativeState);
        if (!_handlers.TryAdd(handler.NativeState, handler))
            throw new InvalidOperationException(
                $"R&C1 player action 0x{handler.NativeState:x2} already has a lifecycle handler.");
    }

    public Rac1PlayerActionSnapshot ApplyWeaponUseAdmission(
        Rac1WeaponUseAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);

        if (!admission.Accepted)
        {
            if (admission.NativePlayerActionState is not null ||
                admission.NativePlayerSequenceId is not null)
                throw new InvalidDataException(
                    "Rejected R&C1 weapon use cannot carry player action or sequence selectors.");

            return _current;
        }

        if (admission.NativePlayerActionState is not int nativeState ||
            admission.NativePlayerSequenceId is not int nativeSequence)
            throw new InvalidDataException(
                "Accepted R&C1 weapon use requires both recovered player action and sequence selectors.");

        Rac1PlayerActionDomain.Validate(nativeState);
        if (!_handlers.TryGetValue(nativeState, out var handler))
            throw new NotSupportedException(
                $"R&C1 player action 0x{nativeState:x2} has no recovered lifecycle handler.");

        Rac1PlayerActionEntry expected = handler.Initialize(_current);
        if (expected.NativeSequence != nativeSequence)
            throw new InvalidDataException(
                $"R&C1 weapon admission sequence {nativeSequence} does not match action " +
                $"0x{nativeState:x2} entry sequence {expected.NativeSequence?.ToString() ?? "none"}.");

        return EnterState(nativeState);
    }

    public Rac1PlayerActionSnapshot EnterState(int nativeState)
    {
        Rac1PlayerActionDomain.Validate(nativeState);
        if (_current.CurrentNativeState == nativeState)
            return _current;

        Rac1PlayerActionEntry entry = _handlers.TryGetValue(nativeState, out var handler)
            ? handler.Initialize(_current)
            : new Rac1PlayerActionEntry(
                null,
                null,
                Rac1OrdinaryMovementPolicy.Unknown);

        _current = new Rac1PlayerActionSnapshot(
            nativeState,
            _current.CurrentNativeState,
            Rac1PlayerActionDomain.Describe(nativeState),
            entry.NativeSequence,
            entry.NativeSequenceFrame,
            entry.OrdinaryMovement,
            _current.EntryGeneration + 1,
            UpdateGeneration: 0);
        return _current;
    }
    public Rac1PlayerActionSnapshot Update()
    {
        if (_handlers.TryGetValue(_current.CurrentNativeState, out var handler))
            _current = handler.Update(_current);

        _current = _current with
        {
            UpdateGeneration = _current.UpdateGeneration + 1,
        };
        return _current;
    }

    private sealed class NeutralActionHandler : IRac1PlayerActionHandler
    {
        public int NativeState => Rac1PlayerActionDomain.Neutral;

        public Rac1PlayerActionEntry Initialize(Rac1PlayerActionSnapshot previous) =>
            new(null, null, Rac1OrdinaryMovementPolicy.Allowed);

        public Rac1PlayerActionSnapshot Update(Rac1PlayerActionSnapshot current) => current;
    }

    private sealed class WrenchActionHandler : IRac1PlayerActionHandler
    {
        public int NativeState => Rac1PlayerActionDomain.Wrench;

        public Rac1PlayerActionEntry Initialize(Rac1PlayerActionSnapshot previous) =>
            new(
                Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
                0,
                previous.OrdinaryMovement);

        public Rac1PlayerActionSnapshot Update(Rac1PlayerActionSnapshot current) => current;
    }

    private sealed class FirstRangedFireActionHandler : IRac1PlayerActionHandler
    {
        public int NativeState => Rac1PlayerActionDomain.FirstRangedFire;

        public Rac1PlayerActionEntry Initialize(Rac1PlayerActionSnapshot previous) =>
            new(
                Rac1RatchetSequenceSelection.FirstRangedFireSequenceId,
                0,
                previous.OrdinaryMovement);

        public Rac1PlayerActionSnapshot Update(Rac1PlayerActionSnapshot current) => current;
    }

    private sealed class EnvironmentalFallDeathActionHandler : IRac1PlayerActionHandler
    {
        public int NativeState => Rac1PlayerActionDomain.EnvironmentalFallDeath;

        public Rac1PlayerActionEntry Initialize(Rac1PlayerActionSnapshot previous) =>
            new(
                Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId,
                0,
                Rac1OrdinaryMovementPolicy.Suppressed);

        public Rac1PlayerActionSnapshot Update(Rac1PlayerActionSnapshot current) => current;
    }
}

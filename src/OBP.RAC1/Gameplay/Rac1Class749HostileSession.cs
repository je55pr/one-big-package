using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Deterministic session for the recovered class-749 hostile family.
/// Entries are keyed by native class plus authored instance index; UID semantics are not used.
/// </summary>
public sealed class Rac1Class749HostileSession
{
    private readonly Dictionary<Rac1Class749Key, Entry> _entries = [];

    public int RegisteredCount => _entries.Count;

    public Rac1Class749HostProbe Register(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        var authored = Rac1Class749Hostile.ReadAuthored(source)
            ?? throw new ArgumentException("Source is not an R&C1 class-749 hostile.", nameof(source));
        current.EnsureMatches(source);

        byte[] pvar = Rac1Class749Hostile.RequirePVar(source);
        Rac1Class749Hostile.WriteHomePosition(
            pvar, Rac1Class749Hostile.ReadAuthoredPosition(source));

        return RegisterCore(
            source,
            authored.Key,
            current,
            pvar,
            activationGroup: null,
            Rac1Class749Hostile.TargetSearchNativeState);
    }

    public Rac1Class749HostProbe RegisterVeldinPlacement(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        var authored = Rac1Class749Hostile.ReadAuthored(source)
            ?? throw new ArgumentException("Source is not an R&C1 class-749 hostile.", nameof(source));
        if (!Rac1Class749VeldinPopulation.TryGetActivationGroup(source.InstanceIndex, out int group))
            throw new ArgumentException("Source is not a recovered Veldin class-749 placement.", nameof(source));
        current.EnsureMatches(source);

        byte[] pvar = Rac1Class749Hostile.RequirePVar(source);
        Rac1Class749Hostile.WriteHomePosition(
            pvar, Rac1Class749Hostile.ReadAuthoredPosition(source));
        Rac1Class749VeldinPopulation.ValidateAuthoredFields(source, pvar);

        return RegisterCore(
            source,
            authored.Key,
            current,
            pvar,
            group,
            Rac1Class749VeldinPopulation.InitialNativeState(source, pvar));
    }

    private Rac1Class749HostProbe RegisterCore(
        RuntimeDynamicObject source,
        Rac1Class749Key key,
        RuntimeEntityState current,
        byte[] pvar,
        int? activationGroup,
        int initialState)
    {
        var entry = new Entry(
            pvar,
            activationGroup,
            Rac1MobyRuntime.Create(source, initialState, current));
        if (!_entries.TryAdd(key, entry))
            throw new InvalidOperationException(
                $"R&C1 class-749 instance {source.InstanceIndex} is already registered.");
        return Snapshot(key, entry);
    }

    public Rac1Class749HostProbe Probe(RuntimeDynamicObject source)
    {
        var (key, entry) = RequireEntry(source);
        return Snapshot(key, entry);
    }

    public Rac1Class749HostProbe Step(
        RuntimeDynamicObject source,
        Rac1Class749TargetFacts target)
    {
        ValidateTargetFacts(target);

        var (key, entry) = RequireEntry(source);
        int nativeStateBefore = entry.NativeState;
        int statusSentinel = UpdateTargetDescriptor(entry, target);
        Rac1Class749AttackEvent? attack = null;
        IReadOnlyList<IRac1MobyHostIntent> hostIntents =
            HostIntentsForDispatch(nativeStateBefore, entry.PVar);

        switch (entry.NativeState)
        {
            case Rac1Class749Hostile.LinkedObjectNativeState:
                if (target.LinkedObjectTerminal)
                {
                    entry.NativeState = Rac1Class749Hostile.ReturnHomeNativeState;
                    SetSequence(entry, Rac1Class749Hostile.StateSixOrEightSequenceId);
                }
                break;
            case Rac1Class749Hostile.TargetSearchNativeState:
                if (statusSentinel != Rac1Class749Hostile.StatusSentinelTwo)
                {
                    entry.NativeState = Rac1Class749Hostile.TargetedNativeState;
                    RandomizeActivationYaw(entry);
                }
                break;

            case Rac1Class749Hostile.TargetedNativeState:
                if (statusSentinel == Rac1Class749Hostile.StatusSentinelTwo)
                {
                    entry.NativeState = Rac1Class749Hostile.ReturnHomeNativeState;
                    SetSequence(entry, Rac1Class749Hostile.StateSixOrEightSequenceId);
                }
                else if (target.Distance < Rac1Class749Hostile.AttackDistanceExclusive &&
                         target.FacingError < Rac1Class749Hostile.AttackFacingErrorExclusive)
                {
                    entry.NativeState = Rac1Class749Hostile.AttackNativeState;
                    SetSequence(entry, Rac1Class749Hostile.AttackSequenceId);
                }
                break;

            case Rac1Class749Hostile.AttackNativeState:
                if (statusSentinel == Rac1Class749Hostile.StatusSentinelTwo)
                {
                    entry.NativeState = Rac1Class749Hostile.ReturnHomeNativeState;
                    SetSequence(entry, Rac1Class749Hostile.StateSixOrEightSequenceId);
                }
                else if (target.Distance > Rac1Class749Hostile.AttackRetainDistanceInclusive ||
                         target.FacingError >= Rac1Class749Hostile.AttackFacingErrorExclusive)
                {
                    entry.NativeState = Rac1Class749Hostile.TargetedNativeState;
                    SetSequence(entry, Rac1Class749Hostile.StateSixOrEightSequenceId);
                }
                else
                {
                    attack = AdvanceAttackSequence(entry);
                }
                break;

            case Rac1Class749Hostile.ReturnHomeNativeState:
                double homeDistance = target.CurrentPosition.DistanceTo(
                    Rac1Class749Hostile.ReadHomePosition(entry.PVar));
                if (homeDistance < Rac1Class749Hostile.HomeDistanceExclusive)
                {
                    entry.NativeState = Rac1Class749Hostile.TargetSearchNativeState;
                    SetSequence(entry, Rac1Class749Hostile.StateFiveSequenceId);
                }
                else if (statusSentinel != Rac1Class749Hostile.StatusSentinelTwo)
                {
                    entry.NativeState = Rac1Class749Hostile.TargetedNativeState;
                    SetSequence(entry, Rac1Class749Hostile.StateSixOrEightSequenceId);
                }
                break;
        }

        var hostEvents = HostEventsForStep(
            entry.RuntimeState.Key,
            nativeStateBefore,
            entry.NativeState,
            attack);
        return Snapshot(key, entry, attack, hostIntents, hostEvents);
    }

    public Rac1Class749HostProbe ApplyWrenchDamage(
        RuntimeDynamicObject source,
        Rac1WrenchDamageResult damage)
    {
        if (damage.ContactPath != Rac1WrenchContactPath.HostPolicyAdmission ||
            damage.NativeDamage != Rac1WrenchCombatController.RepresentativeDamage ||
            damage.NativeDamageFlags != Rac1WrenchCombatController.RepresentativeDamageFlags ||
            damage.BoltCrateBreak is not null)
            throw new NotSupportedException(
                "Only the bounded host-admitted wrench stimulus is supported for class 749.");

        return ApplyRepresentativeDamage(source, damage.DamageEnvelope, "host-admitted wrench");
    }

    private Rac1Class749HostProbe ApplyRepresentativeDamage(
        RuntimeDynamicObject source,
        Rac1NativeDamageEnvelope damage,
        string sourceLabel)
    {
        var (key, entry) = RequireEntry(source);
        int nativeStateBefore = entry.NativeState;
        float healthBefore = Rac1Class749Hostile.ReadHealth(entry.PVar);
        if (healthBefore != 1f)
            throw new NotSupportedException(
                $"R&C1 class-749 {sourceLabel} consequence is proven only for representative health 1.0, not {healthBefore}.");

        float healthAfter = healthBefore - checked((float)damage.NativeDamage);
        if (healthAfter != 0f)
            throw new InvalidOperationException("Representative class-749 damage did not produce health 0.0.");
        Rac1Class749Hostile.WriteHealth(entry.PVar, healthAfter);
        entry.NativeState = Rac1Class749Hostile.DamageNativeState;
        entry.NativeSequence = null;
        entry.NativeSequenceUpdate = 0;

        IReadOnlyList<IRac1MobyHostEvent> hostEvents =
        [
            new Rac1MobyDamageConsumedEvent(entry.RuntimeState.Key, damage),
            new Rac1MobyNativeStateChangedEvent(
                entry.RuntimeState.Key,
                nativeStateBefore,
                entry.NativeState),
        ];
        return Snapshot(key, entry, hostEvents: hostEvents);
    }

    public Rac1Class749HostProbe ApplyTerminalStatus(RuntimeDynamicObject source, int nativeStatus)
    {
        if (nativeStatus is not (Rac1Class749Hostile.TerminalNativeStateFd or Rac1Class749Hostile.TerminalNativeStateFe))
            throw new ArgumentOutOfRangeException(nameof(nativeStatus));

        var (key, entry) = RequireEntry(source);
        if (entry.NativeState != Rac1Class749Hostile.DamageNativeState &&
            entry.NativeState != Rac1Class749Hostile.TerminalNativeStateFd &&
            entry.NativeState != Rac1Class749Hostile.TerminalNativeStateFe)
            throw new InvalidOperationException(
                $"R&C1 class-749 terminal status cannot follow native state {entry.NativeState}.");

        int nativeStateBefore = entry.NativeState;
        entry.RuntimeState = Rac1MobyRuntime.Terminalize(entry.RuntimeState, nativeStatus);
        entry.NativeSequence = null;
        entry.NativeSequenceUpdate = 0;

        var hostEvents = new List<IRac1MobyHostEvent>();
        if (nativeStateBefore != nativeStatus)
        {
            hostEvents.Add(new Rac1MobyNativeStateChangedEvent(
                entry.RuntimeState.Key,
                nativeStateBefore,
                nativeStatus));
        }
        hostEvents.Add(new Rac1MobyTerminalizedEvent(entry.RuntimeState.Key, nativeStatus));
        return Snapshot(key, entry, hostEvents: hostEvents);
    }

    private static int UpdateTargetDescriptor(Entry entry, Rac1Class749TargetFacts target)
    {
        if (entry.NativeState == Rac1Class749Hostile.LinkedObjectNativeState)
        {
            Rac1Class749Hostile.WriteTargetDestination(entry.PVar, target.TargetPosition);
            return Rac1Class749Hostile.ReadStatusSentinel(entry.PVar);
        }

        int status = target.StatusSentinel ??
            (entry.ActivationGroup is int group
                ? Rac1Class749VeldinPopulation.IsAdmitted(group, target.TargetPosition) ? 0 : 2
                : Rac1Class749Hostile.ReadStatusSentinel(entry.PVar));
        Rac1Class749Hostile.WriteStatusSentinel(entry.PVar, status);
        if (status != Rac1Class749Hostile.StatusSentinelTwo &&
            (entry.ActivationGroup is not null || target.TargetPosition != default))
            Rac1Class749Hostile.WriteTargetDestination(entry.PVar, target.TargetPosition);
        return status;
    }

    private static void RandomizeActivationYaw(Entry entry)
    {
        entry.ActivationCount++;
        uint mixed = unchecked(
            (uint)(entry.InstanceIndex * 1103515245) +
            (uint)(entry.ActivationCount * 12345));
        double unit = (mixed & 0xffffu) / 65535d;
        const double maxRadians = 20d * Math.PI / 180d;
        Rac1Class749Hostile.WriteActivationYawOffset(
            entry.PVar, checked((float)((unit * 2d - 1d) * maxRadians)));
    }

    private static Rac1Class749AttackEvent? AdvanceAttackSequence(Entry entry)
    {
        if (entry.NativeSequence != Rac1Class749Hostile.AttackSequenceId)
            throw new InvalidOperationException("R&C1 class-749 state 7 requires native sequence 5.");

        int previous = entry.NativeSequenceUpdate;
        int next = previous + 1;
        bool crossedMarker =
            previous < Rac1Class749Hostile.AttackMarkerNativeUpdate &&
            next >= Rac1Class749Hostile.AttackMarkerNativeUpdate;
        if (next >= Rac1Class749Hostile.AttackSequenceNativeUpdates)
            next -= Rac1Class749Hostile.AttackSequenceNativeUpdates;
        entry.NativeSequenceUpdate = next;

        return crossedMarker
            ? new Rac1Class749AttackEvent(Rac1Class749Hostile.AttackMarker, Rac1Class749Hostile.AttackDamage)
            : null;
    }

    private static void SetSequence(Entry entry, int sequence)
    {
        entry.NativeSequence = sequence;
        entry.NativeSequenceUpdate = 0;
    }

    private static IReadOnlyList<IRac1MobyHostIntent> HostIntentsForDispatch(
        int nativeState,
        ReadOnlySpan<byte> pvar) =>
        nativeState switch
        {
            Rac1Class749Hostile.TargetSearchNativeState =>
            [
                new Rac1Class749IdleTurnIntent(
                    Rac1Class749Hostile.IdleTurnDescriptorOffset,
                    Rac1Class749Hostile.IdleTurnInput,
                    Rac1Class749Hostile.IdleTurnInput),
            ],
            Rac1Class749Hostile.TargetedNativeState =>
            [
                new Rac1Class749NavigationIntent(
                    Rac1Class749NavigationIntentKind.PursueRecoveredTarget,
                    Rac1Class749Hostile.ReadTargetDestination(pvar)),
            ],
            Rac1Class749Hostile.ReturnHomeNativeState =>
            [
                new Rac1Class749NavigationIntent(
                    Rac1Class749NavigationIntentKind.ReturnHome,
                    Rac1Class749Hostile.ReadHomePosition(pvar)),
            ],
            _ => Array.Empty<IRac1MobyHostIntent>(),
        };

    private static IReadOnlyList<IRac1MobyHostEvent> HostEventsForStep(
        Rac1MobyRuntimeKey key,
        int nativeStateBefore,
        int nativeStateAfter,
        Rac1Class749AttackEvent? attack)
    {
        var events = new List<IRac1MobyHostEvent>(2);
        if (attack is not null) events.Add(attack);
        if (nativeStateBefore != nativeStateAfter)
            events.Add(new Rac1MobyNativeStateChangedEvent(key, nativeStateBefore, nativeStateAfter));
        return events;
    }

    private (Rac1Class749Key Key, Entry Entry) RequireEntry(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != Rac1Class749Hostile.NativeClassId)
            throw new ArgumentException("Source is not an R&C1 class-749 hostile.", nameof(source));
        var key = new Rac1Class749Key(source.NativeClassId, source.InstanceIndex);
        if (!_entries.TryGetValue(key, out var entry))
            throw new InvalidOperationException($"R&C1 class-749 instance {source.InstanceIndex} is not registered.");
        entry.EntityState.EnsureMatches(source);
        return (key, entry);
    }

    private static void ValidateTargetFacts(Rac1Class749TargetFacts target)
    {
        if (!double.IsFinite(target.Distance) || target.Distance < 0d)
            throw new ArgumentOutOfRangeException(nameof(target));
        if (!double.IsFinite(target.FacingError) || target.FacingError < 0d)
            throw new ArgumentOutOfRangeException(nameof(target));
        if (!double.IsFinite(target.CurrentPosition.X) ||
            !double.IsFinite(target.CurrentPosition.Y) ||
            !double.IsFinite(target.CurrentPosition.Z) ||
            !double.IsFinite(target.TargetPosition.X) ||
            !double.IsFinite(target.TargetPosition.Y) ||
            !double.IsFinite(target.TargetPosition.Z))
            throw new ArgumentOutOfRangeException(nameof(target));
    }

    private static Rac1Class749HostProbe Snapshot(
        Rac1Class749Key key,
        Entry entry,
        Rac1Class749AttackEvent? attack = null,
        IReadOnlyList<IRac1MobyHostIntent>? hostIntents = null,
        IReadOnlyList<IRac1MobyHostEvent>? hostEvents = null) =>
        new(
            key,
            Rac1Class749Hostile.ReadHealth(entry.PVar),
            entry.NativeSequence,
            entry.NativeSequenceUpdate,
            attack,
            entry.RuntimeState,
            hostIntents ?? Array.Empty<IRac1MobyHostIntent>(),
            hostEvents ?? Array.Empty<IRac1MobyHostEvent>());

    private sealed class Entry(
        byte[] pvar,
        int? activationGroup,
        Rac1MobyRuntimeState runtimeState)
    {
        public byte[] PVar { get; } = pvar;
        public int InstanceIndex { get; } = runtimeState.Key.InstanceIndex;
        public int? ActivationGroup { get; } = activationGroup;
        public int ActivationCount { get; set; }
        public Rac1MobyRuntimeState RuntimeState { get; set; } = runtimeState;
        public int NativeState
        {
            get => RuntimeState.NativeState;
            set => RuntimeState = Rac1MobyRuntime.WithNativeState(RuntimeState, value);
        }
        public int? NativeSequence { get; set; }
        public int NativeSequenceUpdate { get; set; }
        public RuntimeEntityState EntityState => RuntimeState.EntityState;
    }
}

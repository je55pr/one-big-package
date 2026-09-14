using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Deterministic session for the single recovered class-749 hostile witness.
/// Entries are keyed by native class plus authored instance index; UID semantics are not used.
/// </summary>
public sealed class Rac1Class749HostileSession
{
    private readonly Dictionary<Rac1Class749Key, Entry> _entries = [];

    public Rac1Class749HostProbe RegisterRepresentative(
        RuntimeDynamicObject source,
        RuntimeEntityState current)
    {
        var authored = Rac1Class749Hostile.ReadAuthored(source)
            ?? throw new ArgumentException("Source is not an R&C1 class-749 hostile.", nameof(source));
        current.EnsureMatches(source);

        var entry = new Entry(
            Rac1Class749Hostile.RequirePVar(source),
            Rac1Class749Hostile.TargetSearchNativeState,
            current);
        if (!_entries.TryAdd(authored.Key, entry))
            throw new InvalidOperationException(
                $"R&C1 class-749 instance {source.InstanceIndex} is already registered.");
        return Snapshot(authored.Key, entry);
    }

    public Rac1Class749HostProbe Probe(RuntimeDynamicObject source)
    {
        var (key, entry) = RequireEntry(source);
        return Snapshot(key, entry);
    }

    public Rac1Class749HostProbe Step(
        RuntimeDynamicObject source,
        Rac1Class749TargetFacts target,
        double nativeAnimationMarker)
    {
        ValidateTargetFacts(target);
        if (!double.IsFinite(nativeAnimationMarker))
            throw new ArgumentOutOfRangeException(nameof(nativeAnimationMarker));

        var (key, entry) = RequireEntry(source);
        Rac1Class749AttackEvent? attack = null;
        if (entry.NativeState == Rac1Class749Hostile.TargetSearchNativeState)
        {
            if (target.TargetAcquired)
                entry.NativeState = Rac1Class749Hostile.TargetedNativeState;
        }
        else if (entry.NativeState == Rac1Class749Hostile.TargetedNativeState)
        {
            if (target.TargetAcquired &&
                target.Distance < Rac1Class749Hostile.AttackDistanceExclusive &&
                target.FacingError < Rac1Class749Hostile.AttackFacingErrorExclusive)
            {
                entry.NativeState = Rac1Class749Hostile.AttackNativeState;
                entry.NativeSequence = Rac1Class749Hostile.AttackSequenceId;
                entry.AttackEmitted = false;
            }
        }
        else if (entry.NativeState == Rac1Class749Hostile.AttackNativeState &&
                 !entry.AttackEmitted &&
                 nativeAnimationMarker == Rac1Class749Hostile.AttackMarker)
        {
            attack = new Rac1Class749AttackEvent(
                Rac1Class749Hostile.AttackMarker,
                Rac1Class749Hostile.AttackDamage);
            entry.AttackEmitted = true;
        }

        return Snapshot(key, entry, attack);
    }

    public Rac1Class749HostProbe ApplyWrenchDamage(
        RuntimeDynamicObject source,
        Rac1WrenchDamageResult damage)
    {
        if (damage.ContactPath != Rac1WrenchContactPath.ForwardDirectRecord ||
            damage.NativeDamage != Rac1WrenchCombatController.NativeDamage ||
            damage.NativeDamageFlags != Rac1WrenchCombatController.NativeDamageFlags ||
            damage.BoltCrateBreak is not null)
            throw new NotSupportedException(
                "Only the recovered ordinary forward wrench damage result is admitted for class 749.");

        var (key, entry) = RequireEntry(source);
        float healthBefore = Rac1Class749Hostile.ReadHealth(entry.PVar);
        if (healthBefore != 1f)
            throw new NotSupportedException(
                $"R&C1 class-749 wrench consequence is proven only for representative health 1.0, not {healthBefore}.");

        float healthAfter = healthBefore - checked((float)damage.NativeDamage);
        if (healthAfter != 0f)
            throw new InvalidOperationException("Representative class-749 wrench damage did not produce health 0.0.");
        Rac1Class749Hostile.WriteHealth(entry.PVar, healthAfter);
        entry.NativeState = Rac1Class749Hostile.DamageNativeState;
        entry.NativeSequence = null;
        entry.AttackEmitted = false;
        return Snapshot(key, entry);
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

        entry.NativeState = nativeStatus;
        entry.NativeSequence = null;
        entry.AttackEmitted = false;
        entry.EntityState = entry.EntityState.WithPresence(RuntimeEntityPresence.Inactive);
        return Snapshot(key, entry);
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
    }

    private static Rac1Class749HostProbe Snapshot(
        Rac1Class749Key key,
        Entry entry,
        Rac1Class749AttackEvent? attack = null) =>
        new(
            key,
            entry.NativeState,
            Rac1Class749Hostile.ReadHealth(entry.PVar),
            entry.NativeSequence,
            attack,
            entry.EntityState);

    private sealed class Entry(byte[] pvar, int nativeState, RuntimeEntityState entityState)
    {
        public byte[] PVar { get; } = pvar;
        public int NativeState { get; set; } = nativeState;
        public int? NativeSequence { get; set; }
        public bool AttackEmitted { get; set; }
        public RuntimeEntityState EntityState { get; set; } = entityState;
    }
}

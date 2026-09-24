using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Engine-independent runtime for the recovered class-1440 contact/damage
/// boundary. Unrecovered native states remain opaque instead of inheriting
/// class-749 chase, melee, or death policy.
/// </summary>
public sealed class Rac1Class1440ContactFamilySession :
    IRac1MobyDamageConsumer
{
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Dictionary<Rac1Class1440Key, Entry> _entries = [];

    public Rac1Class1440ContactFamilySession()
        : this(new Rac1MobyRuntimeSession())
    {
    }

    public Rac1Class1440ContactFamilySession(Rac1MobyRuntimeSession runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.RegisterDamageConsumer(this);
    }

    public int NativeClassId => Rac1Class1440ContactFamily.NativeClassId;

    object IRac1MobyDamageConsumer.ApplyDamage(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage) =>
        ApplyDamage(source, damage);

    public Rac1Class1440ContactProbe RegisterRecovered(
        RuntimeDynamicObject source,
        RuntimeEntityState current,
        int initialNativeState)
    {
        var authored = Rac1Class1440ContactFamily.ReadAuthored(source)
            ?? throw new ArgumentException("Source is not an R&C1 class-1440 Moby.", nameof(source));
        current.EnsureMatches(source);
        Rac1Class1440ContactFamily.ValidateRecoveredNativeState(initialNativeState);
        if (authored.Health != Rac1Class1440ContactFamily.WitnessHealth)
            throw new NotSupportedException(
                $"R&C1 class-1440 registration is proven for witness health " +
                $"{Rac1Class1440ContactFamily.WitnessHealth}, not {authored.Health}.");

        var runtimeInstance = _runtime.Register(
            source,
            current,
            initialNativeState,
            Rac1Class1440ContactFamily.RequirePVar(source));
        var entry = new Entry(_runtime, runtimeInstance);
        if (!_entries.TryAdd(authored.Key, entry))
            throw new InvalidOperationException(
                $"R&C1 class-1440 instance {source.InstanceIndex} is already registered.");
        return Snapshot(authored.Key, entry);
    }

    public Rac1Class1440ContactProbe Probe(RuntimeDynamicObject source)
    {
        var (key, entry) = RequireEntry(source);
        return Snapshot(key, entry);
    }

    /// <summary>
    /// Surfaces one of the two recovered native state-5 query descriptors. This
    /// does not claim that both sites execute on every update.
    /// </summary>
    public Rac1Class1440ContactProbe BeginContactQuery(
        RuntimeDynamicObject source,
        int querySite)
    {
        ValidateQuerySite(querySite);
        var (key, entry) = RequireEntry(source);
        RequireContactQueryState(entry);
        IReadOnlyList<IRac1MobyHostIntent> intents =
        [
            new Rac1Class1440ContactQueryIntent(
                entry.RuntimeState.Key,
                querySite,
                Rac1Class1440ContactFamily.ContactDescriptorWord84,
                Rac1Class1440ContactFamily.NativeClassId,
                Rac1Class1440ContactFamily.ContactDescriptorScalar8c,
                Rac1Class1440ContactFamily.ContactDescriptorInteger90),
        ];
        return Snapshot(key, entry, hostIntents: intents);
    }

    /// <summary>
    /// Applies the recovered state-5 player-contact gate. Non-player/no-contact
    /// results do not assign semantics to the query result and leave state 5.
    /// </summary>
    public Rac1Class1440ContactProbe ResolveContactQuery(
        RuntimeDynamicObject source,
        int querySite,
        bool queryHitPlayer)
    {
        ValidateQuerySite(querySite);
        var (key, entry) = RequireEntry(source);
        RequireContactQueryState(entry);
        if (!queryHitPlayer)
            return Snapshot(key, entry);

        int nativeStateBefore = entry.NativeState;
        entry.NativeState = Rac1Class1440ContactFamily.ContactTransitionNativeState;
        IReadOnlyList<IRac1MobyHostEvent> events =
        [
            new Rac1Class1440PlayerContactEvent(entry.RuntimeState.Key, querySite),
            new Rac1MobyNativeStateChangedEvent(
                entry.RuntimeState.Key,
                nativeStateBefore,
                entry.NativeState),
        ];
        return Snapshot(key, entry, hostEvents: events);
    }

    /// <summary>
    /// Consumes the shared R&C1 damage event after upstream native filtering.
    /// This class validates victim identity and requires the native envelope,
    /// while source/weapon eligibility remains the caller's responsibility.
    /// </summary>
    public Rac1Class1440ContactProbe ApplyDamage(
        RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var target = new Rac1MobyRuntimeKey(
            source.NativeClassId,
            source.InstanceIndex);
        if (!damage.Target.MatchesMoby(target))
            throw new ArgumentException(
                "R&C1 class-1440 damage target does not match the supplied runtime entity.",
                nameof(damage));

        Rac1NativeDamageEnvelope envelope = damage.DamageEnvelope
            ?? throw new NotSupportedException(
                "R&C1 class-1440 consumed damage requires recovered native damage flags.");
        return ApplyConsumedNativeDamage(source, envelope);
    }

    /// <summary>
    /// Applies only the class-owned consequence after the common native damage
    /// consumer has already accepted a record. Damage filtering and weapon
    /// eligibility therefore stay outside this class-specific method.
    /// </summary>
    public Rac1Class1440ContactProbe ApplyConsumedNativeDamage(
        RuntimeDynamicObject source,
        Rac1NativeDamageEnvelope damage)
    {
        if (damage.NativeDamage <= 0d)
            throw new NotSupportedException(
                "R&C1 class-1440 damage intake is modeled only for positive consumed native damage.");

        var (key, entry) = RequireEntry(source);
        float healthBefore = Rac1Class1440ContactFamily.ReadHealth(entry.PVar);
        float healthAfter = healthBefore - checked((float)damage.NativeDamage);
        if (!float.IsFinite(healthAfter))
            throw new InvalidOperationException("R&C1 class-1440 damage produced non-finite health.");
        Rac1Class1440ContactFamily.WriteHealth(entry.PVar, healthAfter);

        IReadOnlyList<IRac1MobyHostEvent> events =
        [
            new Rac1MobyDamageConsumedEvent(entry.RuntimeState.Key, damage),
        ];
        return Snapshot(key, entry, hostEvents: events);
    }

    private (Rac1Class1440Key Key, Entry Entry) RequireEntry(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" ||
            source.NativeClassId != Rac1Class1440ContactFamily.NativeClassId)
            throw new ArgumentException("Source is not an R&C1 class-1440 Moby.", nameof(source));

        var key = new Rac1Class1440Key(source.NativeClassId, source.InstanceIndex);
        if (!_entries.TryGetValue(key, out var entry))
            throw new InvalidOperationException(
                $"R&C1 class-1440 instance {source.InstanceIndex} is not registered.");

        var runtimeInstance = _runtime.Require(source);
        if (!ReferenceEquals(runtimeInstance, entry.RuntimeInstance))
            throw new InvalidOperationException(
                $"R&C1 class-1440 instance {source.InstanceIndex} runtime identity drifted.");
        return (key, entry);
    }

    private static void RequireContactQueryState(Entry entry)
    {
        if (entry.NativeState != Rac1Class1440ContactFamily.ContactQueryNativeState)
            throw new InvalidOperationException(
                $"R&C1 class-1440 contact query requires native state " +
                $"{Rac1Class1440ContactFamily.ContactQueryNativeState}, not {entry.NativeState}.");
    }

    private static void ValidateQuerySite(int querySite)
    {
        if (querySite is < 0 or >= Rac1Class1440ContactFamily.ContactQuerySiteCount)
            throw new ArgumentOutOfRangeException(nameof(querySite));
    }

    private static Rac1Class1440ContactProbe Snapshot(
        Rac1Class1440Key key,
        Entry entry,
        IReadOnlyList<IRac1MobyHostIntent>? hostIntents = null,
        IReadOnlyList<IRac1MobyHostEvent>? hostEvents = null) =>
        new(
            key,
            Rac1Class1440ContactFamily.ReadHealth(entry.PVar),
            entry.RuntimeState,
            hostIntents ?? Array.Empty<IRac1MobyHostIntent>(),
            hostEvents ?? Array.Empty<IRac1MobyHostEvent>());

    private sealed class Entry(
        Rac1MobyRuntimeSession runtime,
        Rac1MobyRuntimeInstance runtimeInstance)
    {
        public Rac1MobyRuntimeInstance RuntimeInstance { get; } = runtimeInstance;
        public byte[] PVar => RuntimeInstance.MutablePVar;
        public Rac1MobyRuntimeState RuntimeState => RuntimeInstance.State;
        public int NativeState
        {
            get => RuntimeState.NativeState;
            set => runtime.SetNativeState(RuntimeInstance, value);
        }
        public RuntimeEntityState EntityState => RuntimeState.EntityState;
    }
}

using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded Novalis class459 host adapter. Class/PVar/health/clip data originate
/// in retail LEVEL1; detailed branches of birdbot_update_l1 (L1:0x2e6bf0)
/// are generated in rac1-native-game@3fd14c1, so the movement, weapon cadence
/// and animation selectors below are explicitly NOT a native translation.
/// </summary>
public sealed class Rac1NovalisBirdbotSession : IRac1MobyClassController, IRac1MobyDamageConsumer
{
    public const int ClassId = 459;
    public const int PVarSize = 0x240;
    public const float PlayerAcquisitionDistance = 18f;
    public const float ShootingDistance = 13f;
    public const float StandOffDistance = 5f;
    public const float HeightTolerance = 6f;
    public const int ShootCooldownTicks = 65;
    public const int DeathSequence = 19; // host-selected original clip; native selector still unknown

    public enum HostMode { Idle, Pursuing, Shooting, Hit, Dead }
    public readonly record struct Facts(Vector3 Position, Vector3 Target, bool PlayerAlive,
        bool AnimationWrapped = false);
    public sealed record Probe(Rac1MobyRuntimeInstance Instance, HostMode Mode, int Sequence,
        float Health, Vector3? Destination, bool Fire, bool Terminal, int Cooldown);

    private sealed class Entry(Rac1MobyRuntimeInstance instance)
    {
        public Rac1MobyRuntimeInstance Instance { get; } = instance;
        public HostMode Mode { get; set; }
        public int Sequence { get; set; } = 1;
        public int Cooldown { get; set; }
        public bool Fire { get; set; }
        public Vector3? Destination { get; set; }
    }

    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Dictionary<int, Entry> _entries = [];
    public int NativeClassId => ClassId;
    public int RegisteredCount => _entries.Count;
    public Rac1NovalisBirdbotSession(Rac1MobyRuntimeSession runtime)
    {
        _runtime = runtime;
        runtime.RegisterController(this);
        runtime.RegisterDamageConsumer(this);
    }

    public Probe Register(RuntimeDynamicObject source, RuntimeEntityState state)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != ClassId)
            throw new ArgumentException("Birdbot requires R&C1 class459.", nameof(source));
        byte[][] pvars = source.NativePayloads?
            .Where(payload => payload.Format == Rac1BoltCrate.PVarPayloadFormat)
            .Select(payload => payload.Data).ToArray() ?? [];
        if (pvars is not [{ Length: PVarSize }])
            throw new InvalidDataException("Novalis class459 requires its original 0x240 PVar.");
        var pvar = pvars[0].ToArray();
        float health = BinaryPrimitives.ReadSingleLittleEndian(pvar.AsSpan(0x20));
        if (!float.IsFinite(health) || health <= 0)
            throw new InvalidDataException("Birdbot's original health field must be positive.");
        // Host states are temporary; do not pretend the source's 0..14 states
        // have been mapped from the generated code.
        var instance = _runtime.Register(source, state, nativeState: 1, pvar);
        var entry = new Entry(instance);
        if (!_entries.TryAdd(source.InstanceIndex, entry))
            throw new InvalidOperationException("Duplicate Birdbot native index.");
        return Snapshot(entry);
    }

    public Probe Get(int instanceIndex) => Snapshot(_entries[instanceIndex]);
    object IRac1MobyClassController.Update(RuntimeDynamicObject source, object facts) =>
        facts is Facts input ? Step(source.InstanceIndex, input)
            : throw new ArgumentException("Birdbot host update requires Birdbot facts.", nameof(facts));

    public Probe Step(int index, Facts facts)
    {
        if (!Finite(facts.Position) || !Finite(facts.Target))
            throw new ArgumentOutOfRangeException(nameof(facts));
        Entry entry = _entries[index];
        entry.Destination = null;
        entry.Fire = false;
        if (!entry.Instance.IsActive) return Snapshot(entry);
        if (entry.Cooldown > 0) entry.Cooldown--;
        float distance = Vector2.Distance(new(facts.Position.X, facts.Position.Y),
            new(facts.Target.X, facts.Target.Y));
        bool admitted = facts.PlayerAlive &&
            MathF.Abs(facts.Position.Z - facts.Target.Z) < HeightTolerance &&
            distance < PlayerAcquisitionDistance;

        switch (entry.Mode)
        {
            case HostMode.Idle:
                if (admitted) Select(entry, HostMode.Pursuing, 2);
                break;
            case HostMode.Pursuing:
                if (!admitted) { Select(entry, HostMode.Idle, 1); break; }
                if (distance > StandOffDistance) entry.Destination = facts.Target;
                if (distance < ShootingDistance && entry.Cooldown == 0)
                {
                    entry.Fire = true;
                    entry.Cooldown = ShootCooldownTicks;
                    Select(entry, HostMode.Shooting, 3);
                }
                break;
            case HostMode.Shooting:
                if (facts.AnimationWrapped) Select(entry, admitted ? HostMode.Pursuing : HostMode.Idle, admitted ? 2 : 1);
                break;
            case HostMode.Hit:
                if (facts.AnimationWrapped) Select(entry, admitted ? HostMode.Pursuing : HostMode.Idle, admitted ? 2 : 1);
                break;
            case HostMode.Dead:
                if (facts.AnimationWrapped) _runtime.Terminalize(entry.Instance);
                break;
        }
        return Snapshot(entry);
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        source.NativeClassId == ClassId &&
        _entries.TryGetValue(source.InstanceIndex, out var entry) &&
        entry.Instance.IsActive && entry.Mode != HostMode.Dead &&
        damage.Target.MatchesMoby(entry.Instance.Key) &&
        Rac1EnemyDamage.IsPlayerWeapon(damage);
    object IRac1MobyDamageConsumer.ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        ApplyDamage(source, damage);
    public (Probe Probe, bool Lethal) ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage))
            throw new InvalidOperationException("Birdbot cannot consume this damage.");
        var entry = _entries[source.InstanceIndex];
        float health = Health(entry) - (float)damage.NativeDamage;
        BinaryPrimitives.WriteSingleLittleEndian(entry.Instance.MutablePVar.AsSpan(0x20), health);
        Select(entry, health <= 0 ? HostMode.Dead : HostMode.Hit, health <= 0 ? DeathSequence : 7);
        return (Snapshot(entry), health <= 0);
    }

    private void Select(Entry entry, HostMode mode, int sequence)
    {
        entry.Mode = mode; entry.Sequence = sequence;
    }
    private static Probe Snapshot(Entry entry) => new(entry.Instance, entry.Mode, entry.Sequence,
        Health(entry), entry.Destination, entry.Fire, !entry.Instance.IsActive, entry.Cooldown);
    private static float Health(Entry entry) =>
        BinaryPrimitives.ReadSingleLittleEndian(entry.Instance.MutablePVar.AsSpan(0x20));
    private static bool Finite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}

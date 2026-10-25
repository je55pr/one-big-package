using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Opening Veldin class1440: script init health2, authored entrance path,
/// turn/idle, three shared attack slots, wind-up, sweeping contact and recovery.
/// State/sequence selectors are from 0x2e0b88..0x2e1aa8; no class749 defaults.
/// </summary>
public sealed class Rac1Class1440Session : IRac1MobyDamageConsumer, IRac1MobyClassController
{
    public const int ClassId = 1440;
    public const int PVarSize = 0x1e0;
    public const float AcquisitionDistance = 24;
    public const float ContactLength = 5;
    public const float SweepOffset = .15f;
    public const int AttackSlotCount = 3;
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly IReadOnlyList<Vector3[]> _paths;
    private readonly Dictionary<int, Entry> _entries = [];
    private readonly HashSet<int> _attackers = [];
    private readonly Rac1PickupRandom _random;
    public int NativeClassId => ClassId;
    public int RegisteredCount => _entries.Count;

    public sealed record Facts(Vector3 Position, Vector3 Target, bool AnimationFinished = false,
        bool AnimationBlending = false, float AnimationPosition = 0, bool PlayerContact = false,
        bool Grounded = true, bool PlayerAlive = true, float StepLength = 0,
        Target? LinkedTarget = null, Rac1GameplayEntityRef? ContactTarget = null);
    public sealed record Target(Rac1MobyRuntimeKey Identity, Vector3 Position);
    public sealed record Probe(Rac1MobyRuntimeInstance Instance, float Health, int NativeState, int Sequence,
        Vector3? Destination, float TurnOffset, bool EmitContactDamage, int HitCooldown, bool HasAttackSlot,
        float DistanceToPathEnd, Vector3 LookAt, int? LinkedTargetInstanceIndex, Rac1GameplayEntityRef? DamageTarget)
    {
        public bool ReactionActive => NativeState is 7 or 8;
        public bool TurnsToTarget => NativeState is >= 2 and <= 5;
    }
    public sealed record DamageResult(Probe Probe, bool Lethal);

    public Rac1Class1440Session(Rac1MobyRuntimeSession runtime, IReadOnlyList<Vector3[]> paths, Rac1PickupRandom? random = null)
    {
        _runtime = runtime; _paths = paths;
        _random = random ?? Rac1PickupRandom.FromSeed(0);
        runtime.RegisterDamageConsumer(this);
        runtime.RegisterController(this);
    }

    public Probe Register(RuntimeDynamicObject source, RuntimeEntityState state)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != ClassId || source.InstanceIndex is < 189 or > 197)
            throw new ArgumentException("Only opening Veldin's authored class1440 population is admitted.", nameof(source));
        var pvar = Rac1Class1440ContactFamily.RequirePVar(source);
        if (pvar.Length != PVarSize) throw new InvalidDataException("Class1440 requires its exact 0x1e0 PVar.");
        int region = Read(pvar, 0x1b4), path = Read(pvar, 0x1b8);
        if (region < -1 || path < -1 || region >= _paths.Count || path >= _paths.Count)
            throw new InvalidDataException("Class1440 references an absent authored path.");
        if (path >= 0)
        {
            if (_paths[path].Length < 2) throw new InvalidDataException("Class1440 entrance requires a start and a destination.");
            // 0x2e0d20..0x2e0d28 copies path point0 into Moby+0x10.
            // The editor placement can be at the route's far end; retaining it
            // makes the robot run backwards across the route before entering.
            Vector3 start = _paths[path][0];
            var matrix = (double[])state.Presentation.Transform.Matrix.Clone();
            matrix[12] = start.X; matrix[13] = start.Z; matrix[14] = start.Y;
            state = state.WithTransform(new(matrix));
        }
        Write(pvar, 0x20, BitConverter.SingleToInt32Bits(2)); // 0x2e0cb4 overwrites authored 1.0.
        pvar[0x28] = 1; // 0x2e0cb8: health-state kind, shared Blaster damage table.
        var instance = _runtime.Register(source, state, path < 0 ? 3 : 1, pvar);
        var entry = new Entry(instance, region, path, Read(pvar, 0x1bc), _random.Below(2) != 0);
        _entries.Add(source.InstanceIndex, entry);
        return Snapshot(entry);
    }

    public Probe Get(int instance) => Snapshot(_entries[instance]);
    public IReadOnlyList<Vector3> ActivationRegion(int instance) => _entries[instance].Region is >= 0 and var region ? _paths[region] : [];
    public IReadOnlyList<Vector3> EntrancePath(int instance) => _entries[instance].Path is >= 0 and var path ? _paths[path] : [];
    object IRac1MobyClassController.Update(RuntimeDynamicObject source, object facts) => facts is Facts update
        ? Step(source.InstanceIndex, update) : throw new ArgumentException("Class1440 update requires its source facts.", nameof(facts));
    public Probe Step(int instanceIndex, Facts facts)
    {
        if (!Finite(facts.Position) || !Finite(facts.Target) ||
            facts.LinkedTarget is { Position: var linkedPosition } && !Finite(linkedPosition) ||
            !float.IsFinite(facts.AnimationPosition) || facts.AnimationPosition < 0 ||
            !float.IsFinite(facts.StepLength) || facts.StepLength < 0)
            throw new ArgumentOutOfRangeException(nameof(facts));
        var entry = _entries[instanceIndex];
        entry.Destination = null; entry.ContactDamage = false; entry.DamageTarget = null; entry.TurnOffset = 0;
        if (!entry.Instance.IsActive) return Snapshot(entry);
        if (entry.Cooldown > 0) entry.Cooldown--;
        int state = entry.Instance.State.NativeState;
        bool admitted = facts.PlayerAlive && PlanarDistance(facts.Position, facts.Target) <= AcquisitionDistance;
        entry.LookAt = facts.Target;
        // 0x2e1a38..0x2e1a7c compares the authored linked Moby with the
        // ordinary target and selects it when closer, independently of range24.
        if (facts.LinkedTarget is { } linked && entry.Linked == linked.Identity.InstanceIndex &&
            PlanarDistance(facts.Position, linked.Position) < PlanarDistance(facts.Position, facts.Target))
        {
            entry.LookAt = linked.Position; admitted = true;
        }
        if (state == 1) admitted = entry.Region >= 0 && facts.PlayerAlive && Rac1Paths.ContainsPlanar(_paths[entry.Region], facts.Target);
        switch (state)
        {
            case 1:
                if (admitted) Select(entry, 2, 9);
                break;
            case 2:
                var path = _paths[entry.Path];
                if (entry.Waypoint >= path.Length) { Select(entry, 3, 1); break; }
                entry.Destination = path[entry.Waypoint];
                entry.DistanceToPathEnd = Vector3.Distance(facts.Position, path[^1]);
                // 0x2e0f2c calls full XYZ vector length, unlike the XY target query.
                if (Vector3.Distance(facts.Position, path[entry.Waypoint]) < facts.StepLength * 2 && ++entry.Waypoint >= path.Length)
                    Select(entry, 3, 1);
                break;
            case 3:
                if (admitted && (_attackers.Contains(instanceIndex) || _attackers.Count < AttackSlotCount))
                {
                    _attackers.Add(instanceIndex); Select(entry, 4, 5);
                }
                else if (PlanarDistance(facts.Position, facts.Target) < 20) entry.Sequence = 4;
                else if (facts.AnimationFinished) entry.Sequence = 1 + _random.Below(2);
                break;
            case 4:
                if (facts.AnimationFinished && !facts.AnimationBlending) { entry.FireLoops = 0; Select(entry, 5, 6); }
                break;
            case 5:
                entry.TurnOffset = entry.Clockwise ? SweepOffset : -SweepOffset;
                bool active = !facts.AnimationBlending && facts.AnimationPosition > 1;
                var victim = facts.PlayerContact ? Rac1GameplayEntityRef.Player : facts.ContactTarget;
                if (active && victim is { } contact)
                {
                    entry.ContactDamage = true; entry.DamageTarget = contact;
                    if (contact == Rac1GameplayEntityRef.Player) { Release(entry); Select(entry, 6, 7); break; }
                }
                if (facts.AnimationFinished && !facts.AnimationBlending && entry.FireLoops++ == 3)
                {
                    Release(entry); Select(entry, 6, 12);
                }
                break;
            case 6:
                if (facts.AnimationFinished && !facts.AnimationBlending) Select(entry, 3, 1);
                break;
            case 7:
                if (facts.AnimationFinished && facts.Grounded) Select(entry, 3, 1);
                break;
            case 8:
                if (facts.AnimationFinished && facts.Grounded) { Release(entry); _runtime.Terminalize(entry.Instance); }
                break;
        }
        return Snapshot(entry);
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        source.SourceGame == ObpSourceGame.Rac1 && source.NativeClassId == ClassId && damage.Target.MatchesMoby(new(ClassId, source.InstanceIndex)) &&
        Rac1EnemyDamage.IsPlayerWeapon(damage) && _entries.TryGetValue(source.InstanceIndex, out var entry) &&
        entry.Instance.IsActive && entry.Instance.State.NativeState != 8 &&
        (entry.Cooldown == 0 || Rac1EnemyDamage.IsBlaster(damage));

    object IRac1MobyDamageConsumer.ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) => ApplyDamage(source, damage);
    public DamageResult ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage)) throw new InvalidOperationException("Class1440 cannot consume this damage event.");
        var entry = _entries[source.InstanceIndex];
        float health = Health(entry) - Rac1EnemyDamage.ResolveNativeDamage(damage, entry.Instance.MutablePVar);
        Write(entry.Instance.MutablePVar, 0x20, BitConverter.SingleToInt32Bits(health));
        Release(entry);
        if (health <= 0) Select(entry, 8, 11);
        else { entry.Cooldown = 60; Select(entry, 7, 8); }
        return new(Snapshot(entry), health <= 0);
    }

    private void Release(Entry entry) => _attackers.Remove(entry.Instance.InstanceIndex);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
    private void Select(Entry entry, int state, int sequence)
    {
        _runtime.SetNativeState(entry.Instance, state); entry.Sequence = sequence;
    }
    private static Probe Snapshot(Entry entry) => new(entry.Instance, Health(entry), entry.Instance.State.NativeState,
        entry.Sequence, entry.Destination, entry.TurnOffset, entry.ContactDamage, entry.Cooldown, entry.HasSlot, entry.DistanceToPathEnd,
        entry.LookAt, entry.Linked >= 0 ? entry.Linked : null, entry.DamageTarget);
    private static float Health(Entry entry) => BitConverter.Int32BitsToSingle(Read(entry.Instance.MutablePVar, 0x20));
    private static int Read(byte[] source, int offset) => BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset));
    private static void Write(byte[] target, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset), value);

    private sealed class Entry(Rac1MobyRuntimeInstance instance, int region, int path, int linked, bool clockwise)
    {
        public Rac1MobyRuntimeInstance Instance { get; } = instance;
        public int Region { get; } = region;
        public int Path { get; } = path;
        public int Linked { get; } = linked;
        public int Sequence { get; set; } = 1;
        public int Waypoint { get; set; } = 1;
        public int Cooldown { get; set; }
        public int FireLoops { get; set; }
        public bool Clockwise { get; } = clockwise;
        public Vector3? Destination { get; set; }
        public float TurnOffset { get; set; }
        public float DistanceToPathEnd { get; set; }
        public bool ContactDamage { get; set; }
        public Rac1GameplayEntityRef? DamageTarget { get; set; }
        public Vector3 LookAt { get; set; }
        public bool HasSlot => Instance.State.NativeState is 4 or 5;
    }
}

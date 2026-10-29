using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class-572/865/866 source identity, dormant group allocation and damage
/// transitions from native L01 0x2edca0, 0x2eec68, 0x2ef350. No Moby cloning.
/// World sweeps, path queries and animation samples remain explicit host facts.
/// </summary>
public sealed class Rac1AmoeboidFamilySession
{
    public const int KnockbackState = 8;
    public const int FollowPathState = 4;
    public const int ReturnState = 5;
    public const int LungeState = 6;
    public const int AnimationExitState = 7;
    public const int KnockbackRecoveryState = 9;
    public const float NativeAttackRange = 2f;
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Rac1AmoeboidPathRouter? _pathRouter;
    private readonly Func<int, Vector3, Vector3, bool>? _segmentIntersects;
    private readonly Dictionary<int, Entry> _actors = [];
    private readonly Dictionary<int, List<Entry>> _groups = [];

    private sealed class Entry(Rac1MobyRuntimeInstance native, Rac1AmoeboidSpawnFacts facts,
        int group, Vector3 pos, float yaw)
    {
        public Rac1MobyRuntimeInstance Native { get; } = native;
        public Rac1AmoeboidSpawnFacts Facts { get; } = facts;
        public int Group { get; } = group;
        public Vector3 Position { get; set; } = pos;
        public Vector3 Home { get; } = pos;
        public float Yaw { get; set; } = yaw;
        public float Health { get; set; } = 3;
        public bool UsedReserve { get; set; }
        public bool ContactEmitted { get; set; }
        public int Sequence { get; set; } = 0;
    }

    public Rac1AmoeboidFamilySession(Rac1MobyRuntimeSession runtime,
        Rac1AmoeboidPathRouter? pathRouter = null,
        Func<int, Vector3, Vector3, bool>? segmentIntersects = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if ((pathRouter is null) != (segmentIntersects is null))
            throw new ArgumentException("Native path routing needs both the source path table and collision adapter.");
        _pathRouter = pathRouter;
        _segmentIntersects = segmentIntersects;
        foreach (int cls in new[] { Rac1AmoeboidAuthored.Large, Rac1AmoeboidAuthored.Medium, Rac1AmoeboidAuthored.Small })
            _runtime.RegisterDamageConsumer(new FamilyDamageConsumer(this, cls));
    }

    public int RegisteredCount => _actors.Count;
    public int ActiveCount => _actors.Values.Count(x => x.Native.IsActive);
    public int DormantCount => _actors.Values.Count(x => x.Native.State.NativeState == Rac1AmoeboidAuthored.DormantPoolState);
    public int GatedCount => _actors.Values.Count(x => x.Native.State.NativeState == Rac1AmoeboidAuthored.WaitingForVolumeState);

    public Rac1AmoeboidFrame Register(RuntimeDynamicObject source, RuntimeEntityState state,
        Rac1AmoeboidSpawnFacts facts, int group, Vector3 nativePosition, float nativeYaw)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != facts.ClassId ||
            !Rac1AmoeboidAuthored.IsFamily(facts.ClassId))
            throw new ArgumentException("Amoeboid source/facts mismatch.", nameof(source));
        if (group is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(group));
        if (!float.IsFinite(nativeYaw) || !Finite(nativePosition))
            throw new ArgumentOutOfRangeException(nameof(nativePosition));
        if (_actors.ContainsKey(source.InstanceIndex)) throw new InvalidOperationException("Duplicate native Amoeboid.");
        var native = _runtime.Register(source, state, facts.InitialNativeState,
            source.NativePayloads?.Single(p => p.Format == Rac1BoltCrate.PVarPayloadFormat).Data ?? []);
        var entry = new Entry(native, facts, group, nativePosition, nativeYaw);
        // Native state0 overwrites the authored working-HP slot.
        entry.Health = facts.ForcedActive ? 1f : 3f;
        BinaryPrimitives.WriteSingleLittleEndian(native.MutablePVar.AsSpan(0x20), entry.Health);
        if (facts.InitialNativeState != Rac1AmoeboidAuthored.ActiveState)
            _runtime.SetPresence(native, RuntimeEntityPresence.Inactive);
        _actors.Add(source.InstanceIndex, entry);
        if (!_groups.TryGetValue(group, out var members)) _groups.Add(group, members = []);
        // Source list order follows authored runtime Moby indices, not class order.
        members.Add(entry);
        members.Sort((a, b) => a.Native.InstanceIndex.CompareTo(b.Native.InstanceIndex));
        return Frame(entry);
    }

    public Rac1AmoeboidFrame Get(int index) => Frame(_actors[index]);

    public int TargetCandidateGroup(int index) => _actors[index].Facts.TargetGroup;

    // Source selects +0x224 during a target-search countdown and +20
    // otherwise. The countdown is not yet modeled in the family host.
    public float HostDetectionRange(int index) => AttackAdmission(_actors[index]) + 20f;

    public IReadOnlyList<Rac1AmoeboidFrame> ActivateGates(Vector3 heroPosition,
        Func<int, Vector3, bool> containsVolume)
    {
        var activated = new List<Rac1AmoeboidFrame>();
        foreach (var entry in _actors.Values.OrderBy(x => x.Native.InstanceIndex))
        {
            if (entry.Native.State.NativeState != Rac1AmoeboidAuthored.WaitingForVolumeState ||
                !containsVolume(entry.Facts.VolumeIndex, heroPosition)) continue;
            // Native state 0 hides gated actors at authored Z - 20, then
            // state 13 restores +20. The Godot host hides the original
            // Moby visually without lowering it: restore its authored home
            // instead of lifting the rendered actor above the level.
            entry.Position = entry.Home;
            Activate(entry, Rac1AmoeboidAuthored.ActiveState, 0);
            activated.Add(Frame(entry));
        }
        return activated;
    }

    public Rac1AmoeboidFrame Step(int index, Vector3 position, Vector3 target,
        bool animationFinished = false, bool grounded = false, bool targetInContact = false,
        float animationPosition = 0)
    {
        if (!Finite(position) || !Finite(target) || !float.IsFinite(animationPosition))
            throw new ArgumentOutOfRangeException(nameof(position));
        var entry = _actors[index];
        if (!entry.Native.IsActive) return Frame(entry);
        entry.Position = position;
        int state = entry.Native.State.NativeState;
        bool attack = false;
        Vector3 next = Vector3.Zero;
        float planar = Vector2.Distance(new(position.X, position.Y), new(target.X, target.Y));
        switch (state)
        {
            case 1:
                // Native state 1 checks the source path first and enters 4
                // on a successful route; state 2 is the failed-path fallback.
                if (_pathRouter is not null && _segmentIntersects is not null &&
                    _pathRouter.TryFollowPath(entry.Facts.PathIndex, entry.Facts.NextPathIndex,
                        Size(entry.Native.NativeClassId) * 0.8f, position, target,
                        _segmentIntersects, out _))
                    Transition(entry, FollowPathState, 1);
                else
                    Transition(entry, 2, 1);
                // Native state 1 probes the route and changes animation/state,
                // but does not submit a movement vector until the next tick.
                break;
            case 2:
                if (planar < AttackAdmission(entry)) Transition(entry, 15, 1);
                else if (Vector2.Distance(new(position.X, position.Y), new(entry.Home.X, entry.Home.Y)) > 1)
                    next = MoveTowards(entry, position, entry.Home);
                break;
            case 15:
                if (planar < NativeAttackRange) Transition(entry, LungeState, 2);
                else if (planar > AttackAdmission(entry) + 20) Transition(entry, ReturnState, 1);
                else next = MoveTowards(entry, position, target);
                break;
            case FollowPathState:
                if (planar < NativeAttackRange) Transition(entry, LungeState, 2);
                else if (planar > AttackAdmission(entry) + 20) Transition(entry, ReturnState, 1);
                else if (!TryPathMove(entry, position, target, out next))
                    Transition(entry, ReturnState, 1);
                break;
            case LungeState:
                if (targetInContact && !entry.ContactEmitted && planar < NativeAttackRange &&
                    animationPosition >= 15.5f)
                {
                    attack = true;
                    entry.ContactEmitted = true;
                }
                if (animationFinished)
                {
                    entry.ContactEmitted = false;
                    Transition(entry, AnimationExitState, 3);
                }
                break;
            case AnimationExitState:
            case 3:
                if (animationFinished) Transition(entry, FollowPathState, 1);
                break;
            case ReturnState:
                if (Vector2.Distance(new(position.X, position.Y), new(entry.Home.X, entry.Home.Y)) < 1)
                    Transition(entry, 1, 0);
                else if (!TryPathMove(entry, position, entry.Home, out next))
                    next = Vector3.Zero; // Preserve source blocked route, don't walk through obstacles.
                break;
            case KnockbackState:
                if (grounded) Transition(entry, KnockbackRecoveryState, 5);
                break;
            case KnockbackRecoveryState:
                if (animationFinished) Transition(entry, FollowPathState, 1);
                break;
        }
        return Frame(entry) with { Displacement = next, Attack = attack };
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        source.SourceGame == ObpSourceGame.Rac1 && _actors.TryGetValue(source.InstanceIndex, out var actor) &&
        actor.Native.NativeClassId == source.NativeClassId && actor.Native.IsActive &&
        actor.Health > 0 && damage.Target.MatchesMoby(actor.Native.Key) &&
        (Rac1EnemyDamage.IsPlayerWeapon(damage) ||
         Rac1SeekerExplosion.IsNativeExplosion(damage) ||
         Rac1ExplosiveCrateBlast.IsNativeMobyBlast(damage));

    public Rac1AmoeboidDamageResult ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage)) throw new InvalidOperationException("Unadmitted Amoeboid hit.");
        var parent = _actors[source.InstanceIndex];
        // Source FUN_L01_002eec68 ignores the damage record magnitude after
        // validating a nonzero contact; each accepted hit subtracts one HP.
        parent.Health = Math.Max(0, parent.Health - 1);
        BinaryPrimitives.WriteSingleLittleEndian(parent.Native.MutablePVar.AsSpan(0x20), parent.Health);
        var children = new List<Rac1AmoeboidFrame>(2);
        if (parent.Health > 0 && parent.Group != 255 && parent.Facts.SplitChildClassId is int childClass &&
            _groups.TryGetValue(parent.Group, out var members))
        {
            foreach (var child in members)
            {
                if (children.Count == 2) break;
                if (child.Native.NativeClassId != childClass ||
                    child.Native.State.NativeState != Rac1AmoeboidAuthored.DormantPoolState ||
                    child.UsedReserve) continue;
                child.Position = parent.Position;
                child.Yaw = parent.Yaw;
                child.Health = parent.Health;
                BinaryPrimitives.WriteSingleLittleEndian(child.Native.MutablePVar.AsSpan(0x20), child.Health);
                // Native 0x2ef350 copies the parent's actual attack range
                // (+0x224) into each reused child, rather than leaving the
                // reserve's originally authored detection radius in place.
                BinaryPrimitives.WriteSingleLittleEndian(
                    child.Native.MutablePVar.AsSpan(0x224),
                    BinaryPrimitives.ReadSingleLittleEndian(parent.Native.PVar.Span[0x224..]));
                child.UsedReserve = true;
                Activate(child, KnockbackState, 0);
                children.Add(Frame(child));
            }
        }
        _runtime.Terminalize(parent.Native, Rac1MobyRuntime.TerminalNativeStateFd);
        return new(Frame(parent), children, parent.Health <= 0);
    }

    private static float AttackAdmission(Entry e)
    {
        float authored = BinaryPrimitives.ReadSingleLittleEndian(e.Native.PVar.Span[0x224..]);
        return authored > 0 && float.IsFinite(authored) ? authored : 15f;
    }

    private bool TryPathMove(Entry entry, Vector3 position, Vector3 target, out Vector3 movement)
    {
        Vector3 waypoint = target;
        if (_pathRouter is not null && _segmentIntersects is not null &&
            !_pathRouter.TryFollowPath(entry.Facts.PathIndex, entry.Facts.NextPathIndex,
                Size(entry.Native.NativeClassId) * 0.8f, position, target,
                _segmentIntersects, out waypoint))
        {
            movement = Vector3.Zero;
            return false;
        }
        movement = MoveTowards(entry, position, waypoint);
        return true;
    }

    private static Vector3 MoveTowards(Entry e, Vector3 position, Vector3 target)
    {
        Vector2 difference = new(target.X - position.X, target.Y - position.Y);
        if (difference.LengthSquared() < 1e-8f) return Vector3.Zero;
        e.Yaw = MathF.Atan2(difference.Y, difference.X);
        // Explicit host substitute for the source 0x258b50 world/path mover.
        float step = MathF.Min(difference.Length(), Size(e.Native.NativeClassId) * .8f / 60);
        var direction = Vector2.Normalize(difference) * step;
        return new(direction.X, direction.Y, 0);
    }

    private static float Size(int cls) => cls switch { 572 => 1f, 865 => .7f, 866 => .49f, _ => 0 };

    private void Activate(Entry entry, int state, int sequence)
    {
        _runtime.SetNativeState(entry.Native, state);
        _runtime.SetPresence(entry.Native, RuntimeEntityPresence.Active);
        entry.Sequence = sequence;
    }

    private void Transition(Entry entry, int state, int sequence)
    {
        _runtime.SetNativeState(entry.Native, state);
        entry.Sequence = sequence;
    }

    private static Rac1AmoeboidFrame Frame(Entry e) =>
        new(e.Native.InstanceIndex, e.Native.NativeClassId, e.Group, e.Native.State.NativeState,
            e.Native.IsActive, e.Health, e.Position, e.Yaw, e.Sequence, Vector3.Zero, false);

    private static bool Finite(Vector3 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    private sealed class FamilyDamageConsumer(Rac1AmoeboidFamilySession session, int cls) : IRac1MobyDamageConsumer
    {
        public int NativeClassId => cls;
        public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
            session.CanApplyDamage(source, damage);
        public object ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
            session.ApplyDamage(source, damage);
    }
}

public sealed record Rac1AmoeboidFrame(int InstanceIndex, int ClassId, int Group, int NativeState,
    bool Active, float Health, Vector3 Position, float Yaw, int Sequence, Vector3 Displacement, bool Attack);

public sealed record Rac1AmoeboidDamageResult(Rac1AmoeboidFrame Parent,
    IReadOnlyList<Rac1AmoeboidFrame> Children, bool Depleted);

using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded 60-Hz class-827 native state owner. State admission, animation
/// selectors, 2-unit attack entrance and 10..15 animation contact window are
/// recovered from L06:0x2e8678 and the native hit helper 0x2e94f0.
/// Horizontal steering, target acquisition and missing common ground/path
/// solvers are explicitly provisional host policy (see research).
/// </summary>
public sealed class Rac1FrogSession : IRac1MobyDamageConsumer
{
    public const int ClassId = Rac1FrogAuthored.ClassId;
    public const float AttackEntryDistance = 2f;
    public const float PathPointDistance = .5f;
    public const float RetreatCompletionDistance = 2f;
    public const float NativeTicksPerSecond = 60f;
    public const int AttackSequence = 5;
    public const int RoamMinTicks = 45;
    public const int RoamMaxTicks = 75;
    public const float RoamMinRadius = .5f;
    public const float RoamMaxRadius = 2f;
    public const int CarrierChildClassId = Rac1BoltCrate.NativeClassId;
    public const float CarrierReleaseDistance = 4f;
    public const int CarrierIgnoredHitSourceClass = 0xaf;
    public const float NativeLaunchGravityPerTick = 20f / (NativeTicksPerSecond * NativeTicksPerSecond);
    public const float NativeLaunchRisePerTick = 2f / NativeTicksPerSecond;
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly IReadOnlyList<Vector3[]> _paths;
    private readonly Dictionary<int, Entry> _entries = [];

    private sealed class Entry(Rac1MobyRuntimeInstance native, Rac1FrogAuthored authored, Vector3 home, float yaw)
    {
        public Rac1MobyRuntimeInstance Native { get; } = native;
        public Rac1FrogAuthored Authored { get; } = authored;
        public Vector3 Home { get; } = home;
        public float Yaw { get; set; } = yaw;
        public int Sequence { get; set; } = authored.InitialState == 3 ? 0 : 4;
        public float Health { get; set; } = authored.Health;
        public int Lives { get; set; } = authored.RespawnLives;
        public int PathPoint { get; set; }
        public Vector3 RoamGoal { get; set; } = home;
        public int RoamTicks { get; set; }
        public bool AttackEmitted { get; set; }
        public bool CarrierChildOwned { get; set; }
        public bool CarrierReleased { get; set; }
        public float LaunchVerticalVelocity { get; set; }
        public uint RandomState { get; set; } = (uint)(native.InstanceIndex + 1) * 747796405u + 2891336453u;
    }

    public Rac1FrogSession(Rac1MobyRuntimeSession runtime, IReadOnlyList<Vector3[]> paths)
    {
        _runtime = runtime;
        _paths = paths;
        runtime.RegisterDamageConsumer(this);
    }

    public int NativeClassId => ClassId;
    public int RegisteredCount => _entries.Count;
    public int PresentedCount => _entries.Values.Count(entry => entry.Native.IsActive);
    public bool ShuttleSwitchEnabled { get; private set; }

    /// <summary>Apply only an event from the LEVEL6 class-1109 shuttle. The
    /// native global D_L06_0015FDF8 selects exactly one 16-bit +0x242
    /// population; carrier, hidden/respawn and terminal actors stay parked.</summary>
    public IReadOnlyList<int> ApplyShuttleSwitch(bool enabled)
    {
        if (ShuttleSwitchEnabled == enabled) return [];
        ShuttleSwitchEnabled = enabled;
        var changed = new List<int>();
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            bool shouldPresent = !entry.Native.State.IsTerminalized &&
                (entry.Authored.HasCarrier == 0 || entry.CarrierReleased) &&
                entry.Native.State.NativeState != 15 &&
                entry.Authored.UpdateEligible(enabled);
            if (entry.Native.IsActive == shouldPresent) continue;
            _runtime.SetPresence(entry.Native,
                shouldPresent ? RuntimeEntityPresence.Active : RuntimeEntityPresence.Inactive);
            changed.Add(pair.Key);
        }
        return changed;
    }

    public Rac1FrogFrame Register(RuntimeDynamicObject source, RuntimeEntityState state,
        Vector3 home, float yaw = 0, bool switchEnabled = false)
    {
        if (!Finite(home) || !float.IsFinite(yaw)) throw new ArgumentOutOfRangeException(nameof(home));
        var authored = Rac1FrogAuthored.Read(source);
        if (authored.PathIndex >= _paths.Count ||
            authored.PathIndex >= 0 && _paths[authored.PathIndex].Length < 2)
            throw new InvalidDataException("Authored Frog path cannot be resolved from this level.");
        if (_entries.ContainsKey(source.InstanceIndex)) throw new InvalidOperationException("Duplicate authored Frog.");
        if (_entries.Count == 0) ShuttleSwitchEnabled = switchEnabled;
        else if (ShuttleSwitchEnabled != switchEnabled)
            throw new InvalidOperationException("Cannot register one Frog population with inconsistent shuttle switch values.");
        var payload = source.NativePayloads!.Single(p => p.Format == Rac1BoltCrate.PVarPayloadFormat).Data;
        var native = _runtime.Register(source, state, authored.InitialState, payload);
        if (!authored.OrdinaryHostAdmission(switchEnabled))
            _runtime.SetPresence(native, RuntimeEntityPresence.Inactive);
        var entry = new Entry(native, authored, home, yaw);
        _entries.Add(source.InstanceIndex, entry);
        return Snapshot(entry);
    }

    public Rac1FrogFrame Get(int instance) => Snapshot(_entries[instance]);
    public Rac1FrogAuthored GetAuthored(int instance) => _entries[instance].Authored;

    /// <summary>
    /// FUN_L06_002e9d10, invoked only by a confirmed native producer:
    /// the Queen's listed wave or the clip-volume animation trigger.
    /// Ordinary target proximity, shuttle changes and elapsed time are
    /// never sufficient to release a source hidden/state-15 Frog.
    /// </summary>
    public Rac1FrogLaunch? LaunchFromNativeProducer(int instance, Vector3 launchPosition,
        Rac1FrogLaunchProducer producer)
    {
        if (!Finite(launchPosition)) throw new ArgumentOutOfRangeException(nameof(launchPosition));
        if (producer is not (Rac1FrogLaunchProducer.QueenGroup or Rac1FrogLaunchProducer.IntervalTrigger))
            throw new ArgumentOutOfRangeException(nameof(producer));
        var e = _entries[instance];
        if (e.Native.State.NativeState != 15 || e.Native.State.IsTerminalized ||
            !e.Authored.UpdateEligible(ShuttleSwitchEnabled) ||
            e.CarrierChildOwned || e.CarrierReleased)
            return null;
        e.Yaw = NextFloat(e) * MathF.Tau;
        e.LaunchVerticalVelocity = NativeLaunchRisePerTick;
        // These exact native PVar writes originate from FUN_L06_002e9d10:
        // +0x130 gravity, +0x138/+0x13c initial rise, +0x144 mode 5,
        // +0x15d selector 3. Remaining vendor arc solver is host-adapted.
        var pvar = e.Native.MutablePVar.AsSpan();
        BinaryPrimitives.WriteSingleLittleEndian(pvar[0x130..], NativeLaunchGravityPerTick);
        BinaryPrimitives.WriteSingleLittleEndian(pvar[0x138..], NativeLaunchRisePerTick);
        BinaryPrimitives.WriteSingleLittleEndian(pvar[0x13c..], NativeLaunchRisePerTick);
        BinaryPrimitives.WriteInt32LittleEndian(pvar[0x144..], 5);
        pvar[0x15d] = 3;
        SetState(e, 6, e.Sequence);
        _runtime.SetPresence(e.Native, RuntimeEntityPresence.Active);
        return new(Snapshot(e), launchPosition, producer);
    }

    /// <summary>Native FUN_L06_002e94f0 checks the owned class-500 child's
    /// hit slot first, ignores class 0xAF, bursts the crate and removes the
    /// parent Frog completely. This is *not* the proximity release into state8.</summary>
    public bool HitCarrierChild(int instance, int? hitSourceClass)
    {
        var e = _entries[instance];
        if (!e.CarrierChildOwned || e.Native.State.NativeState != 1 ||
            e.Native.State.IsTerminalized ||
            !e.Authored.UpdateEligible(ShuttleSwitchEnabled) ||
            hitSourceClass == CarrierIgnoredHitSourceClass)
            return false;
        e.CarrierChildOwned = false;
        _runtime.Terminalize(e.Native);
        return true;
    }

    /// <summary>Record successful creation of the native class-500 child at
    /// the authored Frog. The child is dynamic and belongs to this parent;
    /// never register it as an authored crate or grant bolts for its release.</summary>
    public Rac1FrogFrame BindCarrierChild(int instance, int childClassId)
    {
        var e = _entries[instance];
        if (e.Authored.HasCarrier == 0 || e.Native.State.NativeState != 1 ||
            e.CarrierReleased || e.CarrierChildOwned || e.Native.State.IsTerminalized)
            throw new InvalidOperationException("Frog has no available native carrier-child slot.");
        if (childClassId != CarrierChildClassId)
            throw new ArgumentException("Native Frog carrier child must be R&C1 class 500.", nameof(childClassId));
        e.CarrierChildOwned = true;
        return Snapshot(e);
    }

    /// <summary>
    /// Caller supplies native XYZ, an *eligible* native target (not a decoy
    /// substituted for an unresolved authored target), and animation facts.
    /// Only a successfully bound class-500 carrier child can enter the
    /// native state1 release path. Hidden and switch-gated forms are not
    /// admitted by ordinary target proximity.
    /// </summary>
    public Rac1FrogFrame Step(int instance, Vector3 position, Vector3 target,
        bool targetEligible, bool animationWrapped = false,
        float animationPosition = 0, bool attackTouchesPlayer = false,
        bool grounded = true, bool roamingAllowed = true)
    {
        if (!Finite(position) || !Finite(target) || !float.IsFinite(animationPosition))
            throw new ArgumentOutOfRangeException(nameof(position));
        var e = _entries[instance];
        // Native state1 is visually hidden behind a spawned class-500 child.
        // It must still receive the four-unit release check when the child's
        // presentation has actually been created and the shuttle gate admits it.
        if (e.Native.State.NativeState == 1 && e.CarrierChildOwned &&
            e.Authored.UpdateEligible(ShuttleSwitchEnabled) && targetEligible &&
            Vector3.Distance(position, target) < CarrierReleaseDistance)
        {
            e.CarrierChildOwned = false;
            e.CarrierReleased = true;
            SetState(e, 8, 4);
            _runtime.SetPresence(e.Native, RuntimeEntityPresence.Active);
            return Snapshot(e);
        }
        if (!e.Native.IsActive) return Snapshot(e);
        var state = e.Native.State.NativeState;
        Vector3 movement = Vector3.Zero;
        bool contact = false;
        switch (state)
        {
            case 3: // Source route entrance, before node-by-node state 4.
                SetState(e, 4, 4);
                break;
            case 4:
                var points = _paths[e.Authored.PathIndex];
                movement = Travel(e, position, points[Math.Min(e.PathPoint, points.Length - 1)]);
                if (Vector2.Distance(new(position.X, position.Y),
                        new(points[e.PathPoint].X, points[e.PathPoint].Y)) < PathPointDistance)
                {
                    if (e.PathPoint < points.Length - 2) e.PathPoint++;
                    else SetState(e, 5, 4);
                }
                break;
            case 5: // Retail transitions via mine_fall bit 1; supplied by host.
                if (grounded) SetState(e, 7, 3);
                break;
            case 6: // Native launch: gravity 20 frame_time^2, rise 2 frame_time.
                if (grounded) SetState(e, 7, 3);
                else
                {
                    movement = Vector3.UnitZ * e.LaunchVerticalVelocity;
                    e.LaunchVerticalVelocity -= NativeLaunchGravityPerTick;
                }
                break;
            case 7:
                if (animationWrapped) SetState(e, 8, 4);
                break;
            case 8:
                // Source state8 chooses a home-relative wander destination
                // within radius .5..2 and a 45..75-tick refresh. Native Moby
                // +0x31 activation and exact PRNG are not yet recovered;
                // this movement is an opt-in host fallback for admitted frogs.
                if (roamingAllowed)
                {
                    if (e.RoamTicks <= 0 || Vector3.Distance(position, e.RoamGoal) < .05f)
                    {
                        float angle = NextFloat(e) * 2f * MathF.PI;
                        float radius = RoamMinRadius + NextFloat(e) * (RoamMaxRadius - RoamMinRadius);
                        e.RoamGoal = e.Home + new Vector3(MathF.Cos(angle) * radius,
                            MathF.Sin(angle) * radius, 0);
                        e.RoamTicks = RoamMinTicks + NextInt(e, RoamMaxTicks - RoamMinTicks + 1);
                    }
                    else e.RoamTicks--;
                    movement = Travel(e, position, e.RoamGoal);
                }
                // Retail chooses a random entrance to 9 (1/9 per update)
                // if its native target mode is not 2. Target *selection* is a
                // separate common function. The host radius below is fallback.
                if (targetEligible && NextInt(e, 9) == 0) SetState(e, 9, 4);
                break;
            case 9:
                if (!targetEligible) { SetState(e, 13, 4); break; }
                movement = Travel(e, position, target);
                if (Vector3.Distance(position, target) < AttackEntryDistance)
                    SetState(e, 12, AttackSequence);
                break;
            case 10:
                // The alternate near-volume predicate +0x1ec is not recovered;
                // do not invent admission to state 11 from generic distance.
                if (!targetEligible) SetState(e, 13, 4);
                else if (Vector3.Distance(position, target) < AttackEntryDistance)
                    SetState(e, 12, AttackSequence);
                else movement = Travel(e, position, target);
                break;
            case 11:
                if (animationWrapped) SetState(e, 10, 4);
                break;
            case 12:
                if (animationWrapped) e.AttackEmitted = false;
                // Original uses current animation frame window [10,15],
                // FUN_L00_002599e8. A single event per loop avoids repeatedly
                // hitting Ratchet at every 60-Hz subframe in the window.
                if (animationPosition is >= 10f and <= 15f)
                {
                    // Source state12 doubles native forward movement during
                    // frames 10..15. Keep testing contact across the entire
                    // window; a missed first frame must not suppress later hits.
                    movement = Travel(e, position, target) * 2;
                    if (!e.AttackEmitted && attackTouchesPlayer)
                    {
                        e.AttackEmitted = true;
                        contact = true;
                    }
                }
                if (animationWrapped && Vector3.Distance(position, target) > AttackEntryDistance)
                    SetState(e, 9, 4);
                break;
            case 13:
                movement = Travel(e, position, e.Home);
                if (Vector3.Distance(position, e.Home) < RetreatCompletionDistance)
                    SetState(e, 8, 4);
                else if (targetEligible) SetState(e, 9, 4);
                break;
                // 1: carried child, 15: hidden/respawn, 16: grabbed. The source
                // needs a different Moby/global transition to admit these forms.
        }
        return Snapshot(e, movement, contact);
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        source.SourceGame == "rac1" && source.NativeClassId == ClassId &&
        _entries.TryGetValue(source.InstanceIndex, out var e) && e.Native.IsActive && e.Health > 0 &&
        damage.Target.MatchesMoby(e.Native.Key) &&
        (Rac1EnemyDamage.IsPlayerWeapon(damage) ||
            Rac1SeekerExplosion.IsNativeExplosion(damage) ||
            Rac1ExplosiveCrateBlast.IsNativeMobyBlast(damage));

    object IRac1MobyDamageConsumer.ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
        => ApplyDamage(source, damage);

    public Rac1FrogDamageResult ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage)) throw new InvalidOperationException("Frog damage not admitted.");
        var e = _entries[source.InstanceIndex];
        // Native 0x2e94f0 has a common hit code classifier. Until its exact
        // response code reaches this session, positive admitted damage and
        // actual PVar health are the bounded host approximation.
        float amount = Rac1EnemyDamage.ResolveNativeDamage(damage, e.Native.PVar.Span);
        if (!float.IsFinite(amount) || amount <= 0) throw new InvalidDataException("Invalid Frog damage.");
        e.Health = Math.Max(0, e.Health - amount);
        BinaryPrimitives.WriteSingleLittleEndian(e.Native.MutablePVar.AsSpan(0x20), e.Health);
        if (e.Health > 0) return new(Snapshot(e), false, false);
        if (e.Lives > 0)
        {
            e.Lives--;
            e.Health = e.Authored.Health;
            BinaryPrimitives.WriteInt16LittleEndian(e.Native.MutablePVar.AsSpan(0x240), (short)e.Lives);
            BinaryPrimitives.WriteSingleLittleEndian(e.Native.MutablePVar.AsSpan(0x20), e.Health);
            SetState(e, 15, e.Sequence);
            e.LaunchVerticalVelocity = 0;
            _runtime.SetPresence(e.Native, RuntimeEntityPresence.Inactive);
            return new(Snapshot(e), false, true);
        }
        _runtime.Terminalize(e.Native);
        return new(Snapshot(e), true, false);
    }

    private static Vector3 Travel(Entry e, Vector3 from, Vector3 to)
    {
        var planar = new Vector2(to.X - from.X, to.Y - from.Y);
        if (planar.LengthSquared() < 1e-9f) return Vector3.Zero;
        e.Yaw = MathF.Atan2(planar.Y, planar.X);
        float speed = e.Authored.NativeSpeed / NativeTicksPerSecond;
        var xy = Vector2.Normalize(planar) * MathF.Min(speed, planar.Length());
        return new(xy.X, xy.Y, 0);
    }

    private static float NextFloat(Entry e) => NextInt(e, 1 << 24) / (float)(1 << 24);

    private static int NextInt(Entry e, int max)
    {
        e.RandomState ^= e.RandomState << 13;
        e.RandomState ^= e.RandomState >> 17;
        e.RandomState ^= e.RandomState << 5;
        return (int)(e.RandomState % (uint)max);
    }

    private void SetState(Entry e, int state, int sequence)
    {
        _runtime.SetNativeState(e.Native, state);
        e.Sequence = sequence;
        e.AttackEmitted = false;
    }

    private static Rac1FrogFrame Snapshot(Entry e, Vector3 displacement = default, bool attack = false) =>
        new(e.Native.State.NativeState, e.Sequence, e.Health, e.Lives, e.Yaw,
            displacement, e.Native.IsActive, attack, e.Native.State.IsTerminalized,
            e.Authored.PathIndex, e.Authored.HasCarrier != 0, e.Authored.StartsHidden != 0,
            e.Authored.SwitchSelector != 0, e.CarrierChildOwned, e.CarrierReleased);

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public sealed record Rac1FrogFrame(int NativeState, int Sequence, float Health,
    int Lives, float Yaw, Vector3 Displacement, bool Presented, bool Contact,
    bool Terminal, int PathIndex, bool CarrierMode, bool StartsHidden, bool SwitchGated,
    bool CarrierChildOwned, bool CarrierReleased);
public enum Rac1FrogLaunchProducer
{
    QueenGroup,
    IntervalTrigger
}

public sealed record Rac1FrogLaunch(Rac1FrogFrame Frame, Vector3 NativePosition,
    Rac1FrogLaunchProducer Producer);

public sealed record Rac1FrogDamageResult(Rac1FrogFrame Frame, bool Lethal, bool RespawnParked);

using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native class-193 Seeker hover/pursuit from R&C1 LEVEL9/LEVEL15.
/// States and numerical steering rules are source-backed by
/// L09:0x2c5990; physical sweeps and knockback remain host adapters.
/// </summary>
public sealed class Rac1SeekerSession : IRac1MobyDamageConsumer
{
    public const int ClassId = 193;
    public const int PVarSize = 0x270;
    public const float InitialHealth = 3f;
    public const float SpeedPerTick = 7.75f / 60;
    public const int TimedChaseTicks = 180;
    public const float StopDistance = 1.4f;
    public const float ResumeDistance = 1.9f;
    public const float ChaseAnimationDistance = 6f;
    public const int HurtState = 5;
    public const int DeathState = 6;
    public const int ExplosionState = 7;
    public const float NativeKnockbackExitSpeed = 2f / 60;
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Dictionary<int, Entry> _entries = [];

    private sealed class Entry(Rac1MobyRuntimeInstance native, int groupMode, float yaw)
    {
        public Rac1MobyRuntimeInstance Native { get; } = native;
        public int TargetMode { get; } = groupMode;
        public float Health { get; set; } = InitialHealth;
        public float Yaw { get; set; } = yaw;
        public Rac1MobySteering Heading { get; } = new(yaw);
        public int Sequence { get; set; } = 1;
        public bool Moving { get; set; } = true;
        public int Countdown { get; set; }
    }

    public Rac1SeekerSession(Rac1MobyRuntimeSession runtime)
    {
        _runtime = runtime;
        _runtime.RegisterDamageConsumer(this);
    }

    public int NativeClassId => ClassId;
    public int RegisteredCount => _entries.Count;

    public Rac1SeekerFrame Register(RuntimeDynamicObject source, RuntimeEntityState state,
        float sourceYaw = 0f)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != ClassId)
            throw new ArgumentException("Expected native class-193 Seeker.", nameof(source));
        byte[]? pvar = source.NativePayloads?.SingleOrDefault(p =>
            p.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (pvar is not { Length: PVarSize })
            throw new InvalidDataException("Seeker requires its exact 0x270-byte authored PVar.");
        int pathGate = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x180));
        int mode = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x164));
        if (pathGate == -1)
            throw new InvalidDataException("This source Seeker is disabled by its native path sentinel.");
        var copy = pvar.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(copy.AsSpan(0x20), InitialHealth);
        BinaryPrimitives.WriteSingleLittleEndian(copy.AsSpan(0xf4), SpeedPerTick);
        copy[0x28] = 2; // Native initialize assigns the Seeker's damage-kind byte.
        var native = _runtime.Register(source, state, nativeState: 0, pvar: copy);
        var entry = new Entry(native, mode, sourceYaw);
        if (!_entries.TryAdd(source.InstanceIndex, entry))
            throw new InvalidOperationException("Duplicate source Seeker.");
        return Snapshot(entry, Vector3.Zero);
    }

    public Rac1SeekerFrame Get(int instance) => Snapshot(_entries[instance], Vector3.Zero);

    /// <summary>
    /// Step one 60 Hz source tick. In native XYZ, chase velocity follows the
    /// target at 7.75 units/sec, with distinct 1.4/1.9-unit movement thresholds
    /// and source 0.1/0.05 vertical corrections around Ratchet.
    /// </summary>
    public Rac1SeekerFrame Step(int instance, Vector3 position, Vector3 target,
        bool animationCompleted, float nativeFrameScale = 1f,
        bool knockbackSettled = false, bool hitTerrain = false, float? heroHeight = null)
    {
        if (!Finite(position) || !Finite(target) || !float.IsFinite(nativeFrameScale) || nativeFrameScale < 0 ||
            heroHeight.HasValue && !float.IsFinite(heroHeight.Value))
            throw new ArgumentOutOfRangeException(nameof(position));
        var e = _entries[instance];
        int state = e.Native.State.NativeState;
        Vector3 displacement = Vector3.Zero;
        bool exploded = false, fatalBurst = false;
        if (!e.Native.IsActive) return Snapshot(e, displacement);
        // The Seeker's damage helper 0x2c6230 runs *before* its main update
        // every native tick, including hurt knockback. Its 0x184 timer must
        // keep counting while state 5 is active, not pause the fuse.
        if (e.Countdown > 0 && state != ExplosionState)
        {
            SetCountdown(e, e.Countdown - 1);
            if (e.Countdown == 0)
            {
                SetState(e, ExplosionState, e.Sequence);
                return Snapshot(e, displacement);
            }
        }
        switch (state)
        {
            case 0:
                SetState(e, 1, 1);
                break;
            case 1:
                if (e.TargetMode != 2) SetState(e, 2, 1);
                break;
            case 3:
                if (animationCompleted)
                {
                    SetState(e, 4, 4);
                    if (e.Countdown == 0) SetCountdown(e, TimedChaseTicks);
                }
                break;
            case 2:
            case 4:
                Vector2 aim = new(target.X - position.X, target.Y - position.Y);
                float planar = aim.Length();
                if (planar > 1e-6f)
                {
                    float want = MathF.Atan2(aim.Y, aim.X);
                    e.Yaw = e.Heading.Step(want,
                        8.726646f / 3600f, 8.726646f / 3600f,
                        12.566371f / 60f, angular: true);
                }
                if (planar > ResumeDistance) e.Moving = true;
                else if (planar < StopDistance) e.Moving = false;
                // Planar target can be a Decoy; retail hover tracks Ratchet
                // (hero.motion.pos.z), not the planar target's vertical height.
                float zGoal = (heroHeight ?? target.Z) + (e.Moving ? .8f : .2f);
                float dz = (zGoal - position.Z) * (position.Z < zGoal ? .1f : .05f);
                dz = Math.Clamp(dz, -nativeFrameScale * .3f, nativeFrameScale * .3f);
                displacement = new(e.Moving ? MathF.Cos(e.Yaw) * SpeedPerTick : 0,
                    e.Moving ? MathF.Sin(e.Yaw) * SpeedPerTick : 0, dz);
                if (state == 2 && planar < ChaseAnimationDistance)
                    SetState(e, 3, 3);
                break;
            case HurtState:
                // Native 0x2c5990 preserves hover height while mine_fall
                // damps planar velocity. The Godot host supplies the common
                // 0x25c698 collision/velocity result, not a guessed timer.
                if (knockbackSettled)
                {
                    SetState(e, 4, 4);
                    e.Moving = true;
                    if (e.Countdown == 0) SetCountdown(e, TimedChaseTicks);
                }
                break;
            case DeathState:
                // Native mine_fall's 0x121 impact bit ends the fatal arc.
                // No extra damage or fixed-lifetime removal is fabricated.
                if (hitTerrain)
                {
                    fatalBurst = true;
                    _runtime.Terminalize(e.Native, Rac1MobyRuntime.TerminalNativeStateFd);
                }
                break;
            case ExplosionState:
                // Native 0x2c5990 calls the common explosion routine exactly
                // once, then removes the Moby.
                exploded = true;
                _runtime.Terminalize(e.Native, Rac1MobyRuntime.TerminalNativeStateFd);
                break;
        }
        return Snapshot(e, displacement, exploded, fatalBurst);
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        source.SourceGame == "rac1" && source.NativeClassId == ClassId &&
        _entries.TryGetValue(source.InstanceIndex, out var entry) && entry.Native.IsActive &&
        entry.Health > 0 && damage.Target.MatchesMoby(entry.Native.Key) &&
        (Rac1EnemyDamage.IsPlayerWeapon(damage) || Rac1SeekerExplosion.IsNativeExplosion(damage));

    object IRac1MobyDamageConsumer.ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        ApplyDamage(source, damage);

    public Rac1SeekerDamageResult ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage))
            throw new InvalidOperationException("Seeker damage was not admitted.");
        var e = _entries[source.InstanceIndex];
        float amount = Rac1EnemyDamage.ResolveNativeDamage(damage, e.Native.PVar.Span);
        if (!float.IsFinite(amount) || amount <= 0)
            throw new InvalidDataException("Seeker damage amount is invalid.");
        e.Health = Math.Max(0, e.Health - amount);
        BinaryPrimitives.WriteSingleLittleEndian(e.Native.MutablePVar.AsSpan(0x20), e.Health);
        // Native response 1/2 takes fatal 6, 3..8 normally take hurt 5.
        // Hit classification from 0x213928 is not yet fully transported to
        // the host, so this uses source HP to choose a valid reaction branch.
        if (e.Health <= 0) SetState(e, DeathState, e.Sequence);
        else
        {
            SetState(e, HurtState, e.Sequence);
            if (e.Countdown == 0) SetCountdown(e, TimedChaseTicks);
        }
        return new(Snapshot(e, Vector3.Zero), e.Health <= 0);
    }

    private void SetCountdown(Entry entry, int count)
    {
        entry.Countdown = count;
        BinaryPrimitives.WriteInt32LittleEndian(entry.Native.MutablePVar.AsSpan(0x184), count);
    }

    private void SetState(Entry entry, int nativeState, int sequence)
    {
        _runtime.SetNativeState(entry.Native, nativeState);
        entry.Sequence = sequence;
    }

    private static Rac1SeekerFrame Snapshot(Entry e, Vector3 delta,
        bool exploded = false, bool fatalBurst = false) => new(
        e.Native.State.NativeState, e.Sequence, e.Health, e.Yaw, delta, e.Countdown,
        !e.Native.IsActive, exploded, fatalBurst);

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public sealed record Rac1SeekerFrame(
    int NativeState, int Sequence, float Health, float Yaw,
    Vector3 Displacement, int Countdown, bool Terminal,
    bool ExplosionTriggered = false, bool FatalBurst = false);
public sealed record Rac1SeekerDamageResult(Rac1SeekerFrame Frame, bool Lethal);

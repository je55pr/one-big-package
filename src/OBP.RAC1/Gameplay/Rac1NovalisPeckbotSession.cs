using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native Novalis class-577 selectors grounded in peckbot_update_l1
/// (RAC1 L1:0x002efc60; rac1-native-game@3fd14c1).
/// The authored sleep/rise, path walker and enemy_physics solvers still require
/// host adapters, so their timing and displacement are not claimed as parity.
/// </summary>
public sealed class Rac1NovalisPeckbotSession : IRac1MobyDamageConsumer, IRac1MobyClassController
{
    public const int ClassId = 577;
    public const int PVarSize = 0x270;
    public const float InitialHealth = 1f;
    public const float AlertRadius = 24f;
    public const float WakeRadius = 28f;
    public const float AttackDistance = 1.5f;
    public const float PeckDamageRadius = 2f;
    public const float PeckHeightLimit = .5f;
    public const float TargetHeightLimit = 8f;
    public const float PeckAngleLimit = .2618f;
    public const float PeckAnimationTime = 13f;
    public const int AlertRandomModulus = 19;
    public const int PeckState = 4;
    public const int KnockbackState = 6;
    public const int DeathState = 0x63;

    public sealed record Facts(Vector3 Position, Vector3 Target,
        float FacingError = 0, float AnimationPosition = 0,
        bool AnimationWrapped = false, bool AnimationBlending = false,
        bool PlayerAlive = true);
    public sealed record Probe(Rac1MobyRuntimeInstance Instance, int NativeState,
        int Sequence, float Health, Vector3? Destination, bool PeckContact,
        bool Terminal, bool InitialLoiter)
    {
        public bool Dead => Health <= 0;
    }
    public sealed record DamageResult(Probe Probe, bool Lethal);
    private sealed class Entry(Rac1MobyRuntimeInstance instance, Vector3 home, bool loiter)
    {
        public Rac1MobyRuntimeInstance Instance { get; } = instance;
        public Vector3 Home { get; } = home;
        public bool InitialLoiter { get; } = loiter;
        public int Sequence { get; set; } = loiter ? 0 : 4;
        public Vector3? Destination { get; set; }
        public bool PeckContact { get; set; }
        public bool PeckSent { get; set; }
    }

    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Rac1PickupRandom _random;
    private readonly Dictionary<int, Entry> _entries = [];
    public int NativeClassId => ClassId;
    public int RegisteredCount => _entries.Count;

    public Rac1NovalisPeckbotSession(Rac1MobyRuntimeSession runtime, Rac1PickupRandom? random = null)
    {
        _runtime = runtime;
        _random = random ?? Rac1PickupRandom.FromSeed(0);
        runtime.RegisterController(this);
        runtime.RegisterDamageConsumer(this);
    }

    public Probe Register(RuntimeDynamicObject source, RuntimeEntityState state)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != ClassId)
            throw new ArgumentException("Only retail Novalis Peckbot class-577 instances are admitted.", nameof(source));
        var pvars = source.NativePayloads?.Where(p => p.Format == Rac1BoltCrate.PVarPayloadFormat).ToArray();
        if (pvars is not [{ Data.Length: PVarSize }])
            throw new InvalidDataException("Novalis Peckbot requires exactly one 0x270 PVar.");
        byte[] pvar = pvars[0].Data.ToArray();
        int placed = BinaryPrimitives.ReadInt16LittleEndian(pvar.AsSpan(0x20a));
        if (placed is not (0 or 1))
            throw new InvalidDataException("Unrecovered Novalis Peckbot initial placement selector.");
        var m = state.Presentation.Transform.Matrix;
        Vector3 home = new((float)m[12], (float)m[14], (float)m[13]);
        bool loiter = placed == 0;
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), InitialHealth);
        var instance = _runtime.Register(source, state, loiter ? 0xe : 1, pvar);
        var entry = new Entry(instance, home, loiter);
        if (!_entries.TryAdd(source.InstanceIndex, entry))
            throw new InvalidOperationException("Duplicate Peckbot placement.");
        return Snapshot(entry);
    }

    public Probe Get(int instanceIndex) => Snapshot(_entries[instanceIndex]);

    object IRac1MobyClassController.Update(RuntimeDynamicObject source, object facts) =>
        facts is Facts f ? Step(source.InstanceIndex, f)
            : throw new ArgumentException("Peckbot update requires class-577 facts.", nameof(facts));

    public Probe Step(int instanceIndex, Facts facts)
    {
        if (!Finite(facts.Position) || !Finite(facts.Target) ||
            !float.IsFinite(facts.FacingError) || !float.IsFinite(facts.AnimationPosition) ||
            facts.AnimationPosition < 0)
            throw new ArgumentOutOfRangeException(nameof(facts));
        var e = _entries[instanceIndex];
        e.Destination = null; e.PeckContact = false;
        if (!e.Instance.IsActive) return Snapshot(e);
        int state = e.Instance.State.NativeState;
        float planar = Planar(facts.Position, facts.Target);
        float elevation = MathF.Abs(facts.Position.Z - facts.Target.Z);
        bool inAlert = facts.PlayerAlive && elevation < TargetHeightLimit && planar < WakeRadius;
        switch (state)
        {
            case 0xe: // Native rise from burial is not yet reconstructed.
            case 1:
                if (inAlert && _random.Below(AlertRandomModulus) == 0)
                    Select(e, 2, 5);
                break;
            case 2:
                if (inAlert && _random.Below(AlertRandomModulus) == 0)
                    Select(e, 3, 2);
                else if (!inAlert)
                    Select(e, 5, 2);
                break;
            case 3:
                if (facts.PlayerAlive && planar < AttackDistance && elevation < PeckHeightLimit)
                    Select(e, PeckState, 3);
                else if (!inAlert)
                    Select(e, 5, 2);
                else
                    e.Destination = facts.Target;
                break;
            case PeckState:
                if (facts.AnimationWrapped) e.PeckSent = false;
                if (!e.PeckSent && !facts.AnimationBlending && facts.AnimationPosition >= PeckAnimationTime &&
                    facts.PlayerAlive && planar < PeckDamageRadius && elevation < PeckHeightLimit &&
                    MathF.Abs(facts.FacingError) < PeckAngleLimit)
                {
                    e.PeckSent = true; e.PeckContact = true;
                }
                if (facts.AnimationWrapped && planar >= AttackDistance)
                    Select(e, 3, 2);
                break;
            case 5:
                if (Planar(facts.Position, e.Home) < 2f)
                    Select(e, 1, 4);
                else if (inAlert)
                    Select(e, 3, 2);
                else
                    e.Destination = e.Home;
                break;
            case KnockbackState:
                if (facts.AnimationWrapped) Select(e, 3, 2);
                break;
            case DeathState:
                if (facts.AnimationWrapped)
                    _runtime.Terminalize(e.Instance);
                break;
        }
        return Snapshot(e);
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        source.NativeClassId == ClassId && _entries.TryGetValue(source.InstanceIndex, out var e) &&
        e.Instance.IsActive && Health(e) > 0 &&
        damage.Target.MatchesMoby(e.Instance.Key) && Rac1EnemyDamage.IsPlayerWeapon(damage);

    object IRac1MobyDamageConsumer.ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage) =>
        ApplyDamage(source, damage);

    public DamageResult ApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage))
            throw new InvalidOperationException("Peckbot cannot consume this damage event.");
        var e = _entries[source.InstanceIndex];
        float health = Health(e) - (float)damage.NativeDamage;
        BinaryPrimitives.WriteSingleLittleEndian(e.Instance.MutablePVar.AsSpan(0x20), health);
        Select(e, health <= 0 ? DeathState : KnockbackState, health <= 0 ? 7 : 6);
        return new(Snapshot(e), health <= 0);
    }

    private void Select(Entry e, int state, int clip)
    {
        _runtime.SetNativeState(e.Instance, state);
        e.Sequence = clip; e.PeckSent = false;
    }
    private static Probe Snapshot(Entry e) => new(e.Instance, e.Instance.State.NativeState,
        e.Sequence, Health(e), e.Destination, e.PeckContact, !e.Instance.IsActive, e.InitialLoiter);
    private static float Health(Entry e) =>
        BinaryPrimitives.ReadSingleLittleEndian(e.Instance.MutablePVar.AsSpan(0x20));
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
}

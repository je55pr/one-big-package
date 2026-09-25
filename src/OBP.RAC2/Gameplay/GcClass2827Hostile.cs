using System.Buffers.Binary;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed Aranos class-2827 hostile slice. LEVEL0 placement/order plus
/// the loaded class update identify this as the opening MSR I family.
/// </summary>
public sealed class GcClass2827HostileSession
{
    public const int NativeClassId = 2827;
    public const uint DamageQueryMask = 0x00010000;
    public const short HitCooldownTicks = 15;
    public const int OpeningRoomAuthoredMode = 1;
    public const int ChaseNativeState = 12;
    public const int AttackNativeState = 13;
    public const int AttackSequence16 = 0x10;
    public const int AttackSequence27 = 0x1B;
    public const float AttackContactFrameStart = 19f;
    public const float AttackContactFrameEnd = 25f;
    public static readonly float AttackEntryDistanceExclusive =
        BitConverter.Int32BitsToSingle(unchecked((int)0x40333333u));
    public static readonly float AttackFacingErrorExclusive =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3E567751u));

    private readonly RuntimeDynamicObject _source;
    private RuntimeEntityState _entityState;
    private float _health;
    private short _cooldown;
    private readonly float _attackContactExtent;

    private static readonly (int JointIndex, float Radius)[] AttackVolumeGeometry =
    [
        (0, 0.35f),
        (1, 0.15f),
        (2, 0.15f),
        (9, 0.35f),
    ];

    public GcClass2827HostileSession(
        RuntimeDynamicObject source,
        RuntimeEntityState? entityState = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        var authored = ReadAuthored(source)
            ?? throw new InvalidDataException("Missing class-2827 authored health state.");
        _entityState = entityState ?? RuntimeEntityState.FromAuthored(source);
        _entityState.EnsureMatches(source);
        _health = authored.Health;
        _cooldown = authored.HitCooldownTicks;
        _attackContactExtent = authored.AttackContactExtent;
    }

    public float Health => _health;
    public short CooldownTicks => _cooldown;
    public bool IsTerminal => _entityState.Presentation.Presence == RuntimeEntityPresence.Inactive;
    public RuntimeEntityState EntityState => _entityState;
    public float AttackContactExtent => _attackContactExtent;

    public static bool ShouldEnterAttack(double distance, double facingError) =>
        double.IsFinite(distance) && distance >= 0d &&
        double.IsFinite(facingError) &&
        distance < AttackEntryDistanceExclusive &&
        Math.Abs(facingError) < AttackFacingErrorExclusive;

    public GcClass2827AttackProbe ProbeAttackContact(int nativeSequence, float nativeFrame)
    {
        bool attackSequence = nativeSequence is AttackSequence16 or AttackSequence27;
        bool contactWindow = float.IsFinite(nativeFrame) &&
            nativeFrame >= AttackContactFrameStart &&
            nativeFrame <= AttackContactFrameEnd;
        var contacts = attackSequence && contactWindow
            ? AttackVolumeGeometry.Select(volume =>
                new GcClass2827AttackContact(
                    volume.JointIndex,
                    volume.Radius,
                    _attackContactExtent,
                    UnitScale: 1f)).ToArray()
            : [];
        return new(nativeSequence, nativeFrame, attackSequence, contactWindow, contacts);
    }

    public GcClass2827DamageResult Apply(GcGameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (!damage.Target.Matches(_source))
        {
            throw new InvalidOperationException(
                $"Damage target does not match {_source.InteractionId}.");
        }

        if (IsTerminal || _cooldown > 0 ||
            (damage.Damage.DamageFlags & DamageQueryMask) == 0 ||
            damage.Damage.DamageHp <= 0f)
        {
            return Snapshot(admitted: false);
        }
        _health = MathF.Max(0f, _health - damage.Damage.DamageHp);
        _cooldown = HitCooldownTicks;
        if (_health <= 0f)
        {
            _entityState = _entityState.WithPresence(RuntimeEntityPresence.Inactive);
        }

        return Snapshot(admitted: true);
    }

    public void TickCooldown(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        _cooldown = (short)Math.Max(0, _cooldown - ticks);
    }

    private GcClass2827DamageResult Snapshot(bool admitted) =>
        new(_health, _cooldown, admitted, IsTerminal, _entityState);

    public static GcClass2827AuthoredState? ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac2" || source.NativeClassId != NativeClassId)
        {
            return null;
        }
        var pvar = source.NativePayloads?
            .FirstOrDefault(payload => payload.Format == "rac2-pvar")?.Data;
        if (pvar is null || pvar.Length < 0x280)
        {
            return null;
        }

        int healthBits = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x20, 4));
        float health = BitConverter.Int32BitsToSingle(healthBits);
        short cooldown = BinaryPrimitives.ReadInt16LittleEndian(pvar.AsSpan(0x26, 2));
        float attackContactExtent = pvar[0x34];
        int authoredMode = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x27C, 4));
        if (!float.IsFinite(health) || health <= 0f || cooldown < 0 || attackContactExtent <= 0f)
        {
            return null;
        }

        return new GcClass2827AuthoredState(
            Health: health,
            HitCooldownTicks: cooldown,
            AttackContactExtent: attackContactExtent,
            AuthoredMode: authoredMode,
            AuthoredBolts: source.NativePayloads is null
                ? null
                : ReadAuthoredBolts(source.NativePayloads));
    }

    private static int? ReadAuthoredBolts(IReadOnlyList<RuntimeOpaquePayload> payloads)
    {
        var raw = payloads.FirstOrDefault(
            payload => payload.Format == "rac2-moby-instance-0x88")?.Data;
        return raw is { Length: >= 0x18 }
            ? BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x14, 4))
            : null;
    }
}

public sealed record GcClass2827AuthoredState(
    float Health,
    short HitCooldownTicks,
    float AttackContactExtent,
    int AuthoredMode,
    int? AuthoredBolts);

public sealed record GcClass2827AttackContact(
    int JointIndex,
    float Radius,
    float AuthoredExtent,
    float UnitScale);

public sealed record GcClass2827AttackProbe(
    int NativeSequence,
    float NativeFrame,
    bool IsAttackSequence,
    bool IsContactWindow,
    IReadOnlyList<GcClass2827AttackContact> Contacts);

public sealed record GcClass2827DamageResult(
    float Health,
    short HitCooldownTicks,
    bool Admitted,
    bool Terminal,
    RuntimeEntityState EntityState);

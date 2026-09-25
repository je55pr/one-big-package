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

    private readonly RuntimeDynamicObject _source;
    private RuntimeEntityState _entityState;
    private float _health;
    private short _cooldown;

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
    }

    public float Health => _health;
    public short CooldownTicks => _cooldown;
    public bool IsTerminal => _entityState.Presentation.Presence == RuntimeEntityPresence.Inactive;
    public RuntimeEntityState EntityState => _entityState;

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
        if (pvar is null || pvar.Length < 0x28)
        {
            return null;
        }

        int healthBits = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x20, 4));
        float health = BitConverter.Int32BitsToSingle(healthBits);
        short cooldown = BinaryPrimitives.ReadInt16LittleEndian(pvar.AsSpan(0x26, 2));
        if (!float.IsFinite(health) || health <= 0f || cooldown < 0)
        {
            return null;
        }

        return new GcClass2827AuthoredState(
            Health: health,
            HitCooldownTicks: cooldown,
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
    int? AuthoredBolts);

public sealed record GcClass2827DamageResult(
    float Health,
    short HitCooldownTicks,
    bool Admitted,
    bool Terminal,
    RuntimeEntityState EntityState);

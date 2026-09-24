namespace OBP.RAC1.Gameplay;

public enum Rac1GameplayEntityKind
{
    Player,
    Moby,
    Projectile,
}

/// <summary>
/// Stable engine-neutral reference carried by shared R&C1 gameplay events.
/// RuntimeId is an authored Moby instance index or a source-owned projectile id.
/// </summary>
public readonly record struct Rac1GameplayEntityRef(
    Rac1GameplayEntityKind Kind,
    int NativeClassId,
    long RuntimeId)
{
    public static Rac1GameplayEntityRef Player { get; } =
        new(Rac1GameplayEntityKind.Player, NativeClassId: 0, RuntimeId: 0);

    public static Rac1GameplayEntityRef Moby(Rac1MobyRuntimeKey key) =>
        new(Rac1GameplayEntityKind.Moby, key.NativeClassId, key.InstanceIndex);

    public static Rac1GameplayEntityRef Projectile(
        int nativeClassId,
        long projectileId)
    {
        if (nativeClassId < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeClassId));
        if (projectileId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectileId));

        return new(
            Rac1GameplayEntityKind.Projectile,
            nativeClassId,
            projectileId);
    }

    public bool MatchesMoby(Rac1MobyRuntimeKey key) =>
        Kind == Rac1GameplayEntityKind.Moby &&
        NativeClassId == key.NativeClassId &&
        RuntimeId == key.InstanceIndex;
}
/// <summary>
/// Recovered ownership provenance for a gameplay damage source. NativeClassId is
/// retained even when OBP cannot safely reconstruct the owner's runtime instance.
/// When RuntimeEntity is present it must agree with that native class.
/// </summary>
public readonly record struct Rac1GameplayDamageOwner(
    int NativeClassId,
    Rac1GameplayEntityRef? RuntimeEntity)
{
    public bool HasRuntimeIdentity => RuntimeEntity.HasValue;

    public static Rac1GameplayDamageOwner FromEntity(
        Rac1GameplayEntityRef entity) =>
        new(entity.NativeClassId, entity);

    public static Rac1GameplayDamageOwner NativeClass(int nativeClassId)
    {
        if (nativeClassId < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeClassId));

        return new(nativeClassId, RuntimeEntity: null);
    }

    public Rac1GameplayDamageOwner Validate()
    {
        if (NativeClassId < 0)
            throw new ArgumentOutOfRangeException(nameof(NativeClassId));
        if (RuntimeEntity is { } entity &&
            entity.NativeClassId != NativeClassId)
        {
            throw new InvalidDataException(
                "R&C1 damage owner runtime identity does not match its native class.");
        }

        return this;
    }
}

/// <summary>
/// Common gameplay damage/event transport. Optional native fields remain optional:
/// absence is evidence, not a request to synthesize a value.
/// </summary>
public sealed record Rac1GameplayDamageEvent
{
    public Rac1GameplayDamageEvent(
        Rac1GameplayEntityRef source,
        Rac1GameplayEntityRef target,
        double nativeDamage,
        uint? nativeDamageFlags = null,
        double? nativeMarker = null,
        Rac1NativeDamageHandoffKind? handoffKind = null,
        Rac1GameplayDamageOwner? owner = null)
    {
        if (!double.IsFinite(nativeDamage))
            throw new ArgumentOutOfRangeException(nameof(nativeDamage));
        if (nativeMarker.HasValue && !double.IsFinite(nativeMarker.Value))
            throw new ArgumentOutOfRangeException(nameof(nativeMarker));

        if (owner is { } recoveredOwner)
            recoveredOwner.Validate();

        Source = source;
        Owner = owner;
        Target = target;
        NativeDamage = nativeDamage;
        NativeDamageFlags = nativeDamageFlags;
        NativeMarker = nativeMarker;
        HandoffKind = handoffKind;
    }

    public Rac1GameplayEntityRef Source { get; }
    public Rac1GameplayDamageOwner? Owner { get; }
    public Rac1GameplayEntityRef Target { get; }
    public double NativeDamage { get; }
    public uint? NativeDamageFlags { get; }
    public double? NativeMarker { get; }
    public Rac1NativeDamageHandoffKind? HandoffKind { get; }

    public Rac1NativeDamageEnvelope? DamageEnvelope =>
        NativeDamageFlags is uint flags
            ? new Rac1NativeDamageEnvelope(NativeDamage, flags)
            : null;
}
public sealed record Rac1GameplayDamageDispatch(
    long Sequence,
    Rac1GameplayDamageEvent Damage);

/// <summary>
/// Sequenced engine-neutral transport for emitted R&C1 gameplay damage events.
/// Publishing is synchronous and does not apply consequences; target-specific
/// consumers remain separate from transport and observers do not change event
/// lifetime or native state.
/// </summary>
public sealed class Rac1DamageTransportSession
{
    private long _sequence;

    public long Sequence => _sequence;

    public event Action<Rac1GameplayDamageDispatch>? Published;

    public Rac1GameplayDamageDispatch Publish(Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var dispatch = new Rac1GameplayDamageDispatch(
            checked(++_sequence),
            damage);
        Published?.Invoke(dispatch);
        return dispatch;
    }
}

public static class Rac1DamageRuntime
{
    public static Rac1GameplayDamageEvent FromClass749Attack(
        Rac1MobyRuntimeKey source,
        Rac1Class749AttackEvent attack)
    {
        ArgumentNullException.ThrowIfNull(attack);
        return new Rac1GameplayDamageEvent(
            Rac1GameplayEntityRef.Moby(source),
            Rac1GameplayEntityRef.Player,
            attack.NativeDamage,
            nativeMarker: attack.NativeMarker,
            owner: Rac1GameplayDamageOwner.FromEntity(
                Rac1GameplayEntityRef.Moby(source)));
    }

    public static Rac1GameplayDamageEvent FromWrench(
        Rac1MobyRuntimeKey target,
        Rac1WrenchDamageResult damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        return new Rac1GameplayDamageEvent(
            Rac1GameplayEntityRef.Player,
            Rac1GameplayEntityRef.Moby(target),
            damage.NativeDamage,
            damage.NativeDamageFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(
                Rac1GameplayEntityRef.Player));
    }

    public static Rac1GameplayDamageEvent FromBombGlove(
        Rac1BombGloveDamageResult damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var target = new Rac1MobyRuntimeKey(
            damage.TargetNativeClassId,
            damage.TargetInstanceIndex);
        return new Rac1GameplayDamageEvent(
            Rac1GameplayEntityRef.Projectile(
                Rac1BombGlove.NativeProjectileClassId,
                damage.ProjectileId),
            Rac1GameplayEntityRef.Moby(target),
            damage.NativeDamage,
            damage.NativeDamageFlags,
            handoffKind: damage.DamageHandoff.Kind,
            owner: Rac1GameplayDamageOwner.NativeClass(
                damage.Ownership.OwnerNativeClassId));
    }
}

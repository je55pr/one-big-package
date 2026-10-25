namespace OBP.RAC3.Gameplay;

public enum Rac3GameplayEntityKind
{
    Player,
    Moby,
    Projectile,
}

/// <summary>
/// Stable engine-neutral reference carried by UYA gameplay events. Native class
/// identity remains explicit without assigning any class-specific meaning.
/// </summary>
public readonly record struct Rac3GameplayEntityRef(
    Rac3GameplayEntityKind Kind,
    int NativeClassId,
    long RuntimeId)
{
    public static Rac3GameplayEntityRef Player { get; } =
        new(Rac3GameplayEntityKind.Player, NativeClassId: 0, RuntimeId: 0);

    public static Rac3GameplayEntityRef Moby(Rac3MobyRuntimeKey key) =>
        new(Rac3GameplayEntityKind.Moby, key.NativeClassId, key.InstanceIndex);

    public static Rac3GameplayEntityRef Projectile(int nativeClassId, long runtimeId)
    {
        if (nativeClassId < 0) throw new ArgumentOutOfRangeException(nameof(nativeClassId));
        if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
        return new(Rac3GameplayEntityKind.Projectile, nativeClassId, runtimeId);
    }

    public bool MatchesMoby(Rac3MobyRuntimeKey key) =>
        Kind == Rac3GameplayEntityKind.Moby &&
        NativeClassId == key.NativeClassId &&
        RuntimeId == key.InstanceIndex;
}
/// <summary>
/// UYA damage/event transport with deliberately optional native fields. A hit can
/// be routed before its exact retail damage scalar/flags are recovered; absence
/// stays absence rather than being filled with GC or R&C1 constants.
/// </summary>
public sealed record Rac3GameplayDamageEvent
{
    public Rac3GameplayDamageEvent(
        Rac3GameplayEntityRef source,
        Rac3GameplayEntityRef target,
        double? nativeDamage = null,
        uint? nativeDamageFlags = null,
        double? nativeMarker = null,
        byte? nativeRecordKind = null)
    {
        if (nativeDamage.HasValue && !double.IsFinite(nativeDamage.Value))
            throw new ArgumentOutOfRangeException(nameof(nativeDamage));
        if (nativeMarker.HasValue && !double.IsFinite(nativeMarker.Value))
            throw new ArgumentOutOfRangeException(nameof(nativeMarker));

        Source = source;
        Target = target;
        NativeDamage = nativeDamage;
        NativeDamageFlags = nativeDamageFlags;
        NativeMarker = nativeMarker;
        NativeRecordKind = nativeRecordKind;
    }

    public Rac3GameplayEntityRef Source { get; }
    public Rac3GameplayEntityRef Target { get; }
    public double? NativeDamage { get; }
    public uint? NativeDamageFlags { get; }
    public double? NativeMarker { get; }

    /// <summary>
    /// Native UYA damage-record byte at +0x28. Its wider semantic name is not
    /// yet recovered; class consumers may use exact observed values only.
    /// </summary>
    public byte? NativeRecordKind { get; }
}

public sealed record Rac3GameplayDamageDispatch(
    long Sequence,
    Rac3GameplayDamageEvent Damage);

/// <summary>
/// Synchronous sequenced transport only. Target-specific UYA controllers own
/// admission and consequences; publishing does not mutate entity state.
/// </summary>
public sealed class Rac3DamageTransportSession
{
    private long _sequence;

    public long Sequence => _sequence;
    public event Action<Rac3GameplayDamageDispatch>? Published;

    public Rac3GameplayDamageDispatch Publish(Rac3GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var dispatch = new Rac3GameplayDamageDispatch(checked(++_sequence), damage);
        Published?.Invoke(dispatch);
        return dispatch;
    }
}

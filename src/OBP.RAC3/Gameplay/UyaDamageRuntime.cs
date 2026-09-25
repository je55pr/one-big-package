namespace OBP.RAC3.Gameplay;

public enum UyaGameplayEntityKind
{
    Player,
    Moby,
    Projectile,
}

/// <summary>
/// Stable engine-neutral reference carried by UYA gameplay events. Native class
/// identity remains explicit without assigning any class-specific meaning.
/// </summary>
public readonly record struct UyaGameplayEntityRef(
    UyaGameplayEntityKind Kind,
    int NativeClassId,
    long RuntimeId)
{
    public static UyaGameplayEntityRef Player { get; } =
        new(UyaGameplayEntityKind.Player, NativeClassId: 0, RuntimeId: 0);

    public static UyaGameplayEntityRef Moby(UyaMobyRuntimeKey key) =>
        new(UyaGameplayEntityKind.Moby, key.NativeClassId, key.InstanceIndex);

    public static UyaGameplayEntityRef Projectile(int nativeClassId, long runtimeId)
    {
        if (nativeClassId < 0) throw new ArgumentOutOfRangeException(nameof(nativeClassId));
        if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
        return new(UyaGameplayEntityKind.Projectile, nativeClassId, runtimeId);
    }

    public bool MatchesMoby(UyaMobyRuntimeKey key) =>
        Kind == UyaGameplayEntityKind.Moby &&
        NativeClassId == key.NativeClassId &&
        RuntimeId == key.InstanceIndex;
}
/// <summary>
/// UYA damage/event transport with deliberately optional native fields. A hit can
/// be routed before its exact retail damage scalar/flags are recovered; absence
/// stays absence rather than being filled with GC or R&C1 constants.
/// </summary>
public sealed record UyaGameplayDamageEvent
{
    public UyaGameplayDamageEvent(
        UyaGameplayEntityRef source,
        UyaGameplayEntityRef target,
        double? nativeDamage = null,
        uint? nativeDamageFlags = null,
        double? nativeMarker = null)
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
    }

    public UyaGameplayEntityRef Source { get; }
    public UyaGameplayEntityRef Target { get; }
    public double? NativeDamage { get; }
    public uint? NativeDamageFlags { get; }
    public double? NativeMarker { get; }
}

public sealed record UyaGameplayDamageDispatch(
    long Sequence,
    UyaGameplayDamageEvent Damage);

/// <summary>
/// Synchronous sequenced transport only. Target-specific UYA controllers own
/// admission and consequences; publishing does not mutate entity state.
/// </summary>
public sealed class UyaDamageTransportSession
{
    private long _sequence;

    public long Sequence => _sequence;
    public event Action<UyaGameplayDamageDispatch>? Published;

    public UyaGameplayDamageDispatch Publish(UyaGameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        var dispatch = new UyaGameplayDamageDispatch(checked(++_sequence), damage);
        Published?.Invoke(dispatch);
        return dispatch;
    }
}

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed Going Commando Ratchet Nanotech consequence slice.
/// The opening v1.01 player state carries 4 current / 4 maximum Nanotech.
/// Player damage queries use mask 1, round damage-record +0x2C with MIPS
/// cvt.w.s, subtract the resulting integer, and clamp at zero.
/// </summary>
public sealed class GcRatchetNanotechSession
{
    public const int OpeningAranosInitialNanotech = 4;
    public const int OpeningAranosMaximumNanotech = 4;
    public const uint DamageQueryMask = 0x00000001;
    public const int OrdinaryNativeState = 0;
    public const int ContactHitNativeState = 22;
    public const int DeathNativeState = 57;

    private int _current;
    private int _nativeState = OrdinaryNativeState;
    private bool _pendingHitResolution;

    public GcRatchetNanotechSession(
        int current = OpeningAranosInitialNanotech,
        int maximum = OpeningAranosMaximumNanotech)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        if (current > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        _current = current;
        Maximum = maximum;
    }

    public int Current => _current;
    public int Maximum { get; }
    public bool IsDepleted => _current == 0;
    public int NativeState => _nativeState;
    public bool PendingHitResolution => _pendingHitResolution;

    public GcRatchetNanotechResult Apply(GcPlayerDamageRecord damage)
    {
        if ((damage.QueryMask & DamageQueryMask) == 0 ||
            !float.IsFinite(damage.DamageHp) ||
            damage.DamageHp <= 0f ||
            IsDepleted)
        {
            return Snapshot(admitted: false, damageNanotech: 0);
        }

        int damageNanotech = RoundDamageHpLikeRetail(damage.DamageHp);
        if (damageNanotech <= 0)
        {
            return Snapshot(admitted: false, damageNanotech: 0);
        }

        _current = Math.Max(0, _current - damageNanotech);
        _nativeState = ContactHitNativeState;
        _pendingHitResolution = true;
        return Snapshot(admitted: true, damageNanotech);
    }

    /// <summary>
    /// Completes the retail-observed player state-22 reaction without inventing
    /// its duration. The shared collision/reaction runtime owns when this occurs.
    /// </summary>
    public GcRatchetNanotechResult ResolveHitReaction()
    {
        if (!_pendingHitResolution)
        {
            return Snapshot(admitted: false, damageNanotech: 0);
        }

        _nativeState = _current > 0
            ? OrdinaryNativeState
            : DeathNativeState;
        _pendingHitResolution = false;
        return Snapshot(admitted: false, damageNanotech: 0);
    }

    public static int RoundDamageHpLikeRetail(float damageHp)
    {
        if (!float.IsFinite(damageHp))
        {
            throw new ArgumentOutOfRangeException(nameof(damageHp));
        }

        return checked((int)MathF.Round(damageHp, MidpointRounding.ToEven));
    }
    private GcRatchetNanotechResult Snapshot(bool admitted, int damageNanotech) =>
        new(
            _current,
            Maximum,
            damageNanotech,
            admitted,
            IsDepleted,
            _nativeState,
            _pendingHitResolution);
}

/// <summary>
/// Minimum player-facing projection of GC's collision-damage record.
/// Retail player code queries mask 1 and consumes record +0x2C as DamageHp.
/// </summary>
public readonly record struct GcPlayerDamageRecord(
    uint QueryMask,
    float DamageHp);

public sealed record GcRatchetNanotechResult(
    int Current,
    int Maximum,
    int DamageNanotech,
    bool Admitted,
    bool Depleted,
    int NativeState,
    bool PendingHitResolution);

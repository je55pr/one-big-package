using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Player;

public enum Rac1RatchetLifeState
{
    Alive,
    Dead,
}

public enum Rac1RatchetDeathCause
{
    None,
    CombatZeroNanotech,
    RecoveredEnvironmental,
}

/// <summary>
/// Bounded retail-backed R&C1 Ratchet Nanotech state.
/// This does not generalize health semantics across the trilogy.
/// </summary>
public sealed class Rac1RatchetNanotechSession
{
    public const int RetailVeldinRespawnNanotech = 4;
    public const double RetailVeldinDeathContactSeparationExclusive = 2d;
    public const int RetailVeldinDeathNativeState = 0x77;
    public const int RetailVeldinDeathNativeSequence = 11;
    public const int RetailVeldinDeathNativeSequenceFrame = 0;

    // Raw retail player-state exclusion. Its meaning is intentionally unnamed.
    private const int UnnamedSpecialPlayerState20A4ExcludedValue = 2;

    private int _nanotech = RetailVeldinRespawnNanotech;
    private Rac1RatchetLifeState _lifeState = Rac1RatchetLifeState.Alive;
    private Rac1RatchetDeathCause _deathCause = Rac1RatchetDeathCause.None;
    private int? _nativePlayerState;
    private int? _nativeSequence;
    private int? _nativeSequenceFrame;

    public Rac1RatchetNanotechSnapshot Probe() => Snapshot();

    public Rac1RatchetNanotechSnapshot ApplyClass749Attack(Rac1Class749AttackEvent attack)
    {
        if (_lifeState != Rac1RatchetLifeState.Alive)
            throw new InvalidOperationException("R&C1 Ratchet cannot take the recovered class-749 hit after death.");

        if (attack.NativeMarker != Rac1Class749Hostile.AttackMarker ||
            attack.NativeDamage != Rac1Class749Hostile.AttackDamage)
            throw new NotSupportedException(
                "Only the recovered R&C1 class-749 marker-34 damage-1 attack is admitted.");

        _nanotech = Math.Max(0, _nanotech - 1);
        if (_nanotech == 0)
        {
            _lifeState = Rac1RatchetLifeState.Dead;
            _deathCause = Rac1RatchetDeathCause.CombatZeroNanotech;
        }
        return Snapshot();
    }

    /// <summary>
    /// Applies the recovered Veldin death-plane admission gate. The caller supplies
    /// native-equivalent vertical/contact facts; only this R&C1 session owns the
    /// resulting native player-state and death-sequence transition.
    /// </summary>
    public Rac1RatchetNanotechSnapshot? TryApplyVeldinEnvironmentalDeath(
        Rac1VeldinEnvironmentalDeathFacts facts)
    {
        if (_nativePlayerState == RetailVeldinDeathNativeState ||
            _lifeState != Rac1RatchetLifeState.Alive)
            return null;

        if (!double.IsFinite(facts.NativeVerticalPosition) ||
            !double.IsFinite(facts.DeathHeight) ||
            double.IsNaN(facts.ContactSeparation) ||
            facts.NativeVerticalPosition >= facts.DeathHeight ||
            facts.ContactSeparation <= RetailVeldinDeathContactSeparationExclusive ||
            facts.NativeSpecialPlayerState20A4 == UnnamedSpecialPlayerState20A4ExcludedValue)
            return null;

        _nativePlayerState = RetailVeldinDeathNativeState;
        _nativeSequence = RetailVeldinDeathNativeSequence;
        _nativeSequenceFrame = RetailVeldinDeathNativeSequenceFrame;
        return ApplyEnvironmentalDeathReset();
    }

    /// <summary>
    /// Applies the recovered environmental-death reset boundary after a source-specific
    /// path has independently established that this is one of the retained witnesses.
    /// Veldin supplies an automatic gate above; level 2 is currently smoke/test-injected
    /// from its retained checkpoint/death witness because its gameplay trigger is unknown.
    /// </summary>
    public Rac1RatchetNanotechSnapshot ApplyEnvironmentalDeathReset()
    {
        if (_lifeState != Rac1RatchetLifeState.Alive)
            throw new InvalidOperationException("R&C1 Ratchet is already dead.");

        _nanotech = 0;
        _lifeState = Rac1RatchetLifeState.Dead;
        _deathCause = Rac1RatchetDeathCause.RecoveredEnvironmental;
        return Snapshot();
    }

    /// <summary>
    /// Applies only a recovered environmental restart. Combat zero-Nanotech is a
    /// proven death boundary, but its native restart/checkpoint path is not recovered
    /// and therefore cannot use this reset.
    /// </summary>
    public Rac1RatchetNanotechSnapshot Respawn()
    {
        if (_lifeState != Rac1RatchetLifeState.Dead ||
            _deathCause != Rac1RatchetDeathCause.RecoveredEnvironmental)
            throw new InvalidOperationException(
                "R&C1 recovered respawn requires a recovered environmental death.");

        _nanotech = RetailVeldinRespawnNanotech;
        _lifeState = Rac1RatchetLifeState.Alive;
        _deathCause = Rac1RatchetDeathCause.None;
        _nativePlayerState = null;
        _nativeSequence = null;
        _nativeSequenceFrame = null;
        return Snapshot();
    }

    private Rac1RatchetNanotechSnapshot Snapshot() =>
        new(
            _nanotech,
            RetailVeldinRespawnNanotech,
            _lifeState,
            _deathCause,
            _nativePlayerState,
            _nativeSequence,
            _nativeSequenceFrame);
}

public sealed record Rac1VeldinEnvironmentalDeathFacts(
    double NativeVerticalPosition,
    double DeathHeight,
    double ContactSeparation,
    int NativeSpecialPlayerState20A4);

public sealed record Rac1RatchetNanotechSnapshot(
    int Nanotech,
    int RespawnNanotech,
    Rac1RatchetLifeState LifeState,
    Rac1RatchetDeathCause DeathCause,
    int? NativePlayerState = null,
    int? NativeSequence = null,
    int? NativeSequenceFrame = null)
{
    public bool IsDead => LifeState == Rac1RatchetLifeState.Dead;

    public bool HasRecoveredEnvironmentalRespawn =>
        IsDead && DeathCause == Rac1RatchetDeathCause.RecoveredEnvironmental;
}

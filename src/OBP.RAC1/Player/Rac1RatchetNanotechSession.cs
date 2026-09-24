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
    public const int RetailVeldinDeathNativeState = Rac1PlayerActionDomain.EnvironmentalFallDeath;
    public const int RetailVeldinDeathNativeSequence =
        Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId;
    public const int RetailVeldinDeathNativeSequenceFrame = 0;

    // Raw retail player-state exclusion. Its meaning is intentionally unnamed.
    private const int UnnamedSpecialPlayerState20A4ExcludedValue = 2;

    private readonly Rac1PlayerActionRuntimeSession _actions = new();
    private int _nanotech = RetailVeldinRespawnNanotech;
    private Rac1RatchetLifeState _lifeState = Rac1RatchetLifeState.Alive;
    private Rac1RatchetDeathCause _deathCause = Rac1RatchetDeathCause.None;

    public long EnvironmentalDeathGeneration { get; private set; }
    public long EnvironmentalRestartGeneration { get; private set; }
    public bool HasPendingRecoveredEnvironmentalRestart =>
        _lifeState == Rac1RatchetLifeState.Dead &&
        _deathCause == Rac1RatchetDeathCause.RecoveredEnvironmental &&
        EnvironmentalRestartGeneration < EnvironmentalDeathGeneration;

    public Rac1RatchetNanotechSnapshot Probe() => Snapshot();
    public Rac1PlayerActionRuntimeSession Actions => _actions;

    public Rac1RatchetNanotechSnapshot ApplyDamage(Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (damage.Target != Rac1GameplayEntityRef.Player ||
            damage.Source.Kind != Rac1GameplayEntityKind.Moby ||
            damage.Source.NativeClassId != Rac1Class749Hostile.NativeClassId ||
            damage.NativeMarker != Rac1Class749Hostile.AttackMarker ||
            damage.NativeDamage != Rac1Class749Hostile.AttackDamage ||
            damage.NativeDamageFlags is not null)
        {
            throw new NotSupportedException(
                "Only the recovered R&C1 class-749 marker-34 damage-1 player event is admitted.");
        }

        return ApplyRecoveredClass749Damage();
    }

    public Rac1RatchetNanotechSnapshot ApplyClass749Attack(Rac1Class749AttackEvent attack)
    {
        ArgumentNullException.ThrowIfNull(attack);
        if (attack.NativeMarker != Rac1Class749Hostile.AttackMarker ||
            attack.NativeDamage != Rac1Class749Hostile.AttackDamage)
            throw new NotSupportedException(
                "Only the recovered R&C1 class-749 marker-34 damage-1 attack is admitted.");

        return ApplyRecoveredClass749Damage();
    }

    private Rac1RatchetNanotechSnapshot ApplyRecoveredClass749Damage()
    {
        if (_lifeState != Rac1RatchetLifeState.Alive)
            throw new InvalidOperationException("R&C1 Ratchet cannot take the recovered class-749 hit after death.");

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
        if (_actions.Probe().CurrentNativeState == RetailVeldinDeathNativeState ||
            _lifeState != Rac1RatchetLifeState.Alive)
            return null;

        if (!double.IsFinite(facts.NativeVerticalPosition) ||
            !double.IsFinite(facts.DeathHeight) ||
            double.IsNaN(facts.ContactSeparation) ||
            facts.NativeVerticalPosition >= facts.DeathHeight ||
            facts.ContactSeparation <= RetailVeldinDeathContactSeparationExclusive ||
            facts.NativeSpecialPlayerState20A4 == UnnamedSpecialPlayerState20A4ExcludedValue)
            return null;

        _actions.EnterState(RetailVeldinDeathNativeState);
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

        _actions.EnterState(RetailVeldinDeathNativeState);
        _nanotech = 0;
        _lifeState = Rac1RatchetLifeState.Dead;
        _deathCause = Rac1RatchetDeathCause.RecoveredEnvironmental;
        EnvironmentalDeathGeneration = checked(EnvironmentalDeathGeneration + 1);
        return Snapshot();
    }

    /// <summary>
    /// Applies only a recovered environmental restart. Combat zero-Nanotech is a
    /// proven death boundary, but its native restart/checkpoint path is not recovered
    /// and therefore cannot use this reset.
    /// </summary>
    public Rac1RatchetNanotechSnapshot Respawn()
    {
        if (!HasPendingRecoveredEnvironmentalRestart)
            throw new InvalidOperationException(
                "R&C1 recovered respawn requires a pending recovered environmental death.");

        _nanotech = RetailVeldinRespawnNanotech;
        _lifeState = Rac1RatchetLifeState.Alive;
        _deathCause = Rac1RatchetDeathCause.None;
        EnvironmentalRestartGeneration = EnvironmentalDeathGeneration;
        _actions.EnterState(Rac1PlayerActionDomain.Neutral);
        return Snapshot();
    }

    private Rac1RatchetNanotechSnapshot Snapshot() =>
        new(
            _nanotech,
            RetailVeldinRespawnNanotech,
            _lifeState,
            _deathCause,
            _actions.Probe());
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
    Rac1PlayerActionSnapshot Action)
{
    public bool IsDead => LifeState == Rac1RatchetLifeState.Dead;
    public int NativePlayerState => Action.CurrentNativeState;
    public int? PreviousNativePlayerState => Action.PreviousNativeState;
    public int? NativeSequence => Action.NativeSequence;
    public int? NativeSequenceFrame => Action.NativeSequenceFrame;
    public bool AllowsOrdinaryCharacterMovement =>
        !IsDead && Action.AllowsOrdinaryCharacterMovement;

    public bool HasRecoveredEnvironmentalRespawn =>
        IsDead && DeathCause == Rac1RatchetDeathCause.RecoveredEnvironmental;
}

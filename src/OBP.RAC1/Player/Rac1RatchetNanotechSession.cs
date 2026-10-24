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
    // Ordinary state0x16 entry 0x22666c..0x226678 writes P+0x1c0=77;
    // common intake 0x210d34..0x210d44 rejects damage while that timer is nonzero.
    public const int EnemyHitProtectionTicks = 77;
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
    private int _enemyHitProtection;

    public long EnvironmentalDeathGeneration { get; private set; }
    public long EnvironmentalRestartGeneration { get; private set; }
    public bool HasPendingRecoveredEnvironmentalRestart =>
        _lifeState == Rac1RatchetLifeState.Dead &&
        _deathCause == Rac1RatchetDeathCause.RecoveredEnvironmental &&
        EnvironmentalRestartGeneration < EnvironmentalDeathGeneration;

    public Rac1RatchetNanotechSnapshot Probe() => Snapshot();

    public void UpdateNative()
    {
        if (_enemyHitProtection > 0) _enemyHitProtection--;
    }

    /// <summary>Class806's first arriving cloud particle credits one point, clamped to the ordinary capacity.</summary>
    public bool CollectCrateNanotech()
    {
        if (_lifeState != Rac1RatchetLifeState.Alive || _nanotech >= RetailVeldinRespawnNanotech) return false;
        _nanotech = Math.Min(RetailVeldinRespawnNanotech, _nanotech + Rac1CrateSupply.NanotechPerCloud);
        return true;
    }
    public Rac1PlayerActionRuntimeSession Actions => _actions;

    public Rac1RatchetNanotechSnapshot ApplyDamage(Rac1GameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        bool class749 = damage.Source.NativeClassId == Rac1Class749Hostile.NativeClassId &&
            damage.NativeMarker == Rac1Class749Hostile.AttackMarker && damage.NativeDamageFlags is null;
        bool class1440 = damage.Source.NativeClassId == Rac1Class1440Session.ClassId &&
            damage.NativeMarker is null && damage.NativeDamageFlags == 0x00010001;
        // Native Novalis peckbot_update_l1 passes damage amount 1 at animation time 13.
        // The common damage record's native flags are not yet recovered.
        bool novalisPeck = damage.Source.NativeClassId is Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId &&
            damage.NativeMarker is null && damage.NativeDamageFlags is null;
        // Frog state12 provides native damage-1; its contact flags remain unknown.
        bool frogContact = Rac1FrogContact.IsAdmitted(damage);
        bool seekerBlast = Rac1SeekerExplosion.IsNativeExplosion(damage);
        bool amoeboidAttack = Rac1AmoeboidAttack.IsNativePlayerAttack(damage);
        bool explosiveCrateBlast = Rac1ExplosiveCrateBlast.IsNativePlayerBlast(damage);
        if (damage.Target != Rac1GameplayEntityRef.Player ||
            damage.Source.Kind != Rac1GameplayEntityKind.Moby ||
            !(class749 || class1440 || novalisPeck || frogContact || seekerBlast || amoeboidAttack || explosiveCrateBlast) ||
            damage.NativeDamage != (explosiveCrateBlast ? Rac1ExplosiveCrateBlast.Damage : 1))
        {
            throw new NotSupportedException(
                "Only recovered enemy damage-1 or native class-505 damage-2 player descriptors are admitted.");
        }

        return ApplyRecoveredEnemyDamage(explosiveCrateBlast ? Rac1ExplosiveCrateBlast.Damage : 1);
    }

    /// <summary>Provisional Aridia contact consequence. No retail attack descriptor is claimed.</summary>
    public Rac1RatchetNanotechSnapshot ApplyHostAridiaContactDamage() => ApplyRecoveredEnemyDamage();

    private Rac1RatchetNanotechSnapshot ApplyRecoveredEnemyDamage(int nanotechDamage = 1)
    {
        if (_lifeState != Rac1RatchetLifeState.Alive)
            throw new InvalidOperationException("R&C1 Ratchet cannot take an enemy hit after death.");
        if (_enemyHitProtection > 0) return Snapshot();

        _nanotech = Math.Max(0, _nanotech - nanotechDamage);
        _enemyHitProtection = EnemyHitProtectionTicks;
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
        _enemyHitProtection = 0;
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
            _actions.Probe(),
            _enemyHitProtection);
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
    Rac1PlayerActionSnapshot Action,
    int EnemyHitProtectionTicks = 0)
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

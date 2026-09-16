using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Player;

public enum Rac1RatchetLifeState
{
    Alive,
    Dead,
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
            _lifeState = Rac1RatchetLifeState.Dead;
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
    /// Applies the witnessed Veldin environmental-death reset boundary. Retail leaves
    /// Nanotech unchanged through death sequences 10/11, then zeroes it at reset.
    /// </summary>
    public Rac1RatchetNanotechSnapshot ApplyEnvironmentalDeathReset()
    {
        if (_lifeState != Rac1RatchetLifeState.Alive)
            throw new InvalidOperationException("R&C1 Ratchet is already dead.");

        _nanotech = 0;
        _lifeState = Rac1RatchetLifeState.Dead;
        return Snapshot();
    }

    public Rac1RatchetNanotechSnapshot Respawn()
    {
        if (_lifeState != Rac1RatchetLifeState.Dead)
            throw new InvalidOperationException("R&C1 Ratchet respawn requires a dead state.");

        _nanotech = RetailVeldinRespawnNanotech;
        _lifeState = Rac1RatchetLifeState.Alive;
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
    int? NativePlayerState = null,
    int? NativeSequence = null,
    int? NativeSequenceFrame = null)
{
    public bool IsDead => LifeState == Rac1RatchetLifeState.Dead;
}

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

    private int _nanotech = RetailVeldinRespawnNanotech;
    private Rac1RatchetLifeState _lifeState = Rac1RatchetLifeState.Alive;

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
        return Snapshot();
    }

    private Rac1RatchetNanotechSnapshot Snapshot() =>
        new(_nanotech, RetailVeldinRespawnNanotech, _lifeState);
}

public sealed record Rac1RatchetNanotechSnapshot(
    int Nanotech,
    int RespawnNanotech,
    Rac1RatchetLifeState LifeState)
{
    public bool IsDead => LifeState == Rac1RatchetLifeState.Dead;
}

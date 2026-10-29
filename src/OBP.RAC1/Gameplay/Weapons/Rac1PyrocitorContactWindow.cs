using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded host contact repeat policy for R&C1 class-176 flame volumes.
/// Not a recovered native victim-filter routine. Targets must retain their
/// source identity: class/instance numbers are only unique within a source.
/// Failed native damage admissions do not consume the repeat window.
/// </summary>
public sealed class Rac1PyrocitorContactWindow
{
    private readonly Dictionary<WeaponTargetKey, ulong> _nextEligible = [];

    public bool CanHit(WeaponTargetKey target, ulong tick) =>
        !_nextEligible.TryGetValue(target, out ulong next) || tick >= next;

    public void ConfirmHit(WeaponTargetKey target, ulong tick)
    {
        _nextEligible[target] = tick + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks;
    }

    public void Prune(ulong tick)
    {
        foreach (var key in _nextEligible.Where(entry => entry.Value <= tick).Select(entry => entry.Key).ToArray())
            _nextEligible.Remove(key);
    }

    public void Clear() => _nextEligible.Clear();
}

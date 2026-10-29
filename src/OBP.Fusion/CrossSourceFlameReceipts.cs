using OBP.Runtime.Gameplay;

namespace OBP.Fusion;

/// <summary>
/// OBP-owned receipt identity and repeat window for a source weapon's genuine
/// emitted particles. No projectile simulation or target-native damage policy.
/// A new particle never reuses an old shot key, even after source weapon reset.
/// </summary>
public sealed class CrossSourceFlameReceipts
{
    private sealed record Receipt(long Sequence)
    {
        internal HashSet<WeaponTargetKey> Victims { get; } = [];
    }

    private readonly SourceWeaponKey _weapon;
    private readonly long _repeatTicks;
    private readonly Dictionary<object, Receipt> _particles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WeaponTargetKey, long> _nextAllowedTick = [];
    private long _nextSequence;

    public CrossSourceFlameReceipts(SourceWeaponKey weapon, int repeatTicks)
    {
        _weapon = weapon.Validate();
        _repeatTicks = repeatTicks > 0 ? repeatTicks : throw new ArgumentOutOfRangeException(nameof(repeatTicks));
    }

    public int TrackedParticles => _particles.Count;

    /// <summary>Returns one stable shot key per particle, but only if a live target can receive another hit.</summary>
    public bool CanHit(object particle, WeaponTargetKey target, long tick, out WeaponShotKey shot)
    {
        ArgumentNullException.ThrowIfNull(particle);
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (!_particles.TryGetValue(particle, out var receipt))
            _particles.Add(particle, receipt = new(++_nextSequence));

        shot = new(_weapon, 0, receipt.Sequence);
        return !receipt.Victims.Contains(target) && _nextAllowedTick.GetValueOrDefault(target) <= tick;
    }

    /// <summary>Record a source-native victim adapter's accepted hit, not an attempted contact.</summary>
    public void Accept(object particle, WeaponTargetKey target, long tick)
    {
        if (!CanHit(particle, target, tick, out _))
            throw new InvalidOperationException("An actor cannot accept the same flame hit twice or bypass its repeat window.");
        _particles[particle].Victims.Add(target);
        _nextAllowedTick[target] = checked(tick + _repeatTicks);
    }

    public void Forget(object particle) => _particles.Remove(particle);

    /// <summary>Reset the source particle session, retaining the monotonic shot identity epoch.</summary>
    public void Reset()
    {
        _particles.Clear();
        _nextAllowedTick.Clear();
    }
}

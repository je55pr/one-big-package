using OBP.RAC1.Player;

namespace OBP.Game;

/// <summary>Preserves the existing host's native-step accumulation and post-step projectile phase.</summary>
internal sealed class Rac1EnemyTickClock
{
    // A frame that overruns must not owe more ticks than it can run: uncapped, each overrun made the next
    // frame run extra ticks and overrun further. Time beyond the cap is dropped (the game runs slow instead).
    internal const int MaximumTicksPerAdvance = 4;
    private double _ticks;
    // LEVEL2:0x15f5cc frame-low-bits emission gate. This remains the host's
    // 60-Hz counter; the exact retail global update phase is not yet proven.
    internal uint Frame { get; private set; }

    internal void Advance(double delta, Func<bool> step, Action<double> projectiles)
    {
        _ticks += Math.Max(0, delta) * Rac1RatchetMovementController.UpdateHz;
        if (_ticks > MaximumTicksPerAdvance) _ticks = MaximumTicksPerAdvance;
        while (_ticks >= 1)
        {
            _ticks--;
            Frame++;
            if (!step()) return;
        }
        projectiles(delta);
    }

    internal void Reset() { _ticks = 0; Frame = 0; }
}

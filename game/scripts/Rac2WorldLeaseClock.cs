using OBP.RAC2.Gameplay;

namespace OBP.Game;

/// <summary>
/// Owns the R&C2 world lease's source-invalidation latch and fractional native
/// clock. Ordinary mixed-source actors keep their separate actor-host clock.
/// </summary>
internal sealed class Rac2WorldLeaseClock(Func<bool> worldCurrent, Func<bool> sourceValid, bool sourceRequired)
{
    private const double NativeTickSeconds = 1d / Rac2Class2827ApproachSession.NativeTicksPerSecond;
    private double _accumulator;
    private bool _sourceInvalidated;
    private bool _retired;

    internal bool Current => !_retired && !_sourceInvalidated && worldCurrent() && SourceReadyForActivation;

    /// <summary>
    /// Validate the R&C2 source while the new world is being activated, before
    /// WorldSceneLifetime can publish it as the current lease. A failed source
    /// admission latches invalid for the lifetime of this lease.
    /// </summary>
    internal bool SourceReadyForActivation
    {
        get
        {
            if (_retired || _sourceInvalidated) return false;
            if (sourceRequired && !sourceValid())
            {
                _sourceInvalidated = true;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Tick the lift and door on the source clock. Only non-ordinary gameplay
    /// ticks native hostiles here; ordinary hostiles tick in ActorWorldHost.
    /// </summary>
    internal void Advance(double delta, Func<bool> ordinary, Action lift, Action door, Action hostiles)
    {
        if (!Current) return;
        _accumulator += Math.Clamp(delta, 0d, 0.25d);
        int ticks = Math.Min(12, (int)Math.Floor(_accumulator / NativeTickSeconds));
        _accumulator -= ticks * NativeTickSeconds;
        for (int tick = 0; tick < ticks && Current; tick++)
        {
            lift();
            door();
            if (!ordinary()) hostiles();
        }
    }

    internal void Retire()
    {
        _retired = true;
        _accumulator = 0d;
    }
}

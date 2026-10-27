using OBP.Runtime.Diagnostics;

namespace OBP.Game.Diagnostics;

/// <summary>Small deterministic recording driver; replay consumes the ordinary recorded commands without this script.</summary>
internal static class EffectReplayScript
{
    public static void Apply(int processFrame, Action<PlaytestCommand> command, Action stop)
    {
        if (processFrame is 1 or 40) command(new("effects", "start"));
        if (processFrame == 30) command(new("effects", "reset"));
        // Two native windows of at least 20 ticks, matching the rendered samples.
        // Longer Lab actor wall-contact replay is tracked in #404; the separate
        // rendered scenario still verifies full effect drain and world re-entry.
        if (processFrame == 65) stop();
    }
}

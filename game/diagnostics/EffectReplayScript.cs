using OBP.Runtime.Diagnostics;

namespace OBP.Game.Diagnostics;

/// <summary>Small deterministic recording driver; replay consumes the ordinary recorded commands without this script.</summary>
internal static class EffectReplayScript
{
    // Normal effects smoke stays bounded; the longer contact replay is opt-in.
    private static readonly int StopFrame = ReadStopFrame();

    private static int ReadStopFrame()
    {
        string? value = Environment.GetEnvironmentVariable("OBP_EFFECT_REPLAY_STOP_FRAME");
        if (string.IsNullOrEmpty(value)) return 65;
        if (int.TryParse(value, out int frame) && frame is >= 65 and <= 600) return frame;
        throw new InvalidOperationException("OBP_EFFECT_REPLAY_STOP_FRAME must be between 65 and 600.");
    }

    public static void Apply(int processFrame, Action<PlaytestCommand> command, Action stop)
    {
        if (processFrame is 1 or 40) command(new("effects", "start"));
        if (processFrame == 30) command(new("effects", "reset"));
        // Two native windows of at least 20 ticks, matching the rendered samples.
        // The longer optional stop boundary also exercises grounded wall contact;
        // the separate rendered scenario verifies full drain and world re-entry.
        if (processFrame == StopFrame) stop();
    }
}

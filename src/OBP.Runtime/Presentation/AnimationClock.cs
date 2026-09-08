namespace OBP.Runtime.Presentation;

/// <summary>How a clip repeats once it reaches its last frame.</summary>
public enum LoopMode
{
    /// <summary>Jump back to frame 0 and repeat.</summary>
    Loop,

    /// <summary>Play forward then backward, forever.</summary>
    PingPong,

    /// <summary>Play once and hold the last frame.</summary>
    HoldLast,
}

/// <summary>
/// Pure frame selection for a baked animation clip. Deterministic in the elapsed
/// time, so a capture at a fixed settle time reproduces the same pose regardless
/// of frame rate — the Godot <c>AnimatedMesh</c> is a thin applier over this.
/// </summary>
public static class AnimationClock
{
    /// <summary>
    /// The frame index to show for a <paramref name="frameCount"/>-frame clip at
    /// <paramref name="framesPerSecond"/>, <paramref name="timeSeconds"/> after
    /// the clip started.
    /// </summary>
    public static int FrameAt(int frameCount, double framesPerSecond, double timeSeconds, LoopMode mode)
    {
        if (frameCount <= 1 || framesPerSecond <= 0)
        {
            return 0;
        }

        double rawFrame = timeSeconds * framesPerSecond;
        int i = (int)System.Math.Floor(rawFrame);

        return mode switch
        {
            LoopMode.HoldLast => System.Math.Clamp(i, 0, frameCount - 1),
            LoopMode.PingPong => PingPong(i, frameCount),
            _ => Wrap(i, frameCount),
        };
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static int PingPong(int i, int n)
    {
        int period = 2 * (n - 1);
        int m = Wrap(i, period);
        return m < n ? m : period - m;
    }
}

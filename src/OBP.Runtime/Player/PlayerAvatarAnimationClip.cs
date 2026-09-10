namespace OBP.Runtime.Player;

/// <summary>Neutral semantic role for one source-backed avatar animation clip.</summary>
public enum PlayerAvatarAnimationRole
{
    Standing,
    LocomotionStart,
    SustainedLocomotion,
    LocomotionStopVariant,
    StationaryJump,
    MovingJump,
    Crouch,
    CrouchTurnRight,
    CrouchTurnLeft,
    PrimaryAttack,
}

/// <summary>
/// One model-local baked animation clip. Timing is stored per frame so source
/// formats with variable native frame rates do not have to be flattened to FPS.
/// </summary>
public sealed record PlayerAvatarAnimationClip(
    string Id,
    PlayerAvatarAnimationRole Role,
    IReadOnlyList<double[]> LocalFrames,
    IReadOnlyList<double> FrameDurationsSeconds)
{
    public int FrameCount => LocalFrames.Count;

    public double DurationSeconds => FrameDurationsSeconds.Sum();

    public bool HasVariableTiming
    {
        get
        {
            if (FrameDurationsSeconds.Count <= 1)
                return false;
            double first = FrameDurationsSeconds[0];
            return FrameDurationsSeconds.Skip(1).Any(duration => duration != first);
        }
    }

    /// <summary>Constant clip rate when every frame duration is identical; otherwise null.</summary>
    public float? ConstantFramesPerSecond
    {
        get
        {
            if (FrameDurationsSeconds.Count == 0 || HasVariableTiming)
                return null;
            double duration = FrameDurationsSeconds[0];
            return duration > 0 && double.IsFinite(duration)
                ? (float)(1d / duration)
                : null;
        }
    }

    /// <summary>
    /// Select a frame at elapsed clip time. Looping is presentation policy rather
    /// than native clip data, so callers choose whether to wrap or hold the end.
    /// </summary>
    public int FrameIndexAt(double elapsedSeconds, bool loop)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (FrameCount <= 1)
            return 0;

        double duration = DurationSeconds;
        if (!(duration > 0) || !double.IsFinite(duration))
            return 0;

        double time = Math.Max(0, elapsedSeconds);
        if (!loop && time >= duration)
            return FrameCount - 1;
        if (loop)
        {
            time %= duration;
            double loopTolerance = Math.Max(1d, Math.Abs(duration)) * 1e-12;
            if (duration - time <= loopTolerance)
                time = 0;
        }

        double boundary = 0;
        for (int frame = 0; frame < FrameDurationsSeconds.Count; frame++)
        {
            boundary += FrameDurationsSeconds[frame];
            double tolerance = Math.Max(1d, Math.Abs(boundary)) * 1e-12;
            if (time < boundary - tolerance || frame == FrameDurationsSeconds.Count - 1)
                return frame;
        }

        return FrameCount - 1;
    }
}

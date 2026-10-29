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
    JumpVariant,
    Glide,
    Traversal,
    AttackVariant,
}

/// <summary>
/// One model-local baked animation clip. Timing is stored per frame so source
/// formats with variable native frame rates do not have to be flattened to FPS.
/// Optional frame timeline positions retain a source's authored coordinates;
/// they are not elapsed seconds and are interpreted only by its controller.
/// </summary>
public sealed record PlayerAvatarAnimationClip(
    string Id,
    PlayerAvatarAnimationRole Role,
    IReadOnlyList<double[]> LocalFrames,
    IReadOnlyList<double> FrameDurationsSeconds,
    IReadOnlyList<double>? FrameTimelinePositions = null,
    IReadOnlyDictionary<string, IReadOnlyList<PlayerAvatarAttachmentPose>>? AttachmentFrames = null)
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
    /// Return the frame pair and finite interpolation for a source-timed clip.
    /// FrameIndexAt deliberately tolerates time very near a frame boundary; the
    /// fraction must use the same boundary and wrap decisions to avoid tiny
    /// negative fractions reaching the skeletal sampler.
    /// </summary>
    public PlayerSkeletalSample SampleAt(double elapsedSeconds, bool loop)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (FrameCount == 0 || FrameDurationsSeconds.Count != FrameCount ||
            FrameDurationsSeconds.Any(duration => !double.IsFinite(duration) || duration <= 0) ||
            !double.IsFinite(DurationSeconds))
            throw new InvalidDataException($"Animation clip '{Id}' has invalid frame timing.");

        double total = DurationSeconds;
        double phase = elapsedSeconds;
        if (loop)
        {
            phase %= total;
            if (total - phase <= Math.Max(1d, Math.Abs(total)) * 1e-12) phase = 0;
        }
        else phase = Math.Min(phase, Math.BitDecrement(total));

        int frame = FrameIndexAt(phase, loop);
        double start = FrameDurationsSeconds.Take(frame).Sum();
        double frameDuration = FrameDurationsSeconds[frame];
        double fraction = (phase - start) / frameDuration;
        double roundoff = Math.Max(1d, Math.Abs(start)) * 1e-12 / frameDuration;
        if (!double.IsFinite(fraction) || fraction < -roundoff || fraction > 1 + roundoff)
            throw new InvalidDataException($"Animation clip '{Id}' has inconsistent frame interpolation at {phase:R}s (frame {frame}).");
        return new(frame, loop ? (frame + 1) % FrameCount : Math.Min(frame + 1, FrameCount - 1),
            (float)Math.Clamp(fraction, 0, 1));
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

/// <summary>Model-local attachment origin and basis columns before host axis conversion.</summary>
public sealed record PlayerAvatarAttachmentPose(
    PlayerAvatarPoint Origin, PlayerAvatarPoint X, PlayerAvatarPoint Y, PlayerAvatarPoint Z)
{
    public bool IsFinite => Finite(Origin) && Finite(X) && Finite(Y) && Finite(Z);

    public static PlayerAvatarAttachmentPose Interpolate(
        PlayerAvatarAttachmentPose from, PlayerAvatarAttachmentPose to, double amount) =>
        new(Lerp(from.Origin, to.Origin, amount), Lerp(from.X, to.X, amount),
            Lerp(from.Y, to.Y, amount), Lerp(from.Z, to.Z, amount));

    public PlayerAvatarPoint Transform(PlayerAvatarPoint point) => new(
        Origin.X + X.X * point.X + Y.X * point.Y + Z.X * point.Z,
        Origin.Y + X.Y * point.X + Y.Y * point.Y + Z.Y * point.Z,
        Origin.Z + X.Z * point.X + Y.Z * point.Y + Z.Z * point.Z);

    private static PlayerAvatarPoint Lerp(PlayerAvatarPoint a, PlayerAvatarPoint b, double amount) =>
        new(a.X + (b.X - a.X) * amount, a.Y + (b.Y - a.Y) * amount, a.Z + (b.Z - a.Z) * amount);

    private static bool Finite(PlayerAvatarPoint value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}

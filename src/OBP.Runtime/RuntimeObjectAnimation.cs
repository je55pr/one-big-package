namespace OBP.Runtime;

/// <summary>
/// Neutral presentation role for a source-backed dynamic-object animation.
/// Rest uses the object's ordinary <see cref="RuntimeDynamicObject.Meshes"/>;
/// additional roles are admitted only when source-game evidence selects them.
/// </summary>
public enum RuntimeObjectAnimationRole
{
    Rest,
    Reaction,
}

/// <summary>
/// Frame positions for one <see cref="RuntimeDynamicObject.Meshes"/> surface.
/// Each frame is model-local OBP Y-up and matches that surface's vertex order.
/// </summary>
public sealed record RuntimeObjectAnimationSurface(
    int SurfaceIndex,
    IReadOnlyList<double[]> LocalFrames)
{
    public int FrameCount => LocalFrames.Count;
}

/// <summary>
/// One model-local baked animation clip for a dynamic object. Source-game
/// sequence numbers deliberately do not cross this boundary.
/// </summary>
public sealed record RuntimeObjectAnimationClip(
    string Id,
    RuntimeObjectAnimationRole Role,
    IReadOnlyList<RuntimeObjectAnimationSurface> Surfaces,
    IReadOnlyList<double> FrameDurationsSeconds)
{
    public int FrameCount => Surfaces.Count > 0 ? Surfaces[0].FrameCount : 0;

    public double DurationSeconds => FrameDurationsSeconds.Sum();

    public float? ConstantFramesPerSecond
    {
        get
        {
            if (FrameDurationsSeconds.Count == 0)
                return null;
            double first = FrameDurationsSeconds[0];
            if (!(first > 0) || !double.IsFinite(first) ||
                FrameDurationsSeconds.Skip(1).Any(value => value != first))
                return null;
            return (float)(1d / first);
        }
    }

    /// <summary>
    /// Select the clamped one-shot frame for elapsed clip time. Dynamic object
    /// gameplay owns repetition/role changes; a completed clip holds its final
    /// authored pose until another role is selected.
    /// </summary>
    public int FrameIndexAt(double elapsedSeconds)
    {
        if (FrameCount <= 0 || FrameDurationsSeconds.Count != FrameCount)
            return -1;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0d)
            return 0;

        double cursor = 0d;
        for (int frame = 0; frame < FrameCount; frame++)
        {
            double duration = FrameDurationsSeconds[frame];
            if (!(duration > 0d) || !double.IsFinite(duration))
                throw new InvalidDataException($"Animation clip {Id} has invalid frame duration at {frame}.");
            cursor += duration;
            if (elapsedSeconds < cursor)
                return frame;
        }
        return FrameCount - 1;
    }
}

/// <summary>
/// Source-backed animation capability attached to one dynamic object model.
/// Mutable state/activation belongs to the later gameplay entity boundary.
/// </summary>
public sealed record RuntimeObjectAnimationSet(
    IReadOnlyList<RuntimeObjectAnimationClip> Clips,
    RuntimeObjectAnimationRole InitialRole = RuntimeObjectAnimationRole.Rest)
{
    public RuntimeObjectAnimationClip? Find(RuntimeObjectAnimationRole role) =>
        Clips.SingleOrDefault(clip => clip.Role == role);
}

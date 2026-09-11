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

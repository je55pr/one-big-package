namespace OBP.Runtime.Player;

/// <summary>
/// Freezes the last evaluated pose on each transition, including interruptions.
/// The source controller supplies the transition identity and progress curve.
/// </summary>
public sealed class PlayerSkeletalTransition
{
    private PlayerSkeletalPose? _previous, _source;
    private object? _key;

    public void Reset() { _previous = _source = null; _key = null; }

    public void Resolve(PlayerSkeletalSampler sampler, object? transitionKey, float progress,
        PlayerSkeletalPose? initialSource = null)
    {
        if (!float.IsFinite(progress) || progress is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(progress));
        if (transitionKey is null) { _key = null; _source = null; }
        else if (!ReferenceEquals(_key, transitionKey))
        {
            _key = transitionKey;
            _source = _previous ?? initialSource;
        }
        if (_source is not null) sampler.BlendFrom(_source, progress);
        _previous = sampler.CapturePose();
    }
}

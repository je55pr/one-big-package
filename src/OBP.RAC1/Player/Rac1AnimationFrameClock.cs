namespace OBP.RAC1.Player;

/// <summary>
/// Recovered player frame advance (Veldin 0x232670) for full-clip loops,
/// positive-duration blends and the admitted standing/crouch-exit curves.
/// Does not implement custom loop ranges, sounds or joint-pose evaluation.
/// </summary>
public sealed class Rac1AnimationFrameClock
{
    public readonly record struct Sample(
        int Sequence, int CurrentFrame, int NextFrame, float Fraction, bool IsBlending);

    private readonly IReadOnlyDictionary<int, float[]> _rates;
    private float _transitionRate;
    private IReadOnlyList<float>? _blendCurve;
    private int _blendCurveIndex;
    private float _multiplier = 1;
    private int? _loopStart;
    private int? _loopEnd;
    private int? _exitLoopFrame;

    public Rac1AnimationFrameClock(IReadOnlyDictionary<int, IReadOnlyList<double>> durations,
        int sequence, int nextFrame, int blendTicks)
    {
        _rates = durations.ToDictionary(pair => pair.Key, pair => Rates(pair.Value));
        Select(sequence, nextFrame, blendTicks);
    }

    public Sample Current { get; private set; }
    public bool WrappedThisUpdate { get; private set; }
    public float PlaybackMultiplier
    {
        get => _multiplier;
        set
        {
            if (!float.IsFinite(value) || value < 0 || value > 4)
                throw new ArgumentOutOfRangeException(nameof(value));
            _multiplier = value;
        }
    }

    public int FrameCount(int sequence) => _rates[sequence].Length;

    /// <summary>
    /// Adapter ownership transfer without a native sequence selection. Preserve
    /// the complete clock, including an interrupted preset blend, while making
    /// additional target sequences available to the following action owner.
    /// </summary>
    public Rac1AnimationFrameClock RebindSequences(IReadOnlyDictionary<int, IReadOnlyList<double>> durations)
    {
        var rebound = new Rac1AnimationFrameClock(durations, Current.Sequence, Current.NextFrame, 1);
        if (!_rates[Current.Sequence].SequenceEqual(rebound._rates[Current.Sequence]))
            throw new ArgumentException("Ownership transfer cannot change the active clip rates.", nameof(durations));
        rebound.Current = Current;
        rebound._transitionRate = _transitionRate;
        rebound._blendCurve = _blendCurve;
        rebound._blendCurveIndex = _blendCurveIndex;
        rebound._multiplier = _multiplier;
        rebound.WrappedThisUpdate = WrappedThisUpdate;
        rebound._loopStart = _loopStart;
        rebound._loopEnd = _loopEnd;
        rebound._exitLoopFrame = _exitLoopFrame;
        return rebound;
    }

    /// <summary>
    /// OBP adapter handoff from unblended, full-loop playback. Preserve the
    /// source clip phase and use its current frame's rate on the next update.
    /// This is not a recovered native action-entry or blend-selection rule.
    /// </summary>
    public void ResumePlayback(int sequence, int currentFrame, float fraction, float multiplier)
    {
        if (!_rates.TryGetValue(sequence, out var rates) || (uint)currentFrame >= (uint)rates.Length)
            throw new ArgumentOutOfRangeException(nameof(currentFrame));
        if (!float.IsFinite(fraction) || fraction < 0 || fraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(fraction));
        if (!float.IsFinite(multiplier) || multiplier < 0 || multiplier > 4)
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        Current = new Sample(sequence, currentFrame, (currentFrame + 1) % rates.Length, fraction, false);
        _transitionRate = rates[currentFrame];
        _multiplier = multiplier;
        _blendCurve = null;
        _blendCurveIndex = 0;
        WrappedThisUpdate = false;
        _loopStart = _loopEnd = _exitLoopFrame = null;
    }

    public void Select(int sequence, int nextFrame, int blendTicks)
    {
        if (!_rates.TryGetValue(sequence, out var rates) || (uint)nextFrame >= (uint)rates.Length)
            throw new ArgumentOutOfRangeException(nameof(nextFrame));
        if (blendTicks < 1)
            throw new ArgumentOutOfRangeException(nameof(blendTicks));
        Current = new Sample(sequence, nextFrame, nextFrame, 0, true);
        _transitionRate = 1f / blendTicks;
        _multiplier = 1;
        _blendCurve = null;
        _blendCurveIndex = 0;
        WrappedThisUpdate = false;
        _loopStart = _loopEnd = _exitLoopFrame = null;
    }

    /// <summary>0x2325e0/2328c0: inclusive loop-end keyframe, then a three-update transition to loop start.</summary>
    public void SetLoopWindow(int? first, int? last)
    {
        if (first.HasValue != last.HasValue || first is int start &&
            (start < 0 || last < start || last >= FrameCount(Current.Sequence)))
            throw new ArgumentOutOfRangeException(nameof(first));
        _loopStart = first;
        _loopEnd = last;
    }

    /// <summary>0x232640/232864: on the following keyframe transition, leave the loop at the selected recovery frame.</summary>
    public void QueueLoopExit(int frame)
    {
        if ((uint)frame >= (uint)FrameCount(Current.Sequence))
            throw new ArgumentOutOfRangeException(nameof(frame));
        _exitLoopFrame = frame;
    }

    /// <summary>Native selector argument -2: the pinned 16-update stand-up curve.</summary>
    public void SelectCrouchExit(int standingSequence = 0, int nextFrame = 0)
    {
        Select(standingSequence, nextFrame, 1);
        _blendCurve = Rac1CrouchAnimation.StandUpBlend;
    }

    /// <summary>Native selector argument -1: ordinary standing transition.</summary>
    public void SelectOrdinaryStanding(int sequence = 0, int nextFrame = 0)
    {
        Select(sequence, nextFrame, 1);
        _blendCurve = Rac1OrdinaryStopAnimation.StandingBlend;
    }

    public Sample Advance()
    {
        WrappedThisUpdate = false;
        float fraction = Current.IsBlending && _blendCurve is not null
            ? _blendCurve[_blendCurveIndex++]
            : Current.Fraction + (Current.IsBlending ? _transitionRate : _multiplier * _transitionRate);
        if (fraction > 0.99f && fraction < 1.01f)
            fraction = 1f;
        if (fraction > -0.01f && fraction < 0.01f)
            fraction = 0f;
        var sample = Current;
        float[] rates = _rates[sample.Sequence];
        while (fraction >= 1f)
        {
            int frame = sample.NextFrame;
            int next = (frame + 1) % rates.Length;
            bool exitLoop = _exitLoopFrame.HasValue;
            bool wrapLoop = !exitLoop && _loopEnd.HasValue && next > _loopEnd.Value;
            if (exitLoop)
            {
                next = _exitLoopFrame!.Value;
                _exitLoopFrame = _loopStart = _loopEnd = null;
            }
            else if (wrapLoop)
                next = _loopStart!.Value;
            WrappedThisUpdate |= next == 0;
            sample = new Sample(sample.Sequence, frame, next, 0, false);
            fraction = (fraction - 1f) / _transitionRate;
            _transitionRate = rates[frame];
            if (exitLoop || wrapLoop)
            {
                fraction = 0;
                _transitionRate = exitLoop ? 1 : 1f / 3f;
            }
            fraction *= _transitionRate;
            _blendCurve = null;
        }
        Current = sample with { Fraction = fraction };
        return Current;
    }

    private static float[] Rates(IReadOnlyList<double> durations)
    {
        if (durations.Count is < 1 or > byte.MaxValue)
            throw new ArgumentException("Clip cannot represent the native frame count.", nameof(durations));
        var rates = new float[durations.Count];
        for (int frame = 0; frame < rates.Length; frame++)
        {
            rates[frame] = (float)(1d / (60d * durations[frame]));
            if (!double.IsFinite(durations[frame]) ||
                !float.IsFinite(rates[frame]) || rates[frame] <= 0 || rates[frame] > 1)
                throw new ArgumentException("Clip is outside the recovered player frame-rate range.", nameof(durations));
        }
        return rates;
    }
}

namespace OBP.RAC1.Player;

/// <summary>
/// Ordinary action-2 animation at the original 60 Hz timing scale. Owns the
/// default full-clip loop and eight-update blend, not custom loops, pose
/// decoding, special entry history, or the complete R5900 floating-point unit.
/// See Veldin 0x232670, 0x228c18 and research/RAC1_LOCOMOTION_ANIMATION.md.
/// </summary>
public sealed class Rac1LocomotionAnimationClock
{
    private readonly Rac1AnimationFrameClock _frames;

    public Rac1LocomotionAnimationClock(
        IReadOnlyList<double> walkDurations, IReadOnlyList<double> runDurations)
    {
        int entry = Rac1OrdinaryLocomotionAnimation.EntryNextFrame(3);
        _frames = new Rac1AnimationFrameClock(
            new Dictionary<int, IReadOnlyList<double>> { [3] = walkDurations, [4] = runDurations }, 3, entry, 8);
    }

    public Rac1AnimationFrameClock.Sample Current => _frames.Current;
    public float PlaybackMultiplier => _frames.PlaybackMultiplier;
    public int SubstateAge { get; private set; }

    /// <summary>
    /// Native grounded landing selector 0x2293a8: action 2 enters sequence 4
    /// with its supplied next frame and an eight- or fourteen-update blend.
    /// The usual action-2 speed assignment follows the entry.
    /// </summary>
    public static Rac1LocomotionAnimationClock SelectEntry(
        IReadOnlyList<double> walkDurations, IReadOnlyList<double> runDurations,
        int sequence, int nextFrame, int blendTicks, float scalarStep)
    {
        var clock = new Rac1LocomotionAnimationClock(walkDurations, runDurations);
        _ = Rac1OrdinaryLocomotionAnimation.SelectSequence(sequence, scalarStep, 0, false);
        clock._frames.Select(sequence, nextFrame, blendTicks);
        clock._frames.PlaybackMultiplier =
            Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(sequence, scalarStep);
        return clock;
    }

    /// <summary>
    /// OBP handoff back from airborne semantic playback. This preserves its
    /// existing selected sequence/phase, then resumes the recovered ordinary
    /// speed multiplier and physics clock. Native landing entry is not modeled.
    /// </summary>
    public static Rac1LocomotionAnimationClock ResumePlayback(
        IReadOnlyList<double> walkDurations, IReadOnlyList<double> runDurations,
        int sequence, int currentFrame, float fraction, float scalarStep) =>
        new(walkDurations, runDurations, sequence, currentFrame, fraction, scalarStep);

    private Rac1LocomotionAnimationClock(
        IReadOnlyList<double> walkDurations, IReadOnlyList<double> runDurations,
        int sequence, int currentFrame, float fraction, float scalarStep)
    {
        _ = Rac1OrdinaryLocomotionAnimation.SelectSequence(sequence, scalarStep, 0, false);
        _frames = new Rac1AnimationFrameClock(
            new Dictionary<int, IReadOnlyList<double>> { [3] = walkDurations, [4] = runDurations },
            sequence, currentFrame, 1);
        _frames.ResumePlayback(sequence, currentFrame, fraction,
            Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(sequence, scalarStep));
    }

    /// <summary>
    /// Advance with the previous update's multiplier, then run selection/rate
    /// assignment using P+0x194 after this update's movement executor. Native
    /// 0x211670 calls the executor at 0x211728 before action selection.
    /// Returns the advanced pose before selection so a presenter can snapshot
    /// it when the sequence changes. Unsupported ordinary edge updates advance
    /// animation and age but skip the helper, retaining its previous multiplier.
    /// </summary>
    public Rac1AnimationFrameClock.Sample Step(float scalarAtSelection, bool helperAdmitted, bool entryGuard = false)
    {
        // Validate before mutating, even on a skipped helper update.
        _ = Rac1OrdinaryLocomotionAnimation.SelectSequence(
            Current.Sequence, scalarAtSelection, SubstateAge, entryGuard);
        var advanced = _frames.Advance();
        if (SubstateAge < int.MaxValue)
            SubstateAge++;
        if (!helperAdmitted)
            return advanced;

        int sequence = Rac1OrdinaryLocomotionAnimation.SelectSequence(
            Current.Sequence, scalarAtSelection, SubstateAge, entryGuard);
        if (sequence != Current.Sequence)
        {
            int target = Rac1OrdinaryLocomotionAnimation.TransitionNextFrame(
                Current.Sequence, Current.NextFrame, _frames.FrameCount(Current.Sequence),
                sequence, _frames.FrameCount(sequence));
            _frames.Select(sequence, target, 8);
            SubstateAge = 0;
        }
        _frames.PlaybackMultiplier = Rac1OrdinaryLocomotionAnimation.PlaybackMultiplier(sequence, scalarAtSelection);
        return advanced;
    }
}

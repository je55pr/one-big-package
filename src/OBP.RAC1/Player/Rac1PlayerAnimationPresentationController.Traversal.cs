using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private Rac1AnimationFrameClock? _traversalClock;
    private Rac1MovementAnimationSelection? _traversalSelection;
    private double[]? _traversalBlendSource;
    private bool _traversalCycleCompleted;
    private float _traversalAuthoredAdvance;
    public Rac1WaterAnimationFacts WaterAnimationFacts => _traversalClock is null ? default :
        new(_traversalClock.Current.Sequence, (float)TraversalAuthoredAnimationTime,
            _traversalAuthoredAdvance, _traversalCycleCompleted, _traversalCycleCompleted);
    public double TraversalAuthoredAnimationTime => _traversalClock is null ? 0 :
        Rac1OrdinaryStopAnimation.EvaluateAnimationTime(_traversalClock.Current,
            CurrentClip.FrameTimelinePositions ?? throw new InvalidDataException("Traversal timeline is absent."));
    public bool TraversalAnimationCycleCompleted => _traversalCycleCompleted;
    public Rac1AnimationFrameClock.Sample? TraversalSample => _traversalClock?.Current;

    private bool StepTraversalAnimation(PlayerAnimationState state, Rac1MovementAnimationSelection? selection)
    {
        _traversalCycleCompleted = false;
        float previousTime = (float)TraversalAuthoredAnimationTime;
        _traversalAuthoredAdvance = 0;
        if (selection is not { } selected)
        {
            _traversalClock = null;
            _traversalSelection = null;
            _traversalBlendSource = null;
            return false;
        }
        Rac1PlayerActionDomain.Validate(selected.NativeAction);
        if (!float.IsFinite(selected.PlaybackMultiplier) || selected.PlaybackMultiplier < 0 ||
            selected.PlaybackMultiplier > 4 || selected.BlendTicks is 0 or < -2)
            throw new ArgumentOutOfRangeException(nameof(selection));

        bool entering = _traversalSelection is not { } previous ||
            previous.NativeAction != selected.NativeAction || previous.Sequence != selected.Sequence ||
            previous.EntryGeneration != selected.EntryGeneration;
        if (entering)
        {
            var source = Current.SampledPose?.LocalPositions ??
                CurrentClip.LocalFrames[CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];
            _traversalBlendSource = source;
            var clip = LocomotionClip(selected.Sequence);
            _traversalClock = new Rac1AnimationFrameClock(
                new Dictionary<int, IReadOnlyList<double>> { [selected.Sequence] = clip.FrameDurationsSeconds },
                selected.Sequence, selected.EntryFrame, Math.Max(1, selected.BlendTicks));
            if (selected.BlendTicks == -2)
                _traversalClock.SelectCrouchExit(selected.Sequence, selected.EntryFrame);
            else if (selected.BlendTicks == -1)
                _traversalClock.SelectOrdinaryStanding(selected.Sequence, selected.EntryFrame);
            _locomotionClock = null;
            _locomotionBlendSource = null;
            _stopClock = null;
            _stopBlendSource = null;
            _crouchClock = null;
            _crouchBlendSource = null;
            _standingClock = null;
            _standingBlendSource = null;
            _standingPendingCompletion = false;
        }
        else
        {
            if (selected.ExitLoopFrame is int exitFrame && _traversalSelection?.ExitLoopFrame != exitFrame)
                _traversalClock!.QueueLoopExit(exitFrame);
            _traversalClock!.Advance();
            // Holding the terminal pose resets the clock's transient flags.
            // Preserve this update's completion signal for the action owner.
            _traversalCycleCompleted = _traversalClock.WrappedThisUpdate;
            if (!selected.Loop && !_traversalClock.Current.IsBlending && _traversalClock.Current.NextFrame == 0)
            {
                // Baked-clip presentation fallback, not a recovered action exit.
                _traversalClock.ResumePlayback(selected.Sequence,
                    _traversalClock.FrameCount(selected.Sequence) - 1, 0, 0);
            }
        }
        _traversalSelection = selected;
        if (selected.ExitLoopFrame is null)
            _traversalClock!.SetLoopWindow(selected.LoopStartFrame, selected.LoopEndFrame);
        var sample = _traversalClock!.Current;
        bool terminal = !selected.Loop && !sample.IsBlending && sample.NextFrame == 0;
        _traversalClock.PlaybackMultiplier = terminal ? 0 : selected.PlaybackMultiplier;
        _currentSequenceId = selected.Sequence;
        float time = Rac1OrdinaryStopAnimation.EvaluateAnimationTime(sample,
            LocomotionClip(selected.Sequence).FrameTimelinePositions!);
        _traversalAuthoredAdvance = entering ? 0 : previousTime <= time ? time - previousTime :
            _traversalClock.FractionAdvanceThisUpdate;
        var semantic = selected.NativeAction is 0x13 or 0x14 or 0x15 or 0x23 ? PlayerAnimationState.Attack :
            state is PlayerAnimationState.Walk or PlayerAnimationState.Run or PlayerAnimationState.Idle
            ? PlayerAnimationState.Fall : state;
        Current = Presentation(semantic, selected.Sequence, selected.NativeAction,
            selected.Loop, entering ? _clockSeconds : Current.ClipStartedAtSeconds) with
        {
            SampledPose = new PlayerAvatarSampledPose(sample.CurrentFrame,
                SampleVertices(sample, _traversalBlendSource)),
        };
        return true;
    }
}

using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private readonly Rac1InputMagnitudeHistory _stopInputHistory = new();
    private float _previousInputMagnitude;
    private long _ordinaryActionAge;
    private Rac1AnimationFrameClock? _stopClock;
    private double[]? _stopBlendSource;
    private int _stopAction;

    public Rac1AnimationFrameClock.Sample? OrdinaryStopSample => _stopClock?.Current;

    private bool StepStopAnimation(PlayerAnimationState state, Rac1LocomotionAnimationInput input, float delayedInput)
    {
        bool admitted = input.HelperAdmitted && input.ContactMotionLength.HasValue && !input.CrouchHeld &&
            state is PlayerAnimationState.Idle or PlayerAnimationState.Walk or PlayerAnimationState.Run;
        if (!admitted)
        {
            if (_stopClock is not null)
            {
                // Other actions use their existing semantic fallback. Never
                // retain a stop clock through attack, airborne motion or reset.
                _stopClock = null;
                _stopBlendSource = null;
                Current = Current with { SampledPose = null };
            }
            return false;
        }

        if (_stopClock is not null)
        {
            _stopClock.Advance();
            PublishStopPose(state);
            if (_stopAction == 0)
            {
                if (delayedInput > Rac1OrdinaryStopAnimation.IdleRestartInputThreshold)
                    RestartLocomotion();
                else if (_stopClock.WrappedThisUpdate && _stopClock.Current.Sequence != 0)
                    EnterStanding();
                return true;
            }
            if (_stopClock.WrappedThisUpdate)
            {
                EnterStanding();
                return true;
            }

            switch (Rac1OrdinaryStopAnimation.SelectRestart(delayedInput, input.ScalarAtSelection))
            {
                case Rac1OrdinaryStopAnimation.Restart.StandingWithoutSelection:
                    // Action entry with a1=0 changes ownership without selecting
                    // an animation; ordinary idle may restart on the next update.
                    _stopAction = 0;
                    PublishStopPose(state);
                    break;
                case Rac1OrdinaryStopAnimation.Restart.Locomotion:
                    RestartLocomotion();
                    break;
            }
            return true;
        }

        if (_locomotionClock is null || !HasStopTimelines())
            return false;

        // Animation advance precedes the delayed-input release predicate.
        var advanced = _locomotionClock.Step(input.ScalarAtSelection,
            helperAdmitted: delayedInput >= Rac1OrdinaryStopAnimation.ReleaseInputThreshold);
        _ordinaryActionAge = Math.Min(_ordinaryActionAge + 1, int.MaxValue);
        if (advanced.Sequence != _locomotionClock.Current.Sequence)
            _locomotionBlendSource = SampleVertices(advanced);
        PublishLocomotionPose();
        var release = Rac1OrdinaryStopAnimation.SelectRelease(delayedInput,
            input.ContactMotionLength!.Value, _stopInputHistory.Read(4), (int)_ordinaryActionAge);
        if (release == Rac1OrdinaryStopAnimation.Release.KeepLocomotion)
            return true;

        var sample = _locomotionClock.Current;
        _stopBlendSource = SampleVertices(sample);
        int sequence = release == Rac1OrdinaryStopAnimation.Release.MiddleStop ? 20 :
            Rac1RatchetSequenceSelection.SelectDefaultStopSequence(sample.IsBlending ? 255 : sample.Sequence,
                Rac1OrdinaryStopAnimation.EvaluateAnimationTime(sample,
                    LocomotionClip(sample.Sequence).FrameTimelinePositions!));
        _stopClock = new Rac1AnimationFrameClock(StopDurations(), sequence, sequence == 20 ? 7 : 4, 9);
        _stopAction = 3;
        _locomotionClock = null;
        _locomotionBlendSource = null;
        if (release == Rac1OrdinaryStopAnimation.Release.Standing)
        {
            _stopAction = 0;
            _stopClock.SelectOrdinaryStanding();
        }
        PublishStopPose(state);
        return true;
    }

    private bool HasStopTimelines() => new[] { 0, 3, 4, 5, 6, 20 }.All(sequence =>
        _avatar.AnimationClips.Any(clip => clip.Id == Rac1PlayerAvatarProvider.AnimationDescriptor(sequence).Id &&
            clip.FrameTimelinePositions?.Count == clip.FrameCount));

    private Dictionary<int, IReadOnlyList<double>> StopDurations() => new[] { 0, 5, 6, 20 }.ToDictionary(
        sequence => sequence, sequence => LocomotionClip(sequence).FrameDurationsSeconds);

    private void EnterStanding()
    {
        _stopBlendSource = Current.SampledPose!.LocalPositions;
        _stopClock!.SelectOrdinaryStanding();
        _stopAction = 0;
        PublishStopPose(PlayerAnimationState.Idle);
    }

    private void RestartLocomotion()
    {
        _locomotionBlendSource = Current.SampledPose!.LocalPositions;
        _locomotionClock = new Rac1LocomotionAnimationClock(
            LocomotionClip(3).FrameDurationsSeconds, LocomotionClip(4).FrameDurationsSeconds);
        _ordinaryActionAge = 0;
        _stopClock = null;
        _stopBlendSource = null;
        PublishLocomotionPose();
    }

    private void PublishStopPose(PlayerAnimationState state)
    {
        var sample = _stopClock!.Current;
        bool changed = _currentSequenceId != sample.Sequence;
        _currentSequenceId = sample.Sequence;
        Current = Presentation(_stopAction == 0 ? PlayerAnimationState.Idle : state,
            sample.Sequence, _stopAction, loop: _stopAction == 0,
            changed ? _clockSeconds : Current.ClipStartedAtSeconds) with
        {
            SampledPose = new PlayerAvatarSampledPose(sample.CurrentFrame, SampleVertices(sample, _stopBlendSource)),
        };
    }
}

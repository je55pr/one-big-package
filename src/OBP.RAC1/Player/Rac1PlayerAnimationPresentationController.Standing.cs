using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private Rac1AnimationFrameClock? _standingClock;
    private double[]? _standingBlendSource;
    private bool _standingPendingCompletion;

    public Rac1AnimationFrameClock.Sample? OrdinaryStandingSample => _standingClock?.Current;

    private bool StepStandingAnimation(PlayerAnimationState state, Rac1LocomotionAnimationInput input, float delayedInput)
    {
        bool supported = input.HelperAdmitted && !input.CrouchHeld && state is
            PlayerAnimationState.Idle or PlayerAnimationState.Walk or PlayerAnimationState.Run or PlayerAnimationState.Land;
        if (_standingClock is not null)
        {
            if (supported)
            {
                bool retainedCompletion = _standingPendingCompletion || _standingClock.WrappedThisUpdate;
                _standingPendingCompletion = false;
                _standingClock.Advance();
                if ((retainedCompletion || _standingClock.WrappedThisUpdate) && _standingClock.Current.Sequence != 0)
                {
                    _standingBlendSource = SampleVertices(_standingClock.Current, _standingBlendSource);
                    _standingClock.SelectCrouchExit();
                }
                PublishStandingPose();
                if (delayedInput <= Rac1OrdinaryStopAnimation.IdleRestartInputThreshold)
                    return true;
                _locomotionBlendSource = Current.SampledPose!.LocalPositions;
                _locomotionClock = new Rac1LocomotionAnimationClock(
                    LocomotionClip(3).FrameDurationsSeconds, LocomotionClip(4).FrameDurationsSeconds);
                _ordinaryActionAge = 0;
                _standingClock = null;
                _standingBlendSource = null;
                _standingPendingCompletion = false;
                PublishLocomotionPose();
                return true;
            }

            // An unrelated semantic action must not retain this sampled pose.
            var sample = _standingClock.Current;
            double elapsed = CurrentClip.FrameDurationsSeconds.Take(sample.CurrentFrame).Sum() +
                sample.Fraction * CurrentClip.FrameDurationsSeconds[sample.CurrentFrame];
            Current = Current with
            {
                ClipStartedAtSeconds = _clockSeconds - elapsed,
                SampledPose = null,
            };
            _standingClock = null;
            _standingBlendSource = null;
            _standingPendingCompletion = false;
            return false;
        }

        if (!supported || input.Active || _locomotionClock is not null || _stopClock is not null)
            return false;

        if (_currentSequenceId == 0)
        {
            // Initial idle and completed crouch stand-up already selected 0.
            // Resume its exact phase; do not restart a transition or use the
            // renderer's discrete keyframe clock for subsequent standing.
            var clip = CurrentClip;
            double elapsed = Current.ElapsedAt(_clockSeconds) % clip.DurationSeconds;
            int frame = clip.FrameIndexAt(elapsed, loop: true);
            float fraction = (float)((elapsed - clip.FrameDurationsSeconds.Take(frame).Sum()) /
                clip.FrameDurationsSeconds[frame]);
            _standingClock = StandingClock();
            _standingClock.ResumePlayback(0, frame, Math.Clamp(fraction, 0, MathF.BitDecrement(1f)), 1);
            PublishStandingPose();
            return true;
        }

        if (Current.SemanticState is not (PlayerAnimationState.JumpRise or PlayerAnimationState.Fall or PlayerAnimationState.Land))
            return false;

        // Compatibility for hosts without the native GroundEntry contract.
        // Quiet supported landings cannot infer a run from launch history.
        // This entry timing is an adapter fallback; a native traversal session
        // retains its air clip until its recovered landing selector fires.
        _standingBlendSource = CurrentPose();
        _standingClock = StandingClock();
        _standingClock.SelectCrouchExit();
        PublishStandingPose();
        return true;
    }

    private void EnterGroundAnimation(PlayerAnimationState state, Rac1LocomotionAnimationInput input,
        Rac1MovementAnimationSelection entry)
    {
        bool preserveClip = entry.NativeAction == 0 && entry.BlendTicks == 0;
        // Admission belongs to the source movement session. A subsequent host
        // collision pass may already report a different support fact here.
        if (entry.NativeAction is not (0 or 2 or 3 or 4) ||
            entry.BlendTicks < -2 || entry.BlendTicks == 0 && !preserveClip || !float.IsFinite(entry.PlaybackMultiplier) ||
            entry.PlaybackMultiplier < 0 || entry.PlaybackMultiplier > 4)
            throw new ArgumentOutOfRangeException(nameof(entry));
        if (!preserveClip && (entry.NativeAction, entry.Sequence) is not ((0, 0) or (2, 4) or (3, 5) or (4, 13)))
            throw new NotSupportedException("Grounded landing selection is outside the recovered ordinary families.");
        if (preserveClip && (_traversalClock is null || _traversalClock.Current.Sequence != entry.Sequence))
            throw new InvalidOperationException("A no-selection landing requires its exact active airborne clock.");

        // The old clock advances before the native selector changes ownership.
        // This matters both for blend snapshots and for a no-selection handoff:
        // neither entry may repeat the preceding physics update's pose.
        if (_traversalSelection is { } previous)
            StepTraversalAnimation(state, previous);
        double[] source = CurrentPose();
        var retainedClock = preserveClip ? _traversalClock!.RebindSequences(new[] { 0, entry.Sequence }.Distinct()
            .ToDictionary(sequence => sequence, sequence => LocomotionClip(sequence).FrameDurationsSeconds)) : null;
        double[]? retainedBlendSource = _traversalBlendSource;
        // The baked nonlooping policy freezes a terminal pose and publishes
        // its event only once. A later ownership transfer must remember that
        // completion even after the transient event/clock flag has cleared.
        bool retainedCompletion = preserveClip && (_traversalCycleCompleted ||
            _traversalSelection is { Loop: false } &&
            _traversalClock is { PlaybackMultiplier: 0, Current: { IsBlending: false, NextFrame: 0 } });
        _traversalClock = null;
        _traversalSelection = null;
        _traversalBlendSource = null;
        _traversalCycleCompleted = false;
        _locomotionClock = null;
        _locomotionBlendSource = null;
        _stopClock = null;
        _stopBlendSource = null;
        _crouchClock = null;
        _crouchBlendSource = null;
        _standingClock = null;
        _standingBlendSource = null;
        _standingPendingCompletion = false;
        _ordinaryActionAge = 0;

        switch (entry.NativeAction)
        {
            case 0:
                _standingBlendSource = preserveClip ? retainedBlendSource : source;
                _standingClock = retainedClock ?? StandingClock();
                _standingPendingCompletion = retainedCompletion;
                if (!preserveClip)
                    SelectGroundEntry(_standingClock, entry);
                PublishStandingPose();
                break;
            case 2:
                _locomotionBlendSource = source;
                _locomotionClock = Rac1LocomotionAnimationClock.SelectEntry(
                    LocomotionClip(3).FrameDurationsSeconds, LocomotionClip(4).FrameDurationsSeconds,
                    entry.Sequence, entry.EntryFrame, entry.BlendTicks, input.ScalarAtSelection);
                PublishLocomotionPose();
                break;
            case 3:
                _stopBlendSource = source;
                _stopClock = new Rac1AnimationFrameClock(StopDurations(), entry.Sequence, entry.EntryFrame, 1);
                SelectGroundEntry(_stopClock, entry);
                _stopAction = 3;
                PublishStopPose(state);
                break;
            case 4:
                _crouchBlendSource = source;
                _crouchClock = new Rac1AnimationFrameClock(new[] { 0, 13, 14, 15 }.ToDictionary(
                    sequence => sequence, sequence => LocomotionClip(sequence).FrameDurationsSeconds),
                    entry.Sequence, entry.EntryFrame, 1);
                SelectGroundEntry(_crouchClock, entry);
                _crouchAge = 0;
                PublishCrouchPose();
                break;
        }
    }

    private static void SelectGroundEntry(Rac1AnimationFrameClock clock, Rac1MovementAnimationSelection entry)
    {
        if (entry.BlendTicks == -2)
            clock.SelectCrouchExit(entry.Sequence, entry.EntryFrame);
        else if (entry.BlendTicks == -1)
            clock.SelectOrdinaryStanding(entry.Sequence, entry.EntryFrame);
        else
            clock.Select(entry.Sequence, entry.EntryFrame, entry.BlendTicks);
        clock.PlaybackMultiplier = entry.PlaybackMultiplier;
    }

    private Rac1AnimationFrameClock StandingClock() => new(
        new Dictionary<int, IReadOnlyList<double>> { [0] = LocomotionClip(0).FrameDurationsSeconds }, 0, 0, 1);

    private double[] CurrentPose() => Current.SampledPose?.LocalPositions ??
        CurrentClip.LocalFrames[CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];

    private void PublishStandingPose()
    {
        var sample = _standingClock!.Current;
        bool changed = _currentSequenceId != sample.Sequence;
        _currentSequenceId = sample.Sequence;
        _airborneReturnState = PlayerAnimationState.Idle;
        Current = Presentation(PlayerAnimationState.Idle, sample.Sequence, 0, true,
            changed ? _clockSeconds : Current.ClipStartedAtSeconds) with
        {
            SampledPose = new PlayerAvatarSampledPose(sample.CurrentFrame,
                SampleVertices(sample, _standingBlendSource)),
        };
    }
}

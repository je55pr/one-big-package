using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private Rac1AnimationFrameClock? _crouchClock;
    private double[]? _crouchBlendSource;
    private int _crouchAge;

    public Rac1AnimationFrameClock.Sample? CrouchSample => _crouchClock?.Current;
    public float? CrouchPlaybackMultiplier => _crouchClock?.PlaybackMultiplier;

    // Host support/action admission remains a presentation approximation. The
    // selected sequences, blend clocks and yaw predicates are recovered rules.
    private bool StepCrouchAnimation(PlayerAnimationState state, Rac1LocomotionAnimationInput input)
    {
        bool ordinary = input.HelperAdmitted && state is
            PlayerAnimationState.Idle or PlayerAnimationState.Walk or PlayerAnimationState.Run;
        bool held = ordinary && input.CrouchHeld;
        if (_crouchClock is not null)
        {
            _crouchClock.Advance();
            _crouchAge++;
            bool exiting = _crouchClock.Current.Sequence == 0;
            if (!ordinary || (exiting && !held && (input.Active || !_crouchClock.Current.IsBlending)))
            {
                PublishCrouchPose();
                // Keep the sampled pose for a locomotion entry to snapshot.
                // Other actions use the semantic fallback below.
                var sample = _crouchClock.Current;
                double elapsed = CurrentClip.FrameDurationsSeconds.Take(sample.CurrentFrame).Sum() +
                    sample.Fraction * CurrentClip.FrameDurationsSeconds[sample.CurrentFrame];
                Current = Current with { ClipStartedAtSeconds = _clockSeconds - elapsed };
                _crouchClock = null;
                _crouchBlendSource = null;
                if (!ordinary)
                {
                    Current = Current with { SampledPose = null };
                    // Idle is also the neutral semantic label for crouch.
                    // An unsupported neutral host frame must not retain 13-15.
                    if (state == PlayerAnimationState.Idle && _currentSequenceId != 0)
                    {
                        _currentSequenceId = 0;
                        Current = Presentation(state, 0, 0, true, _clockSeconds);
                    }
                }
                return false;
            }

            if (exiting && held)
            {
                PublishCrouchPose();
                EnterCrouch(previousPoseIsStanding: false);
            }
            else if (!exiting && !held && _crouchAge > Rac1CrouchAnimation.MinimumReleaseAge)
            {
                _crouchBlendSource = SampleVertices(_crouchClock.Current, _crouchBlendSource);
                _crouchClock.SelectCrouchExit();
            }
        }
        else if (held)
        {
            if (_standingClock is not null)
            {
                _standingClock.Advance();
                PublishStandingPose();
                _standingClock = null;
                _standingBlendSource = null;
                _standingPendingCompletion = false;
            }
            if (_locomotionClock is not null)
            {
                _locomotionClock.Step(input.ScalarAtSelection, helperAdmitted: false);
                PublishLocomotionPose();
                _locomotionClock = null;
                _locomotionBlendSource = null;
            }
            EnterCrouch(previousPoseIsStanding: _currentSequenceId == 0);
        }
        else
            return false;

        if (_crouchClock!.Current.Sequence != 0)
        {
            int? next = Rac1CrouchAnimation.SelectTurn(
                _crouchClock.Current.Sequence, input.InputMagnitude, input.NativeYawVelocity);
            if (next.HasValue && next.Value != _crouchClock.Current.Sequence)
            {
                _crouchBlendSource = SampleVertices(_crouchClock.Current, _crouchBlendSource);
                _crouchClock.Select(next.Value, 0, next.Value == 13 ? 7 : 5);
            }
            if (next is 14 or 15)
                _crouchClock.PlaybackMultiplier = Rac1CrouchAnimation.TurnPlaybackMultiplier(input.NativeYawVelocity);
        }
        PublishCrouchPose();
        return true;
    }

    private void EnterCrouch(bool previousPoseIsStanding)
    {
        int frame = CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop);
        _crouchBlendSource = Current.SampledPose?.LocalPositions ?? CurrentClip.LocalFrames[frame];
        var durations = new[] { 0, 13, 14, 15 }.ToDictionary(
            sequence => sequence, sequence => LocomotionClip(sequence).FrameDurationsSeconds);
        _crouchClock = new Rac1AnimationFrameClock(durations, 13, Rac1CrouchAnimation.EntryFrame,
            Rac1CrouchAnimation.EntryBlendTicks(previousPoseIsStanding));
        _crouchAge = 0;
        // Native requests loop [3,18], but the checked setter rejects end 18
        // for the admitted 15-frame sequence 13. The default full loop remains.
    }

    private void PublishCrouchPose()
    {
        var sample = _crouchClock!.Current;
        bool changed = _currentSequenceId != sample.Sequence;
        _currentSequenceId = sample.Sequence;
        _airborneReturnState = PlayerAnimationState.Idle;
        Current = Presentation(PlayerAnimationState.Idle, sample.Sequence,
            sample.Sequence == 0 ? 0 : 4, true, changed ? _clockSeconds : Current.ClipStartedAtSeconds) with
        {
            SampledPose = new PlayerAvatarSampledPose(sample.CurrentFrame,
                SampleVertices(sample, _crouchBlendSource)),
        };
    }
}

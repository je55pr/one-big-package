using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Host facts for the bounded ordinary animation path. Active means held input
/// in ordinary locomotion, including its short unsupported edge interval.
/// HelperAdmitted requires support; ScalarAtSelection is the updated controller
/// scalar before host collision, corresponding to native P+0x194 at selector time.
/// CrouchHeld is an admitted grounded request; InputMagnitude and
/// NativeYawVelocity carry conditioned input and the updated native yaw step.
/// ContactMotionLength is the completed, admitted primary contact vector's 3D
/// length. UncappedInputMagnitude preserves the separate native input history.
/// GroundEntry is a one-update native landing selector, owned by the movement
/// session; it transfers playback to the corresponding ordinary action clock.
/// Action 0 with BlendTicks=0 transfers ownership without selecting a sequence;
/// its Sequence must name the existing airborne clock.
/// </summary>
public readonly record struct Rac1LocomotionAnimationInput(
    bool Active, bool HelperAdmitted, float ScalarAtSelection,
    bool CrouchHeld = false, float InputMagnitude = 0, float NativeYawVelocity = 0,
    float? ContactMotionLength = null, float? UncappedInputMagnitude = null,
    Rac1MovementAnimationSelection? Traversal = null,
    Rac1MovementAnimationSelection? GroundEntry = null);

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private Rac1LocomotionAnimationClock? _locomotionClock;
    private double[]? _locomotionBlendSource;

    public Rac1AnimationFrameClock.Sample? OrdinaryLocomotionSample => _locomotionClock?.Current;
    public float? OrdinaryLocomotionMultiplier => _locomotionClock?.PlaybackMultiplier;
    public float? OrdinaryLocomotionFractionAdvance => _locomotionClock?.FractionAdvanceThisUpdate;

    /// <summary>
    /// Call once per 60 Hz movement update, never from the render loop. Native
    /// clock/selector rules apply to an ordinary idle entry and its uninterrupted
    /// walk/run interval, ordinary stop/restart, and crouch/turn/stand-up. Other action entries
    /// retain the semantic fallback.
    /// Vertex interpolation below is an OBP presentation approximation: retail
    /// blends joint poses before skinning, which baked vertex clips cannot recover.
    /// </summary>
    public PlayerAnimationPresentation StepOrdinaryAnimation(
        PlayerAnimationState state, double clockSeconds, Rac1LocomotionAnimationInput input)
    {
        RestoreEquipmentBasePose();
        if (!float.IsFinite(input.InputMagnitude) || input.InputMagnitude < 0 || input.InputMagnitude > 1 ||
            input.ContactMotionLength is { } length && (!float.IsFinite(length) || length < 0))
            throw new ArgumentOutOfRangeException(nameof(input));
        bool returningFromAir = Current.SemanticState is
            PlayerAnimationState.JumpRise or PlayerAnimationState.Fall or PlayerAnimationState.Land;
        SetClock(clockSeconds);
        float delayedInput = _previousInputMagnitude;
        _previousInputMagnitude = input.InputMagnitude;
        _stopInputHistory.Append(input.UncappedInputMagnitude ?? input.InputMagnitude);
        if (input.GroundEntry is { } groundEntry)
        {
            EnterGroundAnimation(state, input, groundEntry);
            return Current;
        }
        if (StepTraversalAnimation(state, input.Traversal))
            return Current;
        if (StepCrouchAnimation(state, input))
        {
            _stopClock = null;
            _stopBlendSource = null;
            return Current;
        }
        if (StepStopAnimation(state, input, delayedInput))
            return Current;
        if (StepStandingAnimation(state, input, delayedInput))
            return Current;
        bool ordinary = input.Active && state is
            PlayerAnimationState.Idle or PlayerAnimationState.Walk or PlayerAnimationState.Run;
        if (_locomotionClock is not null)
        {
            if (ordinary)
            {
                var advanced = _locomotionClock.Step(input.ScalarAtSelection, input.HelperAdmitted);
                _ordinaryActionAge = Math.Min(_ordinaryActionAge + 1L, int.MaxValue);
                if (advanced.Sequence != _locomotionClock.Current.Sequence)
                    _locomotionBlendSource = SampleVertices(advanced);
                PublishLocomotionPose();
                return Current;
            }

            // Unadmitted contact or another action retains the semantic fallback.
            // Preserve the current clip phase while handing it back.
            var sample = _locomotionClock.Current;
            double elapsed = CurrentClip.FrameDurationsSeconds.Take(sample.CurrentFrame).Sum() +
                sample.Fraction * CurrentClip.FrameDurationsSeconds[sample.CurrentFrame];
            Current = Current with { SampledPose = null, ClipStartedAtSeconds = _clockSeconds - elapsed };
            _locomotionClock = null;
            _locomotionBlendSource = null;
        }

        if (ordinary && Current.SemanticState == PlayerAnimationState.Idle && _currentSequenceId == 0)
        {
            int frame = CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop);
            _locomotionBlendSource = Current.SampledPose?.LocalPositions ?? CurrentClip.LocalFrames[frame];
            _locomotionClock = new Rac1LocomotionAnimationClock(
                LocomotionClip(3).FrameDurationsSeconds, LocomotionClip(4).FrameDurationsSeconds);
            _ordinaryActionAge = 0;
            PublishLocomotionPose();
            return Current;
        }

        SetAnimationState(state);
        if (returningFromAir && input.Active && input.HelperAdmitted &&
            state is PlayerAnimationState.Land or PlayerAnimationState.Walk or PlayerAnimationState.Run &&
            _currentSequenceId is 3 or 4)
        {
            // The semantic landing path already chose a witnessed sequence.
            // Reattach its phase to the native-rate clock instead of leaving
            // subsequent locomotion permanently at the unscaled asset FPS.
            var clip = CurrentClip;
            double elapsed = Current.ElapsedAt(_clockSeconds) % clip.DurationSeconds;
            int frame = clip.FrameIndexAt(elapsed, loop: true);
            float fraction = (float)((elapsed - clip.FrameDurationsSeconds.Take(frame).Sum()) /
                clip.FrameDurationsSeconds[frame]);
            fraction = Math.Clamp(fraction, 0, MathF.BitDecrement(1f));
            _locomotionClock = Rac1LocomotionAnimationClock.ResumePlayback(
                LocomotionClip(3).FrameDurationsSeconds, LocomotionClip(4).FrameDurationsSeconds,
                _currentSequenceId, frame, fraction, input.ScalarAtSelection);
            _locomotionBlendSource = null;
            _ordinaryActionAge = 0;
            PublishLocomotionPose();
        }
        return Current;
    }

    /// <summary>Host teleport/restart boundary; this is not a recovered action transition.</summary>
    public void ResetPhysicsPresentation()
    {
        RestoreEquipmentBasePose();
        _equipmentAmount = 0;
        _clockSeconds = 0;
        _locomotionClock = null;
        _locomotionBlendSource = null;
        _crouchClock = null;
        _crouchBlendSource = null;
        _crouchAge = 0;
        _stopClock = null;
        _stopBlendSource = null;
        _stopAction = 0;
        _ordinaryActionAge = 0;
        _previousInputMagnitude = 0;
        _stopInputHistory.Reset();
        _traversalClock = null;
        _traversalSelection = null;
        _traversalBlendSource = null;
        _traversalCycleCompleted = false;
        _standingClock = null;
        _standingBlendSource = null;
        _standingPendingCompletion = false;
        _airborneReturnState = PlayerAnimationState.Idle;
        _attackReturnState = PlayerAnimationState.Idle;
        _currentSequenceId = 0;
        Current = Presentation(PlayerAnimationState.Idle, 0,
            Rac1RatchetSequenceSelection.NeutralActionState, loop: true, started: 0);
    }

    private void PublishLocomotionPose()
    {
        var sample = _locomotionClock!.Current;
        bool changed = _currentSequenceId != sample.Sequence;
        _currentSequenceId = sample.Sequence;
        _airborneReturnState = sample.Sequence == 3 ? PlayerAnimationState.Walk : PlayerAnimationState.Run;
        Current = Presentation(_airborneReturnState, sample.Sequence, nativeState: 2,
            loop: true, changed ? _clockSeconds : Current.ClipStartedAtSeconds) with
        {
            SampledPose = new PlayerAvatarSampledPose(sample.CurrentFrame, SampleVertices(sample)),
        };
    }

    private double[] SampleVertices(Rac1AnimationFrameClock.Sample sample) =>
        SampleVertices(sample, _locomotionBlendSource);

    private double[] SampleVertices(Rac1AnimationFrameClock.Sample sample, double[]? blendSource)
    {
        var clip = LocomotionClip(sample.Sequence);
        double[] source = sample.IsBlending ? blendSource! : clip.LocalFrames[sample.CurrentFrame];
        double[] target = clip.LocalFrames[sample.NextFrame];
        var positions = new double[target.Length];
        for (int coordinate = 0; coordinate < positions.Length; coordinate++)
            positions[coordinate] = source[coordinate] + (target[coordinate] - source[coordinate]) * sample.Fraction;
        SampleAttachments(positions, source, target, sample.Fraction);
        return positions;
    }

    private PlayerAvatarAnimationClip LocomotionClip(int sequence) =>
        _avatar.AnimationClips.Single(clip => clip.Id == Rac1PlayerAvatarProvider.AnimationDescriptor(sequence).Id);
}

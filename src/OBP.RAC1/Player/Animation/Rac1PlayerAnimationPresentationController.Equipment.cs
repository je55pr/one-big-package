using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private readonly IReadOnlyList<float>? _equipmentMask;
    private readonly Rac1RatchetEquipmentRig? _equipmentRig;
    private PlayerAnimationPresentation? _equipmentBase;
    private float _equipmentAmount;
    private int? _equipmentSequence;
    private double? _equipmentPhase;
    private bool _equipmentLoop, _equipmentFullBody;
    // How much of the whole-body use pose is applied; moves gradually so state changes never pop the legs.
    private float _equipmentBodyBlend;

    private void RestoreEquipmentBasePose()
    {
        JointEaseSkinChange = null;
        // The procedural joint layer is applied last, so it is undone first.
        if (_easeBase is not null)
        {
            Current = _easeBase;
            _easeBase = null;
        }
        if (_armHoldBase is not null)
        {
            Current = _armHoldBase;
            _armHoldBase = null;
        }
        if (_equipmentBase is null) return;
        Current = _equipmentBase;
        _equipmentBase = null;
    }

    /// <summary>
    /// Equipment channels compose before skinning over the source joint lists.
    /// The ordinary animation clock and its unmodified vertices stay intact.
    /// </summary>
    public PlayerAnimationPresentation ApplyEquipmentPose(int? sourceSequence, double? phaseSeconds = null, bool loop = true, bool fullBody = false)
    {
        BeginArmHoldTick();
        ApplyEquipmentPoseCore(sourceSequence, phaseSeconds, loop, fullBody);
        ApplyArmHold();
        return ApplyJointEases();
    }

    private PlayerAnimationPresentation ApplyEquipmentPoseCore(int? sourceSequence, double? phaseSeconds, bool loop, bool fullBody)
    {
        RestoreEquipmentBasePose();
        if (_equipmentMask is null) { _equipmentAmount = 0; _equipmentBodyBlend = 0; return Current; }
        bool freshEntry = _equipmentAmount == 0;
        if (sourceSequence is null)
        {
            _equipmentAmount = Math.Max(0, _equipmentAmount - .07f); // 0x20e728 hold-release fade.
            if (_equipmentAmount == 0 || _equipmentSequence is null) { _equipmentBodyBlend = 0; return Current; }
            sourceSequence = _equipmentSequence; phaseSeconds = _equipmentPhase; loop = _equipmentLoop;
            fullBody = _equipmentFullBody && Current.SemanticState == PlayerAnimationState.Idle;
        }
        else
        {
            _equipmentSequence = sourceSequence; _equipmentPhase = phaseSeconds;
            _equipmentLoop = loop; _equipmentFullBody = fullBody;
            _equipmentAmount = Math.Min(1, _equipmentAmount + .2f);
        }
        // The whole-body pose follows the requested state at a bounded rate; a fresh use starts at it.
        float wantBody = fullBody ? 1f : 0f;
        _equipmentBodyBlend = freshEntry ? wantBody
            : Math.Clamp(wantBody, _equipmentBodyBlend - .15f, _equipmentBodyBlend + .15f);
        var descriptor = Rac1PlayerAvatarProvider.AnimationDescriptor(sourceSequence.Value);
        var clip = _avatar.AnimationClips.Single(clip => clip.Id == descriptor.Id);
        var sample = clip.SampleAt(phaseSeconds ?? _clockSeconds, loop);
        int frame = sample.Frame, next = sample.NextFrame;
        float fraction = sample.Fraction;
        var a = clip.LocalFrames[frame]; var b = clip.LocalFrames[next];
        var baseline = Current.SampledPose?.LocalPositions ?? CurrentClip.LocalFrames[
            CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];
        if (_equipmentRig is not null)
        {
            var basis = EquipmentBaseSample();
            var positions = _equipmentRig.Sample(basis, new(sourceSequence.Value, frame, next, fraction, false),
                _equipmentAmount, fullBody, baseline, _equipmentBodyBlend);
            var sockets = _attachments.GetOrCreateValue(positions);
            sockets[Rac1ClankPackAssets.SocketKey] = _equipmentRig.Attachment(Rac1ClankPackAssets.RatchetJoint);
            sockets[Rac1WrenchAttachment.SocketKey] = _equipmentRig.Attachment(56);
            sockets[Gameplay.Rac1BombProjectileSession.GloveSocketKey] = _equipmentRig.Attachment(54);
            sockets[Gameplay.Rac1WeaponProfiles.HandSocket(0)] = _equipmentRig.Attachment(56);
            sockets[Gameplay.Rac1WeaponProfiles.HandSocket(1)] = _equipmentRig.Attachment(55);
            sockets[Gameplay.Rac1WeaponProfiles.HandSocket(6)] = _equipmentRig.Attachment(54);
            _equipmentBase = Current;
            Current = Current with { SampledPose = new(Current.SampledPose?.FrameIndex ?? 0, positions) };
            return Current;
        }
        var result = new double[baseline.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = baseline[i] + (a[i] + (b[i] - a[i]) * fraction - baseline[i]) * (fullBody ? 1 : _equipmentMask[i / 3]) * _equipmentAmount;
        SampleAttachments(result, baseline, baseline, 0);
        if (clip.AttachmentFrames is { } tracks)
        {
            var attached = _attachments.GetOrCreateValue(result);
            foreach (var pair in tracks)
            {
                var target = PlayerAvatarAttachmentPose.Interpolate(pair.Value[frame], pair.Value[next], fraction);
                attached[pair.Key] = attached.TryGetValue(pair.Key, out var original)
                    ? PlayerAvatarAttachmentPose.Interpolate(original, target, _equipmentAmount) : target;
            }
        }
        _equipmentBase = Current;
        Current = Current with { SampledPose = new(Current.SampledPose?.FrameIndex ?? 0, result) };
        return Current;
    }

    public Rac1AnimationFrameClock.Sample AnimationSoundSample => EquipmentBaseSample();
    public float AnimationPlaybackMultiplier =>
        _traversalClock?.Current.Sequence == _currentSequenceId ? _traversalClock.PlaybackMultiplier :
        _crouchClock?.Current.Sequence == _currentSequenceId ? _crouchClock.PlaybackMultiplier :
        _stopClock?.Current.Sequence == _currentSequenceId ? _stopClock.PlaybackMultiplier :
        _standingClock?.Current.Sequence == _currentSequenceId ? _standingClock.PlaybackMultiplier :
        _locomotionClock?.Current.Sequence == _currentSequenceId ? _locomotionClock.PlaybackMultiplier : 1;
    public int AudioNativeAction => int.Parse(Current.SourceStateKey!, System.Globalization.CultureInfo.InvariantCulture);
    public int AudioActionAge => (int)Math.Min(_ordinaryActionAge, int.MaxValue);

    private Rac1AnimationFrameClock.Sample EquipmentBaseSample()
    {
        foreach (var sample in new[] { TraversalSample, CrouchSample, OrdinaryStopSample, OrdinaryStandingSample, OrdinaryLocomotionSample })
            if (sample is { } current && current.Sequence == _currentSequenceId) return current;
        var sampled = CurrentClip.SampleAt(Current.ElapsedAt(_clockSeconds), Current.Loop);
        return new(_currentSequenceId, sampled.Frame, sampled.NextFrame, sampled.Fraction, false);
    }
}

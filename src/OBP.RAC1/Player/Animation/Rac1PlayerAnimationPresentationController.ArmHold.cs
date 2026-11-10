using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private int _armHoldSlots;
    private readonly float[] _armHoldWeight = new float[Rac1RatchetArmHold.SlotCount];
    private PlayerAnimationPresentation? _armHoldBase;

    internal Rac1RatchetEquipmentRig? EquipmentRig => _equipmentRig;
    /// <summary>Resolved local finger channels for the source held-glove pose-copy path.</summary>
    public Rac1RatchetHandPose CurrentHandPose() =>
        (_equipmentRig ?? throw new InvalidOperationException("The native R&C1 equipment rig is unavailable."))
        .HandPose(EquipmentBaseSample(), ResolvedBodyPose(), _easeOffsets);

    /// <summary>Hold layer weights (right arm, left arm), for diagnostics and tests.</summary>
    public (float Right, float Left) ArmHoldWeights => (_armHoldWeight[0], _armHoldWeight[1]);

    /// <summary>
    /// Step the held-weapon arm layers once per 60 Hz update (<c>0x20dd20</c>). The selected item's
    /// slot animation count enables slot 0 (right arm) and, at two, slot 1 (left arm). Each weight
    /// moves by 0.1 per update toward its target.
    /// </summary>
    public void StepArmHold(int slotCount)
    {
        _armHoldSlots = Math.Clamp(slotCount, 0, Rac1RatchetArmHold.SlotCount);
        for (int slot = 0; slot < _armHoldWeight.Length; slot++)
        {
            bool wanted = _armHoldSlots != 0 && (slot == 0 || _armHoldSlots == 2);
            float step = wanted ? .1f : -.1f;
            _armHoldWeight[slot] = Math.Clamp(_armHoldWeight[slot] + step, 0, 1);
        }
    }

    // The hold sits under any equipment use pose: the rig layers it right after the body pose and before
    // the equipment layer, so a use animation blends from the hold rather than from the bare body.
    private void PushArmHoldWeights() => _equipmentRig?.SetArmHoldWeights(_armHoldWeight[0], _armHoldWeight[1]);

    // The body pose with its transition blend resolved, from the vertices the body animation published
    // this update (before the hold or the eases replaced them).
    private PlayerSkeletalPose? ResolvedBodyPose()
    {
        var body = _equipmentBase ?? _armHoldBase ?? _easeBase ?? Current;
        return body.SampledPose is { } sampled && _bodySkeletalPoses.TryGetValue(sampled.LocalPositions, out var resolved)
            ? resolved.Pose : null;
    }

    // One arm hold update per tick, before any pose is evaluated.
    private void BeginArmHoldTick()
    {
        PushArmHoldWeights();
        _equipmentRig?.BeginArmHoldTick(EquipmentBaseSample());
    }

    private PlayerAnimationPresentation ApplyArmHold()
    {
        PushArmHoldWeights();
        // An equipment pose applied this update already includes the hold under it.
        if (_equipmentBase is not null || _equipmentRig is not { HasArmHold: true } rig ||
            _armHoldWeight[0] <= 0 && _armHoldWeight[1] <= 0)
            return Current;
        var baseline = Current.SampledPose?.LocalPositions ?? CurrentClip.LocalFrames[
            CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];
        if (rig.SampleArmHold(EquipmentBaseSample(), baseline, ResolvedBodyPose()) is not { } positions) return Current;
        var sockets = _attachments.GetOrCreateValue(positions);
        if (_attachments.TryGetValue(baseline, out var source))
            foreach (var (key, pose) in source) sockets[key] = pose;
        sockets[Rac1WrenchAttachment.SocketKey] = rig.Attachment(56);
        sockets[Rac1ClankPackAssets.SocketKey] = rig.Attachment(Rac1ClankPackAssets.RatchetJoint);
        sockets[Gameplay.Rac1BombProjectileSession.GloveSocketKey] = rig.Attachment(54);
        sockets[Gameplay.Rac1WeaponProfiles.HandSocket(0)] = rig.Attachment(56);
        sockets[Gameplay.Rac1WeaponProfiles.HandSocket(1)] = rig.Attachment(55);
        sockets[Gameplay.Rac1WeaponProfiles.HandSocket(6)] = rig.Attachment(54);
        _armHoldBase = Current;
        Current = Current with { SampledPose = new(Current.SampledPose?.FrameIndex ?? 0, positions) };
        return Current;
    }
}

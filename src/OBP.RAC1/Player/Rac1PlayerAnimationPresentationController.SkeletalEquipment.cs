using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private readonly PlayerEquipmentAnimationPlayer _sourceEquipment = new();
    public int? SourceEquipmentUseSequence => _sourceEquipment.CurrentUseSequence;
    public float SourceEquipmentUseWeight => _sourceEquipment.UseWeight;

    public void ClearSourceEquipment() => _sourceEquipment.Reset();

    public PlayerAnimationPresentation ApplySourceEquipment(PlayerEquipmentAnimationSource source,
        bool moving, bool crouching, bool useStarted, bool triggerHeld)
    {
        RestoreEquipmentBasePose();
        if (_equipmentRig is null) throw new InvalidOperationException("Avatar has no admitted skeletal skin.");
        var basis = EquipmentBaseSample();
        var plan = _sourceEquipment.Step(source, basis.Sequence,
            new(basis.CurrentFrame, basis.NextFrame, basis.Fraction), moving, crouching, useStarted, triggerHeld,
            _equipmentRig.Asset.Clips[basis.Sequence]);
        var baseline = Current.SampledPose?.LocalPositions ?? CurrentClip.LocalFrames[
            CurrentClip.FrameIndexAt(Current.ElapsedAt(_clockSeconds), Current.Loop)];
        var positions = _equipmentRig.SampleLayers(basis, plan.Skeleton, plan.Layers, baseline, plan.FullBody);
        var sockets = _attachments.GetOrCreateValue(positions);
        sockets[PlayerEquipmentPose.HandAttachmentKey] = _equipmentRig.Attachment(plan.HandJoint);
        sockets[Rac1WrenchAttachment.SocketKey] = _equipmentRig.Attachment(56);
        sockets[Gameplay.Rac1BombProjectileSession.GloveSocketKey] = _equipmentRig.Attachment(54);
        _equipmentBase = Current;
        Current = Current with { SampledPose = new(Current.SampledPose?.FrameIndex ?? 0, positions) };
        return Current;
    }
}

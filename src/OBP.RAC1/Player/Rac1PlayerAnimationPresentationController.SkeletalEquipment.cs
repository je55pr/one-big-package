using OBP.Runtime.Player;
using System.Runtime.CompilerServices;

namespace OBP.RAC1.Player;

public sealed partial class Rac1PlayerAnimationPresentationController
{
    private readonly PlayerEquipmentAnimationPlayer _sourceEquipment = new();
    private readonly PlayerSkeletalTransition _sourceTransition = new();
    private sealed record BodySkeletalPose(PlayerSkeletalPose Pose, object? BlendKey = null, float Progress = 1);
    private readonly ConditionalWeakTable<double[], BodySkeletalPose> _bodySkeletalPoses = new();
    public int? SourceEquipmentUseSequence => _sourceEquipment.CurrentUseSequence;
    public float SourceEquipmentUseWeight => _sourceEquipment.UseWeight;
    public float SourceEquipmentBlendFraction { get; private set; } = 1;
    public bool SourceEquipmentIsBlending { get; private set; }

    public void ClearSourceEquipment()
    {
        _sourceEquipment.Reset(); _sourceTransition.Reset();
        SourceEquipmentBlendFraction = 1; SourceEquipmentIsBlending = false;
    }

    private void RegisterBaseSkeletalFrames()
    {
        if (_equipmentRig is null) return;
        foreach (var pair in _equipmentRig.Asset.Clips)
        {
            var vertices = LocomotionClip(pair.Key).LocalFrames;
            for (int frame = 0; frame < vertices.Count; frame++)
                _bodySkeletalPoses.Add(vertices[frame], new(new(pair.Value.Frames[frame].Joints)));
        }
    }

    private void RecordBaseSkeletalPose(double[] vertices, Rac1AnimationFrameClock.Sample sample, double[]? blendSource)
    {
        if (_equipmentRig is null) return;
        PlayerSkeletalPose? source = blendSource is not null && _bodySkeletalPoses.TryGetValue(blendSource, out var known)
            ? known.Pose : null;
        _bodySkeletalPoses.Add(vertices, new(_equipmentRig.BasePose(sample, source),
            sample.IsBlending ? blendSource : null, sample.IsBlending ? sample.Fraction : 1));
    }

    public PlayerAnimationPresentation ApplySourceEquipment(PlayerEquipmentAnimationSource source,
        bool moving, bool crouching, bool useStarted, bool triggerHeld)
    {
        RestoreEquipmentBasePose();
        if (_equipmentRig is null) throw new InvalidOperationException("Avatar has no admitted skeletal skin.");
        var basis = EquipmentBaseSample();
        BodySkeletalPose? body = null;
        if (Current.SampledPose is { } sampled) _bodySkeletalPoses.TryGetValue(sampled.LocalPositions, out body);
        SourceEquipmentIsBlending = body?.BlendKey is not null;
        SourceEquipmentBlendFraction = body?.Progress ?? 1;
        // During a native transition Fraction is progress from a saved pose,
        // not interpolation time inside the target clip. Keep its target frozen.
        var target = basis.IsBlending ? basis with { CurrentFrame = basis.NextFrame, Fraction = 0 } : basis;
        var plan = _sourceEquipment.Step(source, target.Sequence,
            new(target.CurrentFrame, target.NextFrame, target.Fraction), moving, crouching, useStarted, triggerHeld,
            _equipmentRig.Asset.Clips[target.Sequence]);
        var initial = body?.BlendKey is double[] key && _bodySkeletalPoses.TryGetValue(key, out var start) ? start.Pose : null;
        var positions = _equipmentRig.SampleLayers(target, plan.Skeleton, plan.Layers, _sourceTransition,
            body?.BlendKey, body?.Progress ?? 1, initial);
        var sockets = _attachments.GetOrCreateValue(positions);
        sockets[PlayerEquipmentPose.HandAttachmentKey] = _equipmentRig.Attachment(plan.HandJoint);
        sockets[Rac1WrenchAttachment.SocketKey] = _equipmentRig.Attachment(56);
        sockets[Gameplay.Rac1BombProjectileSession.GloveSocketKey] = _equipmentRig.Attachment(54);
        _equipmentBase = Current;
        Current = Current with { SampledPose = new(Current.SampledPose?.FrameIndex ?? 0, positions) };
        return Current;
    }
}

using OBP.Core;
using System.Numerics;
using OBP.RAC1.Animation;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>R&C1 source conversion over the shared local-channel skeletal sampler.</summary>
public sealed class Rac1RatchetEquipmentRig
{
    private readonly PlayerSkeletalSampler _sampler;
    private readonly PlayerSkeletalSampler _referenceSampler;
    private readonly IReadOnlyDictionary<int, PlayerSkeletalClip> _armClips;
    public PlayerSkeletalAsset Asset { get; }
    public IReadOnlyList<int> LayerJoints { get; }

    public Rac1RatchetEquipmentRig(Rac1RatchetAvatar.Asset asset, IReadOnlyList<int> layerJoints)
    {
        LayerJoints = layerJoints;
        Asset = Convert(asset);
        _sampler = new(Asset.Skeleton, Asset.Skin);
        _referenceSampler = new(Asset.Skeleton);
        _armClips = Asset.Clips.ToDictionary(p => p.Key, p => Subset(p.Value, layerJoints));
    }

    private static PlayerSkeletalAsset Convert(Rac1RatchetAvatar.Asset asset)
    {
        int count = asset.Skeleton.Count;
        float scale = asset.Mesh.Scale / 1024;
        var joints = new PlayerSkeletonJoint[count];
        var bindRotation = new Matrix4x4[count];
        var pivot = new Vector3[count];
        for (int j = 0; j < count; j++)
        {
            var source = asset.Skeleton[j]; var a = source.NativeAffine;
            var inverse = new Matrix4x4((float)a[0], (float)a[1], (float)a[2], 0,
                (float)a[4], (float)a[5], (float)a[6], 0, (float)a[8], (float)a[9], (float)a[10], 0, 0, 0, 0, 1);
            if (!Matrix4x4.Invert(inverse, out bindRotation[j])) throw new InvalidDataException("Singular Ratchet bind.");
            var common = new Vector3(source.CommonX, source.CommonY, source.CommonZ) * scale;
            int parent = j == 0 ? -1 : source.ParentRecordIndex;
            pivot[j] = parent < 0 ? common : pivot[parent] + Vector3.TransformNormal(common, bindRotation[parent]);
            inverse.Translation = -Vector3.TransformNormal(pivot[j], inverse);
            joints[j] = new(parent, common, inverse);
        }
        var skeleton = new PlayerSkeleton(joints, asset.AttachmentChains.Select((chain, id) => (chain, id))
            .ToDictionary(p => p.id, p => new PlayerSkeletonAttachment(p.chain.Target, p.chain.LayerJoints)));
        var clips = asset.AnimationClips.ToDictionary(clip => clip.SequenceId, clip =>
        {
            if (clip.SourceFrames is null) throw new InvalidDataException("R&C1 skeletal source frames are absent.");
            var frames = clip.SourceFrames.Select((frame, i) => new PlayerSkeletalFrame(Channels(frame),
                clip.FrameDurationsSeconds[i], clip.FrameTimelinePositions[i])).ToArray();
            return new PlayerSkeletalClip(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, 0, clip.SequenceId,
                Enumerable.Range(0, count).ToArray(), frames);
        });
        return new(new(OBP.Core.ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "ratchet", "rac1:ratchet:moby-class-0"),
            skeleton, new(asset.Mesh.Positions, asset.Mesh.VertexJoints, asset.Mesh.VertexWeights), clips);

        PlayerJointChannels[] Channels(Rac1MobyAnimation.Frame frame)
        {
            var result = joints.Select((joint, j) =>
            {
                var q = frame.JointRotations[j];
                return new PlayerJointChannels(new(q.Xf, q.Yf, q.Zf, q.Wf), Vector3.One, joint.Translation);
            }).ToArray();
            foreach (ulong word in frame.Thing1)
            {
                int j = (int)(word >> 48) & 0x7fff;
                result[j] = result[j] with { Scale = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) / 4096 };
            }
            foreach (ulong word in frame.Thing2)
            {
                int j = (int)(word >> 48);
                result[j] = result[j] with { Translation = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) * scale };
            }
            return result;
        }
    }

    private static PlayerSkeletalClip Subset(PlayerSkeletalClip source, IReadOnlyList<int> joints) => source with
    {
        JointIndices = joints,
        Frames = source.Frames.Select(f => f with { Joints = joints.Select(j => f.Joints[j]).ToArray() }).ToArray(),
    };

    public double[] Sample(Rac1AnimationFrameClock.Sample basis, Rac1AnimationFrameClock.Sample layer,
        float amount, bool fullBody, double[] baseline, bool stabilizeShoulders = false)
    {
        _sampler.Reset(Asset.Clips[basis.Sequence], Sample(basis));
        if (stabilizeShoulders && !fullBody)
        {
            // The authored held pose and ordinary running pose disagree about
            // their shared torso parent (joint 6). Replacing only the arm-local
            // channels lets the running torso pitch/yaw pull the held gun off axis.
            // Keep both arm roots' reference world heading, leaving the legs and
            // torso on the ordinary locomotion clock. This is a host retarget,
            // not an invented R&C1 source moving animation.
            _sampler.EvaluateJointMatrices();
            _referenceSampler.Reset(Asset.Clips[layer.Sequence], Sample(layer));
            _referenceSampler.EvaluateJointMatrices();
        }
        _sampler.Apply((fullBody ? Asset.Clips : _armClips)[layer.Sequence], Sample(layer), amount);
        if (stabilizeShoulders && !fullBody)
        {
            _sampler.AlignJointHeading(52, _referenceSampler.JointMatrices[52], amount);
            _sampler.AlignJointHeading(72, _referenceSampler.JointMatrices[72], amount);
        }
        return _sampler.Skin(baseline, fullBody ? null : LayerJoints.ToArray());
    }

    public double[] SampleLayers(Rac1AnimationFrameClock.Sample basis, PlayerSkeleton sourceSkeleton,
        IReadOnlyList<PlayerSkeletalLayer> layers, PlayerSkeletalTransition transition,
        object? blendKey, float blendFraction, PlayerSkeletalPose? initialSource)
    {
        _sampler.RequireCompatible(sourceSkeleton);
        _sampler.Reset(Asset.Clips[basis.Sequence], Sample(basis));
        foreach (var layer in layers) _sampler.Apply(layer.Clip, layer.Sample, layer.Weight);
        transition.Resolve(_sampler, blendKey, blendFraction, initialSource);
        // The entire skin and the hand sockets consume the same blended pose.
        // Leaving the torso on baked vertices would give the arms different ancestors.
        return _sampler.Skin();
    }

    public PlayerSkeletalPose BasePose(Rac1AnimationFrameClock.Sample basis, PlayerSkeletalPose? source = null)
    {
        var target = basis.IsBlending ? basis with { CurrentFrame = basis.NextFrame, Fraction = 0 } : basis;
        _sampler.Reset(Asset.Clips[target.Sequence], Sample(target));
        if (basis.IsBlending && source is not null) _sampler.BlendFrom(source, basis.Fraction);
        return _sampler.CapturePose();
    }

    private static PlayerSkeletalSample Sample(Rac1AnimationFrameClock.Sample sample) => new(sample.CurrentFrame, sample.NextFrame, sample.Fraction);
    public PlayerAvatarAttachmentPose Attachment(int joint) => _sampler.Attachment(joint);
}

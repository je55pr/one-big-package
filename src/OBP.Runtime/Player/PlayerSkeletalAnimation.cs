using OBP.Core;
using System.Text.Json.Serialization;
using System.Numerics;

namespace OBP.Runtime.Player;

/// <summary>Model-local skeletal data. No PS2 layout, native state ids or engine axes are inferred here.</summary>
public sealed record PlayerSkeletonJoint(int Parent, Vector3 Translation, Matrix4x4 InverseBind);
public sealed record PlayerSkeletonAttachment(int Joint, IReadOnlyList<int> LayerJoints);
public sealed record PlayerSkeleton(IReadOnlyList<PlayerSkeletonJoint> Joints,
    IReadOnlyDictionary<int, PlayerSkeletonAttachment> Attachments);

/// <summary>Absent scale/translation channels preserve the underlying pose, as needed by arm-only clips.</summary>
public readonly record struct PlayerJointChannels(Quaternion Rotation, Vector3? Scale = null, Vector3? Translation = null);
/// <summary>A complete, resolved local-joint pose, independent of a mesh or engine transform.</summary>
public sealed record PlayerSkeletalPose(IReadOnlyList<PlayerJointChannels> Joints);
public sealed record PlayerSkeletalFrame(IReadOnlyList<PlayerJointChannels> Joints, double DurationSeconds, double TimelinePosition);
public readonly record struct PlayerSkeletalSample(int Frame, int NextFrame, float Fraction);
public sealed record PlayerSkeletalClip([property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame SourceGame, string BuildId, int CarrierId, int SequenceId,
    IReadOnlyList<int> JointIndices, IReadOnlyList<PlayerSkeletalFrame> Frames)
{
    public double DurationSeconds => Frames.Sum(frame => frame.DurationSeconds);

    public PlayerSkeletalSample SampleAt(double seconds, bool loop = true)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Frames.Count == 0 || Frames.Any(f => !double.IsFinite(f.DurationSeconds) || f.DurationSeconds <= 0))
            throw new InvalidDataException("Skeletal clip has invalid frame timing.");
        double total = DurationSeconds;
        double phase = loop ? seconds % total : Math.Min(seconds, total);
        for (int i = 0; i < Frames.Count; i++)
        {
            double duration = Frames[i].DurationSeconds;
            if (phase < duration || i == Frames.Count - 1)
                return new(i, loop ? (i + 1) % Frames.Count : Math.Min(i + 1, Frames.Count - 1),
                    (float)Math.Clamp(phase / duration, 0, 1));
            phase -= duration;
        }
        throw new InvalidOperationException("Empty skeletal clip.");
    }
}

public sealed record PlayerSkinnedMesh(double[] Positions, int[] Joints, float[] Weights);
public sealed record PlayerSkeletalAsset(PlayerAvatarIdentity Identity, PlayerSkeleton Skeleton,
    PlayerSkinnedMesh? Skin, IReadOnlyDictionary<int, PlayerSkeletalClip> Clips);
public sealed record PlayerSkeletalLayer(PlayerSkeletalClip Clip, PlayerSkeletalSample Sample, float Weight);

/// <summary>Prepared source equipment channels; the source game supplies all selection and attachment facts.</summary>
public sealed record PlayerEquipmentAnimationSource(PlayerSkeletalAsset Avatar,
    IReadOnlyList<IReadOnlyDictionary<int, PlayerSkeletalClip>> Arms,
    int ArmCount, int HandJoint, int StandingUseSequence, int MovingUseSequence, int CrouchingUseSequence,
    bool SustainUse, int UseBlendTicks)
{
    public required PlayerEquipmentBlendSteps BlendSteps { get; init; }
    public required int ArmBankUpperExclusive { get; init; }
    public PlayerSkeleton Skeleton => Avatar.Skeleton;
    public IReadOnlyDictionary<int, PlayerSkeletalClip> Body => Avatar.Clips;
}
public sealed record PlayerEquipmentBlendSteps(float CarryIn, float UseOut, float CompletedUseOut);
public sealed record PlayerEquipmentPose(PlayerSkeleton Skeleton, IReadOnlyList<PlayerSkeletalLayer> Layers,
    int HandJoint, bool FullBody)
{
    public const string HandAttachmentKey = "equipment:source-hand";
}

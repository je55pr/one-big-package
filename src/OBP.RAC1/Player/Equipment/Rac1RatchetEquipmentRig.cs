using OBP.Core;
using System.Numerics;
using OBP.RAC1.Animation;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>R&C1 source conversion over the shared local-channel skeletal sampler.</summary>
public sealed class Rac1RatchetEquipmentRig
{
    private readonly PlayerSkeletalSampler _sampler;
    private readonly IReadOnlyDictionary<int, PlayerSkeletalClip> _armClips;
    public PlayerSkeletalAsset Asset { get; }
    public IReadOnlyList<int> LayerJoints { get; }
    /// <summary>Retail procedural joint table; absent for rigs built without the original overlay.</summary>
    public Rac1HeroEaseTable? HeroEases { get; private set; }

    /// <summary>Why the procedural joint layer is unavailable, or null when it is admitted.</summary>
    public string? HeroEaseStatus { get; private set; } = "Procedural joint layer unavailable: no ease table was admitted.";

    /// <summary>
    /// Admit the retail ease table once, after the rig exists, so every attachment it names is resolved
    /// against the skeleton here instead of failing on each update. A rejected table leaves the avatar
    /// without the procedural layer and records the reason in <see cref="HeroEaseStatus"/>.
    /// </summary>
    public void AdmitHeroEases(Rac1HeroEaseTable table)
    {
        foreach (var record in table.Body)
            if (!Asset.Skeleton.Attachments.ContainsKey(record.AttachmentId))
            {
                RejectHeroEases($"ease attachment {record.AttachmentId} is absent from the skeleton");
                return;
            }
        HeroEases = table;
        HeroEaseStatus = null;
    }

    public void RejectHeroEases(string reason)
    {
        HeroEases = null;
        HeroEaseStatus = $"Procedural joint layer unavailable: {reason}.";
    }

    private readonly IReadOnlyList<IReadOnlyDictionary<int, PlayerSkeletalClip>>? _holdClips;
    private readonly float[] _holdWeights = new float[Rac1RatchetArmHold.SlotCount];
    private readonly int[] _armJoints;
    // Arm channels last tick, the pose a body transition started from, and this tick's result.
    private readonly Dictionary<int, PlayerJointChannels> _holdLast = new(), _holdSource = new(), _holdPending = new();
    private bool _holdBlending, _holdPrevBlending;
    private float _holdFraction;
    private int _holdPrevSequence = -1;

    public Rac1RatchetEquipmentRig(Rac1RatchetAvatar.Asset asset, IReadOnlyList<int> layerJoints,
        Rac1RatchetArmHoldSource? armHold = null)
    {
        LayerJoints = layerJoints;
        Asset = Convert(asset);
        _sampler = new(Asset.Skeleton, Asset.Skin);
        _armClips = Asset.Clips.ToDictionary(p => p.Key, p => Subset(p.Value, layerJoints));
        _armJoints = armHold is null ? [] : armHold.BankJoints.SelectMany(bank => bank).Distinct().ToArray();
        if (armHold is not null)
            _holdClips = Rac1RatchetArmHold.BuildClips(armHold, Asset, asset.Mesh.Scale / 1024);
    }

    /// <summary>True when the original arm hold classes were admitted for this rig.</summary>
    public bool HasArmHold => _holdClips is not null;

    /// <summary>Why the held-gun arm hold is unavailable, or null when it is admitted.</summary>
    public string? ArmHoldStatus { get; init; }

    internal IReadOnlyList<IReadOnlyDictionary<int, PlayerSkeletalClip>>? ArmHoldClips => _holdClips;

    /// <summary>Weights of the right (slot 0) and left (slot 1) arm hold layers for the next pose evaluation.</summary>
    public void SetArmHoldWeights(float right, float left)
    {
        if (!float.IsFinite(right) || !float.IsFinite(left)) throw new ArgumentOutOfRangeException(nameof(right));
        _holdWeights[0] = Math.Clamp(right, 0, 1);
        _holdWeights[1] = Math.Clamp(left, 0, 1);
    }

    /// <summary>
    /// Start one update of the arm hold. The retail slot blends between the previous and the current
    /// body sequence together with the body (<c>0x20dd20</c>), so a body transition must carry the arms
    /// from where they were to the new pose instead of swapping hold sequences in a single update.
    /// Call once per update, before any pose is evaluated.
    /// </summary>
    public void BeginArmHoldTick(Rac1AnimationFrameClock.Sample basis)
    {
        if (_holdClips is null) return;
        if (_holdPending.Count > 0)
        {
            _holdLast.Clear();
            foreach (var pair in _holdPending) _holdLast[pair.Key] = pair.Value;
            _holdPending.Clear();
        }
        bool blending = basis.IsBlending;
        if (blending && (!_holdPrevBlending || basis.Sequence != _holdPrevSequence))
        {
            _holdSource.Clear();
            foreach (var pair in _holdLast) _holdSource[pair.Key] = pair.Value;
        }
        _holdBlending = blending && _holdSource.Count > 0;
        _holdFraction = basis.Fraction;
        _holdPrevBlending = blending;
        _holdPrevSequence = basis.Sequence;
        if (!blending) _holdSource.Clear();
    }

    private static PlayerJointChannels BlendChannels(PlayerJointChannels from, PlayerJointChannels to, float amount) => new(
        Quaternion.Normalize(Quaternion.Lerp(from.Rotation, to.Rotation, amount)),
        Vector3.Lerp(from.Scale!.Value, to.Scale!.Value, amount),
        Vector3.Lerp(from.Translation!.Value, to.Translation!.Value, amount));

    // Layer the arm hold over a freshly reset body pose; returns the arm joints it owns this update.
    // Only the pass that produces the presented pose records the arm channels the next body transition
    // starts from; the ease pass re-derives the same pose to measure its delta and must not overwrite it.
    private List<int> ApplyArmHold(int sequence, PlayerSkeletalSample sample, bool record)
    {
        var changed = new List<int>();
        if (_holdClips is null) return changed;
        bool active = _holdWeights[0] > 0 || _holdWeights[1] > 0;
        if (!active && !_holdBlending)
        {
            _holdLast.Clear();
            return changed;
        }
        if (sequence < Rac1RatchetArmHold.SequenceLimit)
            for (int slot = 0; slot < _holdClips.Count; slot++)
                if (_holdWeights[slot] > 0 && _holdClips[slot].TryGetValue(sequence, out var clip))
                    _sampler.Apply(clip, sample, _holdWeights[slot]);
        foreach (int joint in _armJoints)
        {
            var target = _sampler.GetLocal(joint);
            var resolved = _holdBlending && _holdSource.TryGetValue(joint, out var source)
                ? BlendChannels(source, target, _holdFraction) : target;
            if (_holdBlending) _sampler.SetLocal(joint, resolved);
            if (record) _holdPending[joint] = resolved;
        }
        changed.AddRange(_armJoints);
        return changed;
    }

    /// <summary>
    /// The body pose with the arm hold layers applied, skinned over <paramref name="baseline"/>. Returns
    /// null when the hold does not apply. Hand sockets read the same final joints.
    /// </summary>
    public double[]? SampleArmHold(Rac1AnimationFrameClock.Sample basis, double[] baseline,
        PlayerSkeletalPose? body = null)
    {
        if (_holdClips is null) return null;
        var target = basis.IsBlending ? basis with { CurrentFrame = basis.NextFrame, Fraction = 0 } : basis;
        // The resolved body pose carries any transition blend, so the arms stay on the blended torso.
        if (body is not null) _sampler.LoadPose(body);
        else if (Asset.Clips.TryGetValue(basis.Sequence, out var clip)) _sampler.Reset(clip, Sample(target));
        else return null;
        var changed = ApplyArmHold(target.Sequence, Sample(target), record: true);
        return changed.Count == 0 ? null : _sampler.Skin(baseline, changed);
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

    /// <summary>
    /// Equipment use pose over the body. With <paramref name="fullBodyBlend"/> the arm layer keeps its
    /// own weight while the whole-body pose fades in or out on top of it, so switching between a
    /// standing use pose and a moving one never replaces the legs and torso in a single update.
    /// </summary>
    public double[] Sample(Rac1AnimationFrameClock.Sample basis, Rac1AnimationFrameClock.Sample layer,
        float amount, bool fullBody, double[] baseline, float? fullBodyBlend = null)
    {
        _sampler.Reset(Asset.Clips[basis.Sequence], Sample(basis));
        ApplyArmHold(basis.Sequence, Sample(basis), record: true);
        if (fullBodyBlend is { } blend)
        {
            blend = Math.Clamp(blend, 0, 1);
            // A complete whole-body pose is exactly the old all-or-nothing result.
            if (blend >= 1) _sampler.Apply(Asset.Clips[layer.Sequence], Sample(layer), amount);
            else
            {
                _sampler.Apply(_armClips[layer.Sequence], Sample(layer), amount);
                if (blend > 0) _sampler.Apply(Asset.Clips[layer.Sequence], Sample(layer), amount * blend);
            }
            return _sampler.Skin(baseline, blend > 0 ? null : LayerJoints.ToArray());
        }
        _sampler.Apply((fullBody ? Asset.Clips : _armClips)[layer.Sequence], Sample(layer), amount);
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

    /// <summary>
    /// Procedural joint rotations (the native hero eases) layered over the resolved
    /// body pose. The offsets are composed onto the animation basis and the
    /// resulting vertex and joint change is added to <paramref name="current"/>, so
    /// blends already resolved into the current vertices are preserved. Joint
    /// deltas let hand sockets follow the same final transforms as the skin.
    /// </summary>
    public (double[] Positions, IReadOnlyDictionary<int, Matrix4x4> JointDeltas) ApplyJointOffsets(
        Rac1AnimationFrameClock.Sample basis, double[] current,
        IReadOnlyList<Rac1RatchetJointEases.Offset> offsets, IReadOnlyCollection<int> deltaJoints,
        PlayerSkeletalPose? body = null)
    {
        PlayerSkeletalClip? clip = null;
        if (offsets.Count == 0 || body is null && !Asset.Clips.TryGetValue(basis.Sequence, out clip))
            return (current, new Dictionary<int, Matrix4x4>());
        var target = basis.IsBlending ? basis with { CurrentFrame = basis.NextFrame, Fraction = 0 } : basis;
        var changed = new List<int>(offsets.Count);
        foreach (var offset in offsets)
            changed.Add(Asset.Skeleton.Attachments.TryGetValue(offset.AttachmentId, out var attachment)
                ? attachment.Joint
                : throw new InvalidDataException($"Ratchet ease attachment {offset.AttachmentId} is absent from the skeleton."));
        if (body is not null) _sampler.LoadPose(body); else _sampler.Reset(clip!, Sample(target));
        // The arm hold is part of the pose the eases act on, so the hands and weapon sockets
        // rotate with the shoulders around the same final joints.
        changed.AddRange(ApplyArmHold(target.Sequence, Sample(target), record: false));
        var before = _sampler.Skin(null, changed.Distinct().ToArray());
        var worldBefore = _sampler.JointMatrices.ToArray();
        for (int i = 0; i < offsets.Count; i++) _sampler.ComposeLocalRotation(changed[i], offsets[i].Rotation);
        var after = _sampler.Skin(null, changed.Distinct().ToArray());
        var result = (double[])current.Clone();
        for (int i = 0; i < result.Length; i++) result[i] += after[i] - before[i];
        var deltas = new Dictionary<int, Matrix4x4>();
        foreach (int joint in deltaJoints)
            if ((uint)joint < worldBefore.Length && Matrix4x4.Invert(worldBefore[joint], out var inverse))
                deltas[joint] = inverse * _sampler.JointMatrices[joint];
        return (result, deltas);
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

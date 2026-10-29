using System.Numerics;

namespace OBP.Runtime.Player;

/// <summary>Reusable local-channel blending, hierarchy evaluation and skinning for any admitted skin.</summary>
public sealed class PlayerSkeletalSampler
{
    private readonly PlayerSkeleton _skeleton;
    private readonly PlayerSkinnedMesh _skin;
    private readonly PlayerJointChannels[] _local;
    private readonly Matrix4x4[] _world;
    private readonly int[] _order;
    private readonly Vector3[] _bindLocal;
    private readonly bool[] _affected;
    public IReadOnlyList<Matrix4x4> JointMatrices => _world;

    public PlayerSkeletalSampler(PlayerSkeleton skeleton, PlayerSkinnedMesh? skin = null)
    {
        _skeleton = skeleton; _skin = skin ?? new([], [], []);
        skin = _skin;
        int count = skeleton.Joints.Count;
        if (count is < 1 or > 1024 || skin.Positions.Length % 3 != 0 ||
            skin.Joints.Length != skin.Positions.Length || skin.Weights.Length != skin.Joints.Length ||
            skin.Positions.Any(p => !double.IsFinite(p)) || skin.Weights.Any(w => !float.IsFinite(w) || w < 0))
            throw new InvalidDataException("Invalid skeletal skin streams.");
        _local = new PlayerJointChannels[count]; _world = new Matrix4x4[count]; _affected = new bool[count];
        var order = new List<int>(); var visited = new byte[count];
        void Visit(int j)
        {
            if (visited[j] == 2) return;
            if (visited[j] == 1) throw new InvalidDataException("Skeleton contains a parent cycle.");
            visited[j] = 1;
            var joint = skeleton.Joints[j];
            if (!Finite(joint.Translation) || !Finite(joint.InverseBind) || Math.Abs(joint.InverseBind.GetDeterminant()) < 1e-10)
                throw new InvalidDataException("Invalid skeleton bind transform.");
            if (joint.Parent != -1)
            {
                if ((uint)joint.Parent >= count) throw new InvalidDataException("Skeleton parent is outside its rig.");
                Visit(joint.Parent);
            }
            visited[j] = 2; order.Add(j);
        }
        for (int j = 0; j < count; j++) Visit(j);
        _order = order.ToArray();
        _bindLocal = new Vector3[skin.Joints.Length];
        for (int v = 0; v < skin.Positions.Length / 3; v++)
        {
            var p = new Vector3((float)skin.Positions[v * 3], (float)skin.Positions[v * 3 + 1], (float)skin.Positions[v * 3 + 2]);
            if (!Finite(p)) throw new InvalidDataException("Skin position exceeds its numeric range.");
            for (int influence = 0; influence < 3; influence++)
            {
                int at = v * 3 + influence;
                if (skin.Weights[at] == 0) continue;
                int joint = skin.Joints[at];
                if ((uint)joint >= count) throw new InvalidDataException("Skin influence is outside its skeleton.");
                _bindLocal[at] = Vector3.Transform(p, skeleton.Joints[joint].InverseBind);
            }
        }
    }

    public void Reset(PlayerSkeletalClip clip, PlayerSkeletalSample sample)
    {
        for (int j = 0; j < _local.Length; j++)
            _local[j] = new(Quaternion.Identity, Vector3.One, _skeleton.Joints[j].Translation);
        Apply(clip, sample, 1);
    }

    public PlayerSkeletalPose CapturePose() => new((PlayerJointChannels[])_local.Clone());

    /// <summary>Blend resolved local channels before hierarchy evaluation, skinning and socket extraction.</summary>
    public void BlendFrom(PlayerSkeletalPose source, float fraction)
    {
        if (!float.IsFinite(fraction) || fraction is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(fraction));
        if (source.Joints.Count != _local.Length) throw new InvalidDataException("Pose does not belong to this skeleton.");
        for (int j = 0; j < _local.Length; j++)
        {
            var from = source.Joints[j]; var to = _local[j];
            if (!Finite(from.Rotation) || from.Scale is not { } scale || !Finite(scale) ||
                from.Translation is not { } translation || !Finite(translation))
                throw new InvalidDataException("Pose contains unresolved or invalid channels.");
            _local[j] = new(Quaternion.Lerp(from.Rotation, to.Rotation, fraction),
                Vector3.Lerp(scale, to.Scale!.Value, fraction), Vector3.Lerp(translation, to.Translation!.Value, fraction));
        }
    }

    public void Apply(PlayerSkeletalClip clip, PlayerSkeletalSample sample, float weight)
    {
        if (!float.IsFinite(weight) || weight is < 0 or > 1 || !float.IsFinite(sample.Fraction) || sample.Fraction is < 0 or > 1 ||
            (uint)sample.Frame >= clip.Frames.Count || (uint)sample.NextFrame >= clip.Frames.Count)
            throw new ArgumentException($"Invalid skeletal layer sample: source={clip.SourceGame}, sequence={clip.SequenceId}, " +
                $"frames={clip.Frames.Count}, frame={sample.Frame}, next={sample.NextFrame}, " +
                $"fraction={sample.Fraction:R}, weight={weight:R}.");
        var a = clip.Frames[sample.Frame].Joints; var b = clip.Frames[sample.NextFrame].Joints;
        if (a.Count != clip.JointIndices.Count || b.Count != a.Count || clip.JointIndices.Distinct().Count() != a.Count)
            throw new InvalidDataException("Skeletal channel map does not match its frames.");
        for (int i = 0; i < a.Count; i++)
        {
            int joint = clip.JointIndices[i];
            if ((uint)joint >= _local.Length) throw new InvalidDataException("Layer references a missing joint.");
            var basis = _local[joint];
            if (!Finite(a[i].Rotation) || !Finite(b[i].Rotation)) throw new InvalidDataException("Invalid skeletal rotation.");
            Quaternion q = Quaternion.Lerp(a[i].Rotation, b[i].Rotation, sample.Fraction);
            var scale = Vector3.Lerp(a[i].Scale ?? basis.Scale!.Value, b[i].Scale ?? basis.Scale!.Value, sample.Fraction);
            var translation = Vector3.Lerp(a[i].Translation ?? basis.Translation!.Value,
                b[i].Translation ?? basis.Translation!.Value, sample.Fraction);
            if (!Finite(scale) || !Finite(translation)) throw new InvalidDataException("Invalid skeletal vector channel.");
            _local[joint] = new(Quaternion.Lerp(basis.Rotation, q, weight),
                Vector3.Lerp(basis.Scale!.Value, scale, weight), Vector3.Lerp(basis.Translation!.Value, translation, weight));
        }
    }

    public double[] Skin(double[]? baseline = null, IReadOnlyCollection<int>? changedJoints = null)
    {
        if (baseline is not null && baseline.Length != _skin.Positions.Length)
            throw new ArgumentException("Baseline does not belong to this skin.", nameof(baseline));
        Array.Clear(_affected);
        foreach (int j in changedJoints ?? Enumerable.Range(0, _local.Length).ToArray())
        {
            if ((uint)j >= _local.Length) throw new ArgumentException("Changed joint is outside the skeleton.");
            _affected[j] = true;
        }
        foreach (int j in _order)
        {
            var channels = _local[j];
            var local = Matrix4x4.CreateScale(channels.Scale!.Value) *
                Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(channels.Rotation));
            local.Translation = channels.Translation!.Value;
            int parent = _skeleton.Joints[j].Parent;
            _world[j] = parent < 0 ? local : local * _world[parent];
            if (parent >= 0) _affected[j] |= _affected[parent];
        }
        var result = (double[])(baseline ?? _skin.Positions).Clone();
        for (int v = 0; v < result.Length / 3; v++)
        {
            bool affected = false; var p = Vector3.Zero; float total = 0;
            for (int i = 0; i < 3; i++)
            {
                int at = v * 3 + i; float weight = _skin.Weights[at];
                if (weight <= 0) continue;
                int joint = _skin.Joints[at];
                affected |= _affected[joint];
                p += Vector3.Transform(_bindLocal[at], _world[joint]) * weight; total += weight;
            }
            if (!affected || total <= 0) continue;
            p /= total;
            result[v * 3] = p.X; result[v * 3 + 1] = p.Y; result[v * 3 + 2] = p.Z;
        }
        return result;
    }

    public PlayerAvatarAttachmentPose Attachment(int joint)
    {
        if ((uint)joint >= _world.Length) throw new ArgumentOutOfRangeException(nameof(joint));
        var m = _world[joint];
        return new(new(m.M41, m.M42, m.M43), new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33));
    }

    public void RequireCompatible(PlayerSkeleton source)
    {
        if (source.Joints.Count != _skeleton.Joints.Count || source.Joints.Where((j, i) => j.Parent != _skeleton.Joints[i].Parent ||
            Vector3.DistanceSquared(j.Translation, _skeleton.Joints[i].Translation) > 1e-10f).Any() ||
            source.Attachments.Count != _skeleton.Attachments.Count || source.Attachments.Any(p =>
                !_skeleton.Attachments.TryGetValue(p.Key, out var target) || target.Joint != p.Value.Joint ||
                !target.LayerJoints.SequenceEqual(p.Value.LayerJoints)))
            throw new InvalidDataException("Source animation rig requires an explicit joint mapping.");
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Quaternion q) => float.IsFinite(q.LengthSquared()) && q.LengthSquared() > 1e-8f;
    private static bool Finite(Matrix4x4 m) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 }.All(float.IsFinite);
}

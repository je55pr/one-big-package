using System.Numerics;
using OBP.RAC1.Animation;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Local quaternion/scale/translation layers composed before hierarchy evaluation
/// and skinning. The mask is the class+0x1c animation list, not a vertex torso cut.
/// Bind work is cached; sampling allocates no per-vertex matrices or collections.
/// </summary>
public sealed class Rac1RatchetEquipmentRig
{
    private readonly record struct Channels(Quaternion Rotation, Vector3 Scale, Vector3 Translation);
    private readonly Dictionary<int, Channels[][]> _clips;
    private readonly int[] _parents, _bones;
    private readonly float[] _weights;
    private readonly Vector3[] _bindLocal;
    private readonly bool[] _mask, _affectedVertices;
    private readonly Matrix4x4[] _world;
    public IReadOnlyList<int> LayerJoints { get; }

    public Rac1RatchetEquipmentRig(Rac1RatchetAvatar.Asset asset, IReadOnlyList<int> layerJoints)
    {
        LayerJoints = layerJoints;
        int count = asset.Skeleton.Count;
        _mask = new bool[count];
        foreach (int joint in layerJoints) _mask[joint] = true;
        _parents = asset.Skeleton.Select(joint => joint.ParentRecordIndex).ToArray();
        _bones = asset.Mesh.VertexJoints;
        _weights = asset.Mesh.VertexWeights.Select(weight => (float)weight).ToArray();
        _world = new Matrix4x4[count];
        var inverseBind = new Matrix4x4[count];
        var bindRotation = new Matrix4x4[count];
        var pivot = new Vector3[count];
        float scale = asset.Mesh.Scale / 1024;
        for (int joint = 0; joint < count; joint++)
        {
            var source = asset.Skeleton[joint]; var a = source.NativeAffine;
            inverseBind[joint] = new((float)a[0], (float)a[1], (float)a[2], 0,
                (float)a[4], (float)a[5], (float)a[6], 0, (float)a[8], (float)a[9], (float)a[10], 0, 0, 0, 0, 1);
            if (!Matrix4x4.Invert(inverseBind[joint], out bindRotation[joint])) throw new InvalidDataException("Singular Ratchet bind.");
            var common = new Vector3(source.CommonX, source.CommonY, source.CommonZ) * scale;
            pivot[joint] = joint == 0 ? common : pivot[_parents[joint]] + Vector3.TransformNormal(common, bindRotation[_parents[joint]]);
        }
        _bindLocal = new Vector3[_bones.Length];
        _affectedVertices = new bool[_bones.Length / 3];
        var affectedBones = (bool[])_mask.Clone();
        for (int joint = 1; joint < count; joint++) affectedBones[joint] |= affectedBones[_parents[joint]];
        for (int vertex = 0; vertex < _affectedVertices.Length; vertex++)
        {
            var rest = new Vector3((float)asset.Mesh.Positions[vertex * 3], (float)asset.Mesh.Positions[vertex * 3 + 1], (float)asset.Mesh.Positions[vertex * 3 + 2]);
            for (int influence = 0; influence < 3; influence++)
            {
                int at = vertex * 3 + influence, bone = _bones[at];
                if (_weights[at] <= 0) continue;
                _bindLocal[at] = Vector3.TransformNormal(rest - pivot[bone], inverseBind[bone]);
                _affectedVertices[vertex] |= affectedBones[bone];
            }
        }
        _clips = asset.AnimationClips.ToDictionary(clip => clip.SequenceId,
            clip => clip.SourceFrames!.Select(frame => Decode(frame, asset, scale)).ToArray());
    }

    private static Channels[] Decode(Rac1MobyAnimation.Frame frame, Rac1RatchetAvatar.Asset asset, float scale)
    {
        var result = asset.Skeleton.Select((joint, index) =>
        {
            var q = frame.JointRotations[index];
            var rotation = new Quaternion(q.Xf, q.Yf, q.Zf, q.Wf);
            return new Channels(rotation.LengthSquared() > 1e-8 ? Quaternion.Normalize(rotation) : Quaternion.Identity,
                Vector3.One, new Vector3(joint.CommonX, joint.CommonY, joint.CommonZ) * scale);
        }).ToArray();
        foreach (ulong word in frame.Thing1)
        {
            int joint = (int)(word >> 48) & 0x7fff;
            result[joint] = result[joint] with { Scale = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) / 4096 };
        }
        foreach (ulong word in frame.Thing2)
        {
            int joint = (int)(word >> 48);
            result[joint] = result[joint] with { Translation = new Vector3((short)word, (short)(word >> 16), (short)(word >> 32)) * scale };
        }
        return result;
    }

    private static Channels Blend(Channels a, Channels b, float amount) => new(
        Quaternion.Lerp(a.Rotation, b.Rotation, amount), Vector3.Lerp(a.Scale, b.Scale, amount), Vector3.Lerp(a.Translation, b.Translation, amount));

    public double[] Sample(Rac1AnimationFrameClock.Sample basis, Rac1AnimationFrameClock.Sample layer,
        float amount, bool fullBody, double[] baseline)
    {
        var a = _clips[basis.Sequence]; var b = _clips[layer.Sequence];
        for (int joint = 0; joint < _parents.Length; joint++)
        {
            var native = Blend(a[basis.CurrentFrame][joint], a[basis.NextFrame][joint], basis.Fraction);
            if (fullBody || _mask[joint]) native = Blend(native,
                Blend(b[layer.CurrentFrame][joint], b[layer.NextFrame][joint], layer.Fraction), amount);
            // Native linear blocks use the conjugate of Numerics' row quaternion convention.
            var local = Matrix4x4.CreateScale(native.Scale) * Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(native.Rotation));
            local.Translation = native.Translation;
            _world[joint] = joint == 0 ? local : local * _world[_parents[joint]];
        }
        var output = (double[])baseline.Clone();
        for (int vertex = 0; vertex < _affectedVertices.Length; vertex++)
        {
            if (!fullBody && !_affectedVertices[vertex]) continue;
            var point = Vector3.Zero; float total = 0;
            for (int influence = 0; influence < 3; influence++)
            {
                int at = vertex * 3 + influence; float weight = _weights[at];
                if (weight <= 0) continue;
                point += Vector3.Transform(_bindLocal[at], _world[_bones[at]]) * weight; total += weight;
            }
            if (total <= 0) continue;
            point /= total;
            output[vertex * 3] = point.X; output[vertex * 3 + 1] = point.Y; output[vertex * 3 + 2] = point.Z;
        }
        return output;
    }

    public PlayerAvatarAttachmentPose Attachment(int joint)
    {
        var m = _world[joint];
        return new(new(m.M41, m.M42, m.M43), new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33));
    }
}

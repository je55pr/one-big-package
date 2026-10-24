using System.Buffers.Binary;
using System.Numerics;

namespace OBP.PS2.Geometry;

/// <summary>
/// Global gadget skinning from stored model-space bind vertices. Compose the
/// class inverse bind with the animated forward joint transform; applying an
/// animated inverse directly to these vertices folds the weapon together.
/// This path does not change the established level-Moby presentation decoder.
/// </summary>
public static class GcUyaGadgetPose
{
    public static IReadOnlyList<Matrix4x4> ReadInverseBindMatrices(byte[] source)
    {
        if (source.Length < GcUyaMoby.ClassHeaderSize) throw new InvalidDataException("Truncated gadget class header.");
        int count = source[8], offset = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x14));
        if (count == 0) return [];
        if (offset < GcUyaMoby.ClassHeaderSize || offset > source.Length - count * 64)
            throw new InvalidDataException("Gadget bind table exceeds its class.");
        float scale = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(0x24)) / 1024;
        if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Invalid gadget coordinate scale.");
        var matrices = new Matrix4x4[count];
        for (int j = 0; j < count; j++)
        {
            int at = offset + j * 64;
            float F(int i) => BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(at + i * 4));
            // The 3x3 is the global bind orientation; row 3 is already inverse
            // bind translation. Column 3 holds local translation and +0x3c
            // holds packed flags, so neither belongs in this affine matrix.
            var m = new Matrix4x4(F(0), F(1), F(2), 0, F(4), F(5), F(6), 0,
                F(8), F(9), F(10), 0, F(12) * scale, F(13) * scale, F(14) * scale, 1);
            if (!Finite(m) || MathF.Abs(m.GetDeterminant()) < .000001f)
                throw new InvalidDataException("Invalid gadget inverse bind matrix.");
            matrices[j] = m;
        }
        return matrices;
    }

    public static double[] Pose(GcUyaMoby.Mesh mesh, IReadOnlyList<GcUyaMoby.MobyJoint> joints,
        IReadOnlyList<Matrix4x4> inverseBind, GcUyaMoby.MobyFrame frame)
    {
        if (!mesh.SkinStateFullyResolved || joints.Count == 0 || inverseBind.Count != joints.Count ||
            frame.JointRotations.Length < joints.Count || mesh.SkinLocalPositions.Length != mesh.Positions.Length ||
            mesh.VertexJoints.Length != mesh.Positions.Length || mesh.VertexWeights.Length != mesh.Positions.Length)
            throw new InvalidDataException("Gadget animation does not cover its resolved skin.");
        var globals = new Matrix4x4?[joints.Count];
        var busy = new bool[joints.Count];
        Matrix4x4 Resolve(int joint)
        {
            if (globals[joint] is { } matrix) return matrix;
            if (busy[joint]) throw new InvalidDataException("Gadget skeleton contains a parent cycle.");
            busy[joint] = true;
            var q = frame.JointRotations[joint];
            if (!float.IsFinite(q.LengthSquared()) || q.LengthSquared() < .000001f)
                throw new InvalidDataException("Invalid gadget joint quaternion.");
            // Transpose the Numerics row convention to match the native local
            // quaternion blocks. Hierarchies then compose local * parent here.
            var local = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)));
            var t = joints[joint].LocalTranslation;
            local.Translation = new Vector3(t.X, t.Y, t.Z) * (mesh.Scale / 1024);
            int parent = joints[joint].Parent;
            var global = parent >= 0 && parent < joints.Count && parent != joint ? local * Resolve(parent) : local;
            busy[joint] = false;
            globals[joint] = global;
            return global;
        }
        var skin = Enumerable.Range(0, joints.Count).Select(j => inverseBind[j] * Resolve(j)).ToArray();
        var output = new double[mesh.Positions.Length];
        for (int v = 0; v < output.Length / 3; v++)
        {
            var p = new Vector3((float)mesh.SkinLocalPositions[v * 3],
                (float)mesh.SkinLocalPositions[v * 3 + 1], (float)mesh.SkinLocalPositions[v * 3 + 2]);
            var result = Vector3.Zero; float total = 0;
            for (int influence = 0; influence < 3; influence++)
            {
                int at = v * 3 + influence, joint = mesh.VertexJoints[at];
                float weight = mesh.VertexWeights[at];
                if (weight <= 0) continue;
                if ((uint)joint >= skin.Length) throw new InvalidDataException("Gadget vertex joint exceeds its skeleton.");
                result += Vector3.Transform(p, skin[joint]) * weight; total += weight;
            }
            if (total > 0) result /= total; else result = p;
            if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(result.Z))
                throw new InvalidDataException("Non-finite gadget pose.");
            output[v * 3] = result.X; output[v * 3 + 1] = result.Y; output[v * 3 + 2] = result.Z;
        }
        return output;
    }

    private static bool Finite(Matrix4x4 m) => new[] { m.M11, m.M12, m.M13, m.M21, m.M22, m.M23,
        m.M31, m.M32, m.M33, m.M41, m.M42, m.M43 }.All(float.IsFinite);
}

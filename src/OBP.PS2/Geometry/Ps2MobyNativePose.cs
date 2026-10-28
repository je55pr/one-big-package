using System.Buffers.Binary;
using System.Numerics;

namespace OBP.PS2.Geometry;

/// <summary>
/// Source-backed, single-key Moby pose evaluator. This is deliberately separate
/// from the existing renderer/rotation-only animation path: callers must supply
/// native class bytes and a fully decoded regular frame. Pose and inverse bind
/// are evaluated separately; neither is inferred from the legacy static mesh.
/// </summary>
public static class Ps2MobyNativePose
{
    private readonly record struct V(double X, double Y, double Z)
    {
        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V operator *(double n, V v) => new(n * v.X, n * v.Y, n * v.Z);
    }

    // Three basis columns and one translation. Transforms column vectors.
    private readonly record struct M(V X, V Y, V Z, V T)
    {
        public V Vector(V v) => v.X * X + v.Y * Y + v.Z * Z;
        public V Point(V v) => Vector(v) + T;
        public static M operator *(M a, M b) =>
            new(a.Vector(b.X), a.Vector(b.Y), a.Vector(b.Z), a.Point(b.T));
        public M Scale(V s) => new(s.X * X, s.Y * Y, s.Z * Z, T);
    }

    /// <summary>
    /// Evaluate a complete native regular key, including source inherited and
    /// post-chain scales. No interpolation, layers, joint modifiers, source
    /// class state or gameplay sequence selection is performed.
    /// </summary>
    public static double[] Pose(
        byte[] classBytes, GcUyaMoby.Mesh mesh, GcUyaMoby.MobyFrame frame)
    {
        if (!frame.ExtraChannelsResolved)
            throw new InvalidDataException("Native Moby frame has unresolved extra channels.");
        if (!mesh.SkinStateFullyResolved || !mesh.SkinningApplied ||
            mesh.SkinLocalPositions.Length != mesh.Positions.Length ||
            mesh.VertexJoints.Length != mesh.Positions.Length ||
            mesh.VertexWeights.Length != mesh.Positions.Length)
            throw new InvalidDataException("Native Moby pose requires complete, resolved skin streams.");
        if (classBytes.Length < GcUyaMoby.ClassHeaderSize)
            throw new InvalidDataException("Native Moby class header is truncated.");

        int n = classBytes[8];
        int skeleton = BinaryPrimitives.ReadInt32LittleEndian(classBytes.AsSpan(0x14));
        int common = BinaryPrimitives.ReadInt32LittleEndian(classBytes.AsSpan(0x18));
        if (n is <= 0 or > 112 || frame.JointRotations.Length != n ||
            skeleton <= 0 || common <= 0 ||
            (long)skeleton + n * 0x40 > classBytes.Length ||
            (long)common + n * 0x10 > classBytes.Length)
            throw new InvalidDataException("Native Moby skeleton range is invalid.");

        // The source's row vectors are the images of the joint-space axes.
        // S maps model-space to joint-space; S is NOT an animated pose.
        var inverseBind = new M[n];
        var translation = new V[n];
        var parent = new int[n];
        double k = mesh.Scale / 1024.0;
        if (!double.IsFinite(k) || k <= 0)
            throw new InvalidDataException("Invalid native Moby geometry scale.");

        static V ReadTriple(byte[] src, int offset) => new(
            BinaryPrimitives.ReadSingleLittleEndian(src.AsSpan(offset)),
            BinaryPrimitives.ReadSingleLittleEndian(src.AsSpan(offset + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(src.AsSpan(offset + 8)));
        static bool Finite(V v) =>
            double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        for (int j = 0; j < n; j++)
        {
            int at = skeleton + j * 0x40, tr = common + j * 0x10;
            inverseBind[j] = new M(
                ReadTriple(classBytes, at),
                ReadTriple(classBytes, at + 0x10),
                ReadTriple(classBytes, at + 0x20),
                k * ReadTriple(classBytes, at + 0x30));
            translation[j] = k * ReadTriple(classBytes, tr);
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(classBytes.AsSpan(tr + 12));
            if (word == 0) parent[j] = -1;
            else if (word >= 0x70000000 && (word - 0x70000000) % 0x40 == 0 &&
                     (word - 0x70000000) / 0x40 < (uint)j)
                parent[j] = (int)((word - 0x70000000) / 0x40);
            else throw new InvalidDataException("Native Moby joint parent address is invalid.");
            if (!Finite(inverseBind[j].X) || !Finite(inverseBind[j].Y) ||
                !Finite(inverseBind[j].Z) || !Finite(inverseBind[j].T) ||
                !Finite(translation[j]))
                throw new InvalidDataException("Nonfinite native Moby skeleton data.");
        }

        var inherited = Enumerable.Repeat(new V(1, 1, 1), n).ToArray();
        var post = Enumerable.Repeat(new V(1, 1, 1), n).ToArray();

        foreach (var s in frame.ScaleOverrides)
        {
            if (s.Joint < 0 || s.Joint >= n)
                throw new InvalidDataException("Native Moby scale references an invalid joint.");
            var value = new V(s.X, s.Y, s.Z);
            if (!Finite(value) || value.X < 0 || value.Y < 0 || value.Z < 0)
                throw new InvalidDataException("Invalid native Moby joint scale.");
            if (s.Inherited) inherited[s.Joint] = value;
            else post[s.Joint] = value;
        }

        foreach (var tr in frame.TranslationOverrides)
        {
            if (tr.Joint < 0 || tr.Joint >= n)
                throw new InvalidDataException("Native Moby translation references an invalid joint.");
            // Stored s16 is in native packed units; the decoded record used
            // /1024. Reapply the class's geometry scale to match bind units.
            var value = new V(tr.X * mesh.Scale, tr.Y * mesh.Scale, tr.Z * mesh.Scale);
            if (!Finite(value))
                throw new InvalidDataException("Nonfinite native Moby joint translation.");
            translation[tr.Joint] = value;
        }

        var globals = new M[n];
        for (int j = 0; j < n; j++)
        {
            var local = Rotation(frame.JointRotations[j]).Scale(inherited[j]) with
            {
                T = translation[j]
            };
            globals[j] = parent[j] >= 0 ? globals[parent[j]] * local : local;
        }

        var palette = new M[n];
        for (int j = 0; j < n; j++)
            palette[j] = globals[j].Scale(post[j]) * inverseBind[j];

        var output = new double[mesh.Positions.Length];
        for (int v = 0; v < output.Length; v += 3)
        {
            var raw = new V(mesh.SkinLocalPositions[v], mesh.SkinLocalPositions[v + 1],
                mesh.SkinLocalPositions[v + 2]);
            var value = new V(0, 0, 0);
            double sum = 0;
            for (int influence = 0; influence < 3; influence++)
            {
                int i = v + influence;
                double weight = mesh.VertexWeights[i];
                if (weight <= 0) continue;
                int joint = mesh.VertexJoints[i];
                if (joint < 0 || joint >= n || !double.IsFinite(weight))
                    throw new InvalidDataException("Invalid native Moby skin influence.");
                value += weight * palette[joint].Point(raw);
                sum += weight;
            }
            if (!double.IsFinite(sum)) throw new InvalidDataException("Nonfinite Moby weight sum.");
            if (sum == 0) value = raw;
            else value = (1 / sum) * value;
            if (!Finite(value)) throw new InvalidDataException("Nonfinite posed Moby vertex.");
            output[v] = value.X;
            output[v + 1] = value.Y;
            output[v + 2] = value.Z;
        }
        return output;
    }

    /// <summary>
    /// Transform the authored vertex normals using the same affine native
    /// palette as the vertices, without applying palette translations.
    /// </summary>
    public static float[] PoseNormals(
        byte[] classBytes, GcUyaMoby.Mesh mesh, GcUyaMoby.MobyFrame frame, double[] posed)
    {
        if (mesh.Normals.Length != mesh.Positions.Length)
            return mesh.Normals;
        var displacedLocal = new double[mesh.SkinLocalPositions.Length];
        for (int i = 0; i < displacedLocal.Length; i++)
            displacedLocal[i] = mesh.SkinLocalPositions[i] + mesh.Normals[i];
        var displaced = Pose(classBytes, mesh with { SkinLocalPositions = displacedLocal }, frame);
        var normals = new float[mesh.Normals.Length];
        for (int v = 0; v < posed.Length; v += 3)
        {
            double x = displaced[v] - posed[v], y = displaced[v + 1] - posed[v + 1],
                z = displaced[v + 2] - posed[v + 2];
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (!double.IsFinite(length))
                throw new InvalidDataException("Native pose produced a nonfinite vertex normal.");
            if (length < 1e-10)
            {
                // Degenerate authored normals have no stable skinned direction.
                // Preserve their source value instead of failing the level import.
                normals[v] = mesh.Normals[v];
                normals[v + 1] = mesh.Normals[v + 1];
                normals[v + 2] = mesh.Normals[v + 2];
                continue;
            }
            normals[v] = (float)(x / length);
            normals[v + 1] = (float)(y / length);
            normals[v + 2] = (float)(z / length);
        }
        return normals;
    }

    // Native quaternion uses a conjugate rotation relative to the common
    // conventional quaternion-to-matrix mapping (see original evaluator).
    private static M Rotation(Quaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        double length = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (!double.IsFinite(length) || length < 0.5)
            throw new InvalidDataException("Invalid native joint quaternion.");
        x /= length; y /= length; z /= length; w /= length;
        double xx = 2 * x * x, yy = 2 * y * y, zz = 2 * z * z, xy = 2 * x * y, xz = 2 * x * z, yz = 2 * y * z;
        double xw = 2 * x * w, yw = 2 * y * w, zw = 2 * z * w;
        return new M(
            new V(1 - yy - zz, xy - zw, xz + yw),
            new V(xy + zw, 1 - xx - zz, yz - xw),
            new V(xz - yw, yz + xw, 1 - xx - yy),
            new V(0, 0, 0));
    }
}

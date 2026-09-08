using System.Numerics;

namespace OBP.PS2.Geometry;

/// <summary>
/// Engine-independent pose evaluation for the shared Going Commando / UYA Moby
/// skeleton family. This intentionally consumes the preserved skin-local vertex
/// coordinates and the retail-selected VU0 bindings rather than re-posing the
/// legacy static reconstruction.
/// </summary>
public static class GcUyaMobyPose
{
    private readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator *(double s, V3 a) => new(s * a.X, s * a.Y, s * a.Z);
    }

    private readonly record struct M3(
        double A, double B, double C,
        double D, double E, double F,
        double G, double H, double I)
    {
        public static M3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public V3 Apply(V3 v) => new(
            A * v.X + B * v.Y + C * v.Z,
            D * v.X + E * v.Y + F * v.Z,
            G * v.X + H * v.Y + I * v.Z);

        public double Determinant() =>
            A * (E * I - F * H) - B * (D * I - F * G) + C * (D * H - E * G);

        public M3 Inverse()
        {
            double d = Determinant();
            if (!double.IsFinite(d) || Math.Abs(d) < 1e-12)
                throw new InvalidDataException("Moby animation joint matrix is singular.");
            return new M3(
                (E * I - F * H) / d, (C * H - B * I) / d, (B * F - C * E) / d,
                (F * G - D * I) / d, (A * I - C * G) / d, (C * D - A * F) / d,
                (D * H - E * G) / d, (B * G - A * H) / d, (A * E - B * D) / d);
        }

        public static M3 operator *(M3 x, M3 y) => new(
            x.A * y.A + x.B * y.D + x.C * y.G,
            x.A * y.B + x.B * y.E + x.C * y.H,
            x.A * y.C + x.B * y.F + x.C * y.I,
            x.D * y.A + x.E * y.D + x.F * y.G,
            x.D * y.B + x.E * y.E + x.F * y.H,
            x.D * y.C + x.E * y.F + x.F * y.I,
            x.G * y.A + x.H * y.D + x.I * y.G,
            x.G * y.B + x.H * y.E + x.I * y.H,
            x.G * y.C + x.H * y.F + x.I * y.I);
    }

    private readonly record struct Affine(M3 Linear, V3 Translation)
    {
        public V3 Apply(V3 v) => Linear.Apply(v) + Translation;

        public Affine Inverse()
        {
            var inv = Linear.Inverse();
            return new Affine(inv, -1 * inv.Apply(Translation));
        }

        public static Affine operator *(Affine a, Affine b) =>
            new(a.Linear * b.Linear, a.Linear.Apply(b.Translation) + a.Translation);
    }

    /// <summary>Pose one frame in native Z-up class/model space.</summary>
    public static double[] Pose(
        GcUyaMoby.Mesh mesh,
        IReadOnlyList<GcUyaMoby.MobyJoint> joints,
        GcUyaMoby.MobyFrame frame)
    {
        if (!mesh.SkinStateFullyResolved)
            throw new InvalidDataException("Moby skin state is not fully resolved.");
        if (joints.Count == 0 || frame.JointRotations.Length < joints.Count)
            throw new InvalidDataException("Moby animation frame does not cover its skeleton.");
        if (mesh.SkinLocalPositions.Length != mesh.Positions.Length ||
            mesh.VertexJoints.Length != mesh.Positions.Length ||
            mesh.VertexWeights.Length != mesh.Positions.Length)
            throw new InvalidDataException("Moby animation bindings do not match the mesh vertex count.");

        var matrices = BuildInverseGlobals(mesh.Scale / 1024.0, joints, frame);
        var output = new double[mesh.SkinLocalPositions.Length];
        for (int v = 0; v < output.Length / 3; v++)
        {
            var p = new V3(
                mesh.SkinLocalPositions[v * 3],
                mesh.SkinLocalPositions[v * 3 + 1],
                mesh.SkinLocalPositions[v * 3 + 2]);
            var q = new V3(0, 0, 0);
            double sum = 0;
            for (int influence = 0; influence < 3; influence++)
            {
                double weight = mesh.VertexWeights[v * 3 + influence];
                if (weight <= 0) continue;
                int joint = mesh.VertexJoints[v * 3 + influence];
                if (joint < 0 || joint >= matrices.Length)
                    throw new InvalidDataException($"Moby vertex references joint {joint} outside the skeleton.");
                q += weight * matrices[joint].Apply(p);
                sum += weight;
            }
            if (sum <= 0)
            {
                q = p;
            }
            else if (Math.Abs(sum - 1) > 1e-6)
            {
                q = (1 / sum) * q;
            }
            output[v * 3] = q.X;
            output[v * 3 + 1] = q.Y;
            output[v * 3 + 2] = q.Z;
        }
        return output;
    }

    private static Affine[] BuildInverseGlobals(
        double scale,
        IReadOnlyList<GcUyaMoby.MobyJoint> joints,
        GcUyaMoby.MobyFrame frame)
    {
        var local = new Affine[joints.Count];
        for (int j = 0; j < joints.Count; j++)
        {
            var t = joints[j].LocalTranslation;
            local[j] = new Affine(
                QuaternionMatrixTransposed(frame.JointRotations[j]),
                new V3(t.X * scale, t.Y * scale, t.Z * scale));
        }

        var globals = new Affine?[joints.Count];
        var busy = new bool[joints.Count];
        Affine Resolve(int j)
        {
            if (globals[j] is { } existing) return existing;
            if (busy[j]) throw new InvalidDataException("Moby skeleton contains a parent cycle.");
            busy[j] = true;
            int parent = joints[j].Parent;
            Affine value = parent >= 0 && parent < joints.Count && parent != j
                ? Resolve(parent) * local[j]
                : local[j];
            busy[j] = false;
            globals[j] = value;
            return value;
        }

        var inverse = new Affine[joints.Count];
        for (int j = 0; j < joints.Count; j++) inverse[j] = Resolve(j).Inverse();
        return inverse;
    }

    private static M3 QuaternionMatrixTransposed(Quaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        double n = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (!double.IsFinite(n) || n < 1e-9) return M3.Identity;
        x /= n; y /= n; z /= n; w /= n;
        return new M3(
            1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w),
            2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w),
            2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y));
    }
}

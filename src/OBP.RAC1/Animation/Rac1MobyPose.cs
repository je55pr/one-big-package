using OBP.RAC1.Geometry;

namespace OBP.RAC1.Animation;

/// <summary>
/// Evidence-bounded R&amp;C1 Moby pose evaluation. The original one-joint path
/// remains available, while rigid multi-joint hierarchies use the retail-pinned
/// inverse-bind pivots and global frame orientations. The dedicated Ratchet path
/// additionally supports retail-proven static stretch/shear on non-rigid leaf joints.
/// </summary>
public static class Rac1MobyPose
{
    private const double RigidTolerance = 1e-4;
    private const double TailTolerance = 1e-5;

    public static bool CanPoseSingleJointRigid(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        if (mesh.JointCount != 1 || joints.Count != 1 || frame.JointRotations.Count != 1)
        {
            return false;
        }

        var j = joints[0];
        return IsRigid(j.NativeAffine) &&
            System.Math.Abs(j.NativeAffine[12]) < TailTolerance &&
            System.Math.Abs(j.NativeAffine[13]) < TailTolerance &&
            System.Math.Abs(j.NativeAffine[14]) < TailTolerance;
    }

    /// <summary>
    /// True when the frame's rotation cancels the stored inverse-bind linear
    /// transform, reproducing the class rest surface to retail quaternion
    /// quantisation tolerance.
    /// </summary>
    public static bool IsRestAnchor(
        Rac1Moby.SkeletonJoint joint,
        Rac1MobyAnimation.Frame frame,
        double tolerance = 0.002)
    {
        if (frame.JointRotations.Count != 1 || !IsRigid(joint.NativeAffine))
        {
            return false;
        }

        var r = QuaternionMatrix(frame.JointRotations[0]);
        var a = Affine3x3(joint.NativeAffine);
        var skin = Multiply(r, a);
        double error = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                error = System.Math.Max(error,
                    System.Math.Abs(skin[row, col] - (row == col ? 1.0 : 0.0)));
            }
        }
        return error <= tolerance;
    }

    public static double[] PoseSingleJointRigid(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        if (!CanPoseSingleJointRigid(mesh, joints, frame))
        {
            throw new InvalidOperationException("R&C1 Moby pose is outside the pinned one-joint rigid/tail-zero subset.");
        }
        var rotation = QuaternionMatrix(frame.JointRotations[0]);
        var inverseBind = Affine3x3(joints[0].NativeAffine);
        var skin = Multiply(rotation, inverseBind);
        var output = new double[mesh.Positions.Length];
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            double x = mesh.Positions[i];
            double y = mesh.Positions[i + 1];
            double z = mesh.Positions[i + 2];
            output[i] = skin[0, 0] * x + skin[0, 1] * y + skin[0, 2] * z;
            output[i + 1] = skin[1, 0] * x + skin[1, 1] * y + skin[1, 2] * z;
            output[i + 2] = skin[2, 0] * x + skin[2, 1] * y + skin[2, 2] * z;
        }
        return output;
    }

    public static bool CanPoseRigidHierarchy(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        int n = mesh.JointCount;
        if (n <= 0 || joints.Count != n || frame.JointRotations.Count != n ||
            mesh.VertexJoints.Length != mesh.Positions.Length ||
            mesh.VertexWeights.Length != mesh.Positions.Length)
        {
            return false;
        }
        for (int j = 0; j < n; j++)
        {
            if (!IsRigid(joints[j].NativeAffine)) return false;
            if (j > 0 && (joints[j].ParentRecordIndex < 0 || joints[j].ParentRecordIndex >= j)) return false;
        }
        return true;
    }

    public static bool IsRigidHierarchyRestAnchor(
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame,
        double tolerance = 0.002)
    {
        if (joints.Count == 0 || frame.JointRotations.Count != joints.Count) return false;
        for (int j = 0; j < joints.Count; j++)
        {
            if (!IsRigid(joints[j].NativeAffine)) return false;
            var skinLinear = Multiply(
                QuaternionMatrix(frame.JointRotations[j]),
                Affine3x3(joints[j].NativeAffine));
            if (IdentityError(skinLinear) > tolerance) return false;
        }
        return true;
    }

    public static double[] PoseRigidHierarchy(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        if (!CanPoseRigidHierarchy(mesh, joints, frame))
            throw new InvalidOperationException("R&C1 Moby pose is outside the pinned rigid-hierarchy subset.");
        int n = joints.Count;
        double k = mesh.Scale / 1024.0;
        var inverseBind = new double[n][,];
        var bindLinear = new double[n][,];
        var rotation = new double[n][,];
        var tail = new double[n][];
        var bindPivot = new double[n][];
        var localOffset = new double[n][];
        var animatedPivot = new double[n][];
        for (int j = 0; j < n; j++)
        {
            inverseBind[j] = Affine3x3(joints[j].NativeAffine);
            bindLinear[j] = Inverse3x3(inverseBind[j]);
            rotation[j] = QuaternionMatrix(frame.JointRotations[j]);
            tail[j] = new[]
            {
                joints[j].NativeAffine[12] * k,
                joints[j].NativeAffine[13] * k,
                joints[j].NativeAffine[14] * k,
            };
            bindPivot[j] = Negate(Multiply(bindLinear[j], tail[j]));
        }
        for (int j = 0; j < n; j++)
        {
            if (j == 0)
            {
                localOffset[j] = bindPivot[j];
                animatedPivot[j] = bindPivot[j];
                continue;
            }
            int parent = joints[j].ParentRecordIndex;
            localOffset[j] = Multiply(
                inverseBind[parent],
                Subtract(bindPivot[j], bindPivot[parent]));
            animatedPivot[j] = Add(
                animatedPivot[parent],
                Multiply(rotation[parent], localOffset[j]));
        }

        var output = new double[mesh.Positions.Length];
        int vertexCount = mesh.Positions.Length / 3;
        for (int v = 0; v < vertexCount; v++)
        {
            var rest = new[]
            {
                mesh.Positions[v * 3],
                mesh.Positions[v * 3 + 1],
                mesh.Positions[v * 3 + 2],
            };
            var acc = new double[3];
            double weightSum = 0;
            for (int influence = 0; influence < 3; influence++)
            {
                double weight = mesh.VertexWeights[v * 3 + influence];
                if (weight <= 0) continue;
                int joint = mesh.VertexJoints[v * 3 + influence];
                if (joint < 0 || joint >= n)
                    throw new InvalidDataException($"R&C1 Moby vertex {v} references joint {joint} outside {n} joints.");
                var bindLocal = Add(Multiply(inverseBind[joint], rest), tail[joint]);
                var posed = Add(Multiply(rotation[joint], bindLocal), animatedPivot[joint]);
                acc[0] += weight * posed[0];
                acc[1] += weight * posed[1];
                acc[2] += weight * posed[2];
                weightSum += weight;
            }
            if (weightSum <= 0)
            {
                acc = rest;
            }
            else if (System.Math.Abs(weightSum - 1.0) > 1e-5)
            {
                acc[0] /= weightSum;
                acc[1] /= weightSum;
                acc[2] /= weightSum;
            }
            output[v * 3] = acc[0];
            output[v * 3 + 1] = acc[1];
            output[v * 3 + 2] = acc[2];
        }
        return output;
    }

    /// <summary>
    /// Capability gate for the dedicated R&amp;C1 Ratchet hierarchy. Retail class 0
    /// uses parent-composed local quaternions plus sparse fixed-point scale and
    /// translation channels. The common-transform table supplies the bind/local
    /// translation hierarchy; the skeleton tail floats are not used as Ratchet
    /// inverse-bind translation authority.
    /// </summary>
    public static bool CanPoseRatchetHierarchy(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        int n = mesh.JointCount;
        return n > 0 && joints.Count == n && frame.JointRotations.Count == n &&
            mesh.VertexJoints.Length == mesh.Positions.Length &&
            mesh.VertexWeights.Length == mesh.Positions.Length &&
            CanPoseRatchetJointHierarchy(joints, frame);
    }

    public static bool IsRatchetHierarchyBindLinearAnchor(
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame,
        double tolerance = 0.002)
    {
        if (!CanPoseRatchetJointHierarchy(joints, frame)) return false;
        var channels = DecodeRatchetFrameChannels(joints, frame);
        for (int j = 0; j < joints.Count; j++)
        {
            var inverseBind = Affine3x3(joints[j].NativeAffine);
            var bindLinear = Inverse3x3(inverseBind);
            var expectedLocal = j == 0
                ? bindLinear
                : Multiply(bindLinear, Affine3x3(joints[joints[j].ParentRecordIndex].NativeAffine));
            var actualLocal = Multiply(channels.Scales[j], QuaternionMatrix(frame.JointRotations[j]));
            if (MatrixDifference(expectedLocal, actualLocal) > tolerance) return false;
        }
        return true;
    }

    public static double[] PoseRatchetHierarchy(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        if (!CanPoseRatchetHierarchy(mesh, joints, frame))
            throw new InvalidOperationException("R&C1 Ratchet pose is outside the pinned native hierarchy/channel subset.");

        int n = joints.Count;
        double k = mesh.Scale / 1024.0;
        var inverseBind = new double[n][,];
        var bindLinear = new double[n][,];
        var bindPivot = new double[n][];
        var bindTail = new double[n][];
        for (int j = 0; j < n; j++)
        {
            inverseBind[j] = Affine3x3(joints[j].NativeAffine);
            bindLinear[j] = Inverse3x3(inverseBind[j]);
            var common = new[]
            {
                joints[j].CommonX * k,
                joints[j].CommonY * k,
                joints[j].CommonZ * k,
            };
            bindPivot[j] = j == 0
                ? common
                : Add(
                    bindPivot[joints[j].ParentRecordIndex],
                    Multiply(bindLinear[joints[j].ParentRecordIndex], common));
            bindTail[j] = Negate(Multiply(inverseBind[j], bindPivot[j]));
        }

        var channels = DecodeRatchetFrameChannels(joints, frame);
        var animatedLinear = new double[n][,];
        var animatedPivot = new double[n][];
        for (int j = 0; j < n; j++)
        {
            var localLinear = Multiply(channels.Scales[j], QuaternionMatrix(frame.JointRotations[j]));
            var localTranslation = new[]
            {
                channels.Translations[j][0] * k,
                channels.Translations[j][1] * k,
                channels.Translations[j][2] * k,
            };
            if (j == 0)
            {
                animatedLinear[j] = localLinear;
                animatedPivot[j] = localTranslation;
                continue;
            }
            int parent = joints[j].ParentRecordIndex;
            animatedLinear[j] = Multiply(localLinear, animatedLinear[parent]);
            animatedPivot[j] = Add(
                animatedPivot[parent],
                Multiply(animatedLinear[parent], localTranslation));
        }

        var output = new double[mesh.Positions.Length];
        int vertexCount = mesh.Positions.Length / 3;
        for (int v = 0; v < vertexCount; v++)
        {
            var rest = new[]
            {
                mesh.Positions[v * 3],
                mesh.Positions[v * 3 + 1],
                mesh.Positions[v * 3 + 2],
            };
            var acc = new double[3];
            double weightSum = 0;
            for (int influence = 0; influence < 3; influence++)
            {
                double weight = mesh.VertexWeights[v * 3 + influence];
                if (weight <= 0) continue;
                int joint = mesh.VertexJoints[v * 3 + influence];
                if (joint < 0 || joint >= n)
                    throw new InvalidDataException($"R&C1 Ratchet vertex {v} references joint {joint} outside {n} joints.");

                var bindLocal = Add(Multiply(inverseBind[joint], rest), bindTail[joint]);
                var posed = Add(Multiply(animatedLinear[joint], bindLocal), animatedPivot[joint]);
                acc[0] += weight * posed[0];
                acc[1] += weight * posed[1];
                acc[2] += weight * posed[2];
                weightSum += weight;
            }
            if (weightSum <= 0)
            {
                acc = rest;
            }
            else if (System.Math.Abs(weightSum - 1.0) > 1e-5)
            {
                acc[0] /= weightSum;
                acc[1] /= weightSum;
                acc[2] /= weightSum;
            }
            output[v * 3] = acc[0];
            output[v * 3 + 1] = acc[1];
            output[v * 3 + 2] = acc[2];
        }
        return output;
    }

    private static bool CanPoseRatchetJointHierarchy(
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        int n = joints.Count;
        if (n == 0 || frame.JointRotations.Count != n ||
            frame.JointDataSize != n * 8 ||
            frame.UnknownC != frame.JointDataSize + frame.Thing1Count * 8)
        {
            return false;
        }

        var childCounts = new int[n];
        for (int j = 1; j < n; j++)
        {
            int parent = joints[j].ParentRecordIndex;
            if (parent < 0 || parent >= j) return false;
            childCounts[parent]++;
        }
        for (int j = 0; j < n; j++)
        {
            var a = Affine3x3(joints[j].NativeAffine);
            double determinant = Determinant(a);
            if (!double.IsFinite(determinant) || determinant <= 1e-8) return false;
            if (!IsRigid(joints[j].NativeAffine) && childCounts[j] != 0) return false;
        }

        var scaleJoints = new HashSet<int>();
        foreach (ulong raw in frame.Thing1)
        {
            ushort tag = (ushort)(raw >> 48);
            int joint = tag & 0x7fff;
            if ((tag & 0x8000) == 0 || joint >= n || !scaleJoints.Add(joint)) return false;
            // Retail standing frames legitimately use zero scale components to suppress
            // selected attachment geometry, so signed fixed-point values are accepted.
        }

        var translationJoints = new HashSet<int>();
        foreach (ulong raw in frame.Thing2)
        {
            int joint = (ushort)(raw >> 48);
            if (joint >= n || !translationJoints.Add(joint)) return false;
        }
        return true;
    }

    private static (double[][,] Scales, double[][] Translations) DecodeRatchetFrameChannels(
        IReadOnlyList<Rac1Moby.SkeletonJoint> joints,
        Rac1MobyAnimation.Frame frame)
    {
        int n = joints.Count;
        var scales = new double[n][,];
        var translations = new double[n][];
        for (int j = 0; j < n; j++)
        {
            scales[j] = Identity3x3();
            translations[j] =
            [
                joints[j].CommonX,
                joints[j].CommonY,
                joints[j].CommonZ,
            ];
        }

        foreach (ulong raw in frame.Thing1)
        {
            int joint = ((ushort)(raw >> 48)) & 0x7fff;
            double sx = unchecked((short)(raw & 0xffff)) / 4096.0;
            double sy = unchecked((short)((raw >> 16) & 0xffff)) / 4096.0;
            double sz = unchecked((short)((raw >> 32) & 0xffff)) / 4096.0;
            scales[joint] = Scale3x3(sx, sy, sz);
        }
        foreach (ulong raw in frame.Thing2)
        {
            int joint = (ushort)(raw >> 48);
            translations[joint] =
            [
                unchecked((short)(raw & 0xffff)),
                unchecked((short)((raw >> 16) & 0xffff)),
                unchecked((short)((raw >> 32) & 0xffff)),
            ];
        }
        return (scales, translations);
    }

    private static double[,] Scale3x3(double x, double y, double z) => new[,]
    {
        { x, 0d, 0d },
        { 0d, y, 0d },
        { 0d, 0d, z },
    };

    private static double MatrixDifference(double[,] a, double[,] b)
    {
        double error = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                error = System.Math.Max(error, System.Math.Abs(a[row, col] - b[row, col]));
        return error;
    }

    private static double[,] Identity3x3() => new[,]
    {
        { 1d, 0d, 0d }, { 0d, 1d, 0d }, { 0d, 0d, 1d },
    };

    private static double[,] Transpose(double[,] matrix) => new[,]
    {
        { matrix[0,0], matrix[1,0], matrix[2,0] },
        { matrix[0,1], matrix[1,1], matrix[2,1] },
        { matrix[0,2], matrix[1,2], matrix[2,2] },
    };

    private static double Determinant(double[,] a) =>
        a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) -
        a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) +
        a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);

    private static bool IsRigidMatrix(double[,] a)
    {
        for (int row = 0; row < 3; row++)
        {
            double length2 = 0;
            for (int col = 0; col < 3; col++) length2 += a[row, col] * a[row, col];
            if (System.Math.Abs(length2 - 1.0) > RigidTolerance) return false;
        }
        for (int aRow = 0; aRow < 3; aRow++)
            for (int bRow = aRow + 1; bRow < 3; bRow++)
            {
                double dot = 0;
                for (int col = 0; col < 3; col++) dot += a[aRow, col] * a[bRow, col];
                if (System.Math.Abs(dot) > RigidTolerance) return false;
            }
        return System.Math.Abs(Determinant(a) - 1.0) <= RigidTolerance;
    }

    private static double IdentityError(double[,] matrix)
    {
        double error = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                error = System.Math.Max(error,
                    System.Math.Abs(matrix[row, col] - (row == col ? 1.0 : 0.0)));
        return error;
    }

    private static double[,] Inverse3x3(double[,] a)
    {
        double det =
            a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) -
            a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) +
            a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        if (System.Math.Abs(det) < 1e-12)
            throw new InvalidDataException("R&C1 Moby inverse-bind 3x3 is singular.");
        return new[,]
        {
            { (a[1,1]*a[2,2]-a[1,2]*a[2,1])/det, (a[0,2]*a[2,1]-a[0,1]*a[2,2])/det, (a[0,1]*a[1,2]-a[0,2]*a[1,1])/det },
            { (a[1,2]*a[2,0]-a[1,0]*a[2,2])/det, (a[0,0]*a[2,2]-a[0,2]*a[2,0])/det, (a[0,2]*a[1,0]-a[0,0]*a[1,2])/det },
            { (a[1,0]*a[2,1]-a[1,1]*a[2,0])/det, (a[0,1]*a[2,0]-a[0,0]*a[2,1])/det, (a[0,0]*a[1,1]-a[0,1]*a[1,0])/det },
        };
    }

    private static double[] Multiply(double[,] matrix, double[] vector) =>
    [
        matrix[0,0]*vector[0] + matrix[0,1]*vector[1] + matrix[0,2]*vector[2],
        matrix[1,0]*vector[0] + matrix[1,1]*vector[1] + matrix[1,2]*vector[2],
        matrix[2,0]*vector[0] + matrix[2,1]*vector[1] + matrix[2,2]*vector[2],
    ];

    private static double[] Add(double[] a, double[] b) =>
        [a[0] + b[0], a[1] + b[1], a[2] + b[2]];

    private static double[] Subtract(double[] a, double[] b) =>
        [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Negate(double[] a) => [-a[0], -a[1], -a[2]];

    private static double[,] Affine3x3(float[] affine) => new[,]
    {
        { (double)affine[0], affine[1], affine[2] },
        { affine[4], affine[5], affine[6] },
        { affine[8], affine[9], affine[10] },
    };

    private static bool IsRigid(float[] affine)
    {
        if (affine.Length != 15)
        {
            return false;
        }
        var a = Affine3x3(affine);
        for (int row = 0; row < 3; row++)
        {
            double length2 = 0;
            for (int col = 0; col < 3; col++) length2 += a[row, col] * a[row, col];
            if (System.Math.Abs(length2 - 1.0) > RigidTolerance) return false;
        }
        for (int aRow = 0; aRow < 3; aRow++)
        {
            for (int bRow = aRow + 1; bRow < 3; bRow++)
            {
                double dot = 0;
                for (int col = 0; col < 3; col++) dot += a[aRow, col] * a[bRow, col];
                if (System.Math.Abs(dot) > RigidTolerance) return false;
            }
        }
        double det =
            a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) -
            a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) +
            a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        return System.Math.Abs(det - 1.0) <= RigidTolerance;
    }

    private static double[,] QuaternionMatrix(Rac1MobyAnimation.JointQuaternion q)
    {
        double x = q.Xf, y = q.Yf, z = q.Zf, w = q.Wf;
        double length = System.Math.Sqrt(x * x + y * y + z * z + w * w);
        if (length <= 1e-8) return new[,] { { 1d, 0, 0 }, { 0, 1d, 0 }, { 0, 0, 1d } };
        x /= length; y /= length; z /= length; w /= length;
        return new[,]
        {
            { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w) },
            { 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w) },
            { 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y) },
        };
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var output = new double[3, 3];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                for (int k = 0; k < 3; k++) output[row, col] += a[row, k] * b[k, col];
        return output;
    }
}

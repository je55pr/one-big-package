using OBP.RAC1.Geometry;

namespace OBP.RAC1.Animation;

/// <summary>
/// Conservative R&amp;C1 Moby pose evaluation for the retail-pinned one-joint
/// subset. Multi-joint hierarchy and non-rigid bind transforms intentionally
/// remain unsupported until their native transform composition is fully proved.
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

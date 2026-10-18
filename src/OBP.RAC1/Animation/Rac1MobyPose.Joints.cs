using OBP.RAC1.Geometry;

namespace OBP.RAC1.Animation;

public static partial class Rac1MobyPose
{
    /// <summary>Native local joint origin and row-vector linear transform, before instance placement.</summary>
    public sealed record JointPose(double[] Position, double[,] NativeLinear);

    public static IReadOnlyList<JointPose> PoseRatchetJoints(
        Rac1Moby.Mesh mesh, IReadOnlyList<Rac1Moby.SkeletonJoint> joints, Rac1MobyAnimation.Frame frame)
    {
        if (!CanPoseRatchetHierarchy(mesh, joints, frame))
            throw new InvalidOperationException("Joint pose is outside the recovered R&C1 hierarchy/channel subset.");
        var (linear, pivot) = EvaluateRatchetJointHierarchy(mesh, joints, frame);
        return Enumerable.Range(0, joints.Count).Select(j => new JointPose(pivot[j], linear[j])).ToArray();
    }

    private static (double[][,] Linear, double[][] Pivot) EvaluateRatchetJointHierarchy(
        Rac1Moby.Mesh mesh, IReadOnlyList<Rac1Moby.SkeletonJoint> joints, Rac1MobyAnimation.Frame frame)
    {
        double scale = mesh.Scale / 1024d;
        var channels = DecodeRatchetFrameChannels(joints, frame);
        var linear = new double[joints.Count][,];
        var pivot = new double[joints.Count][];
        for (int j = 0; j < joints.Count; j++)
        {
            var local = Multiply(channels.Scales[j], QuaternionMatrix(frame.JointRotations[j]));
            var translation = channels.Translations[j].Select(value => value * scale).ToArray();
            if (j == 0)
            {
                linear[j] = local;
                pivot[j] = translation;
            }
            else
            {
                int parent = joints[j].ParentRecordIndex;
                linear[j] = Multiply(local, linear[parent]);
                pivot[j] = Add(pivot[parent], Multiply(Transpose(linear[parent]), translation));
            }
        }
        return (linear, pivot);
    }
}

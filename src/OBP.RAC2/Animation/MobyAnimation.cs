using System.Numerics;
using OBP.RAC2.Geometry;

namespace OBP.RAC2.Animation;

/// <summary>
/// Engine-independent Going Commando <see cref="GcMoby.MobySequence"/> pose
/// evaluation. The moby decoder bakes the rest pose into
/// <see cref="GcMoby.Mesh.Positions"/> and records each vertex's bone binding
/// (<see cref="GcMoby.Mesh.VertexJoints"/> / <see cref="GcMoby.Mesh.VertexWeights"/>);
/// this re-poses those vertices for a given animation frame.
///
/// <para>Model: every GC bind-pose joint rotation is identity, so a joint's bind
/// global transform is the pure translation the rest-pose decoder subtracted
/// (<see cref="GcMoby.MobyJoint.Bx"/>… = <c>-skeletonRow3</c>). A frame supplies
/// one local rotation quaternion per joint; the animated global transform is the
/// usual hierarchy product, and the per-joint skinning transform is
/// <c>animGlobal[j] · bindGlobal[j]⁻¹</c> (standard linear blend skinning).
/// Posing a vertex is <c>Σ wᵢ · rest · skin[jᵢ]</c>; with identity quaternions
/// every <c>skin[j]</c> is the identity and this returns the rest pose exactly.</para>
///
/// <para><see cref="Matrix4x4"/> follows the <c>System.Numerics</c> row-vector
/// convention: <c>Vector3.Transform(v, A * B)</c> applies <c>A</c> then <c>B</c>,
/// and a hierarchy composes child-local-first (<c>local · parentGlobal</c>).</para>
/// </summary>
public static class MobyAnimation
{
    /// <summary>
    /// Per-joint skinning matrices for one frame: multiply a rest-pose (model
    /// frame, native Z-up) position by <c>skin[j]</c> to get its animated
    /// position for joint <c>j</c>. <paramref name="k"/> is <c>scale / 1024</c>.
    /// </summary>
    public static Matrix4x4[] SkinMatrices(IReadOnlyList<GcMoby.MobyJoint> joints, GcMoby.MobyFrame frame, float k)
    {
        int n = joints.Count;
        var bindGlobal = new Vector3[n];
        for (int j = 0; j < n; j++)
        {
            bindGlobal[j] = new Vector3(joints[j].Bx, joints[j].By, joints[j].Bz) * k;
        }

        var animGlobal = new Matrix4x4[n];
        for (int j = 0; j < n; j++)
        {
            int p = joints[j].Parent;
            bool hasParent = p >= 0 && p < n && p != j;
            var localTrans = hasParent ? bindGlobal[j] - bindGlobal[p] : bindGlobal[j];
            var q = frame.JointRotations.Length > j ? Normalize(frame.JointRotations[j]) : Quaternion.Identity;
            var local = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(localTrans);
            animGlobal[j] = hasParent ? local * animGlobal[p] : local;
        }

        var skin = new Matrix4x4[n];
        for (int j = 0; j < n; j++)
        {
            skin[j] = animGlobal[j] * Matrix4x4.CreateTranslation(-bindGlobal[j]);
        }

        return skin;
    }

    /// <summary>
    /// Re-pose <paramref name="mesh"/> for <paramref name="frame"/>. Returns a
    /// flat <c>XYZ</c> array the same length as <see cref="GcMoby.Mesh.Positions"/>,
    /// in the class's model frame (native Z-up). Identity-quaternion frames
    /// reproduce <see cref="GcMoby.Mesh.Positions"/>.
    /// </summary>
    public static double[] Pose(GcMoby.Mesh mesh, IReadOnlyList<GcMoby.MobyJoint> joints, GcMoby.MobyFrame frame)
    {
        var outp = new double[mesh.Positions.Length];
        int vc = mesh.Positions.Length / 3;
        if (joints.Count == 0 || mesh.VertexJoints.Length != mesh.Positions.Length)
        {
            System.Array.Copy(mesh.Positions, outp, outp.Length);
            return outp;
        }

        var skin = SkinMatrices(joints, frame, mesh.Scale / 1024f);

        for (int v = 0; v < vc; v++)
        {
            var rest = new Vector3(
                (float)mesh.Positions[v * 3],
                (float)mesh.Positions[v * 3 + 1],
                (float)mesh.Positions[v * 3 + 2]);

            Vector3 acc = Vector3.Zero;
            float wsum = 0f;
            for (int inf = 0; inf < 3; inf++)
            {
                float w = mesh.VertexWeights[v * 3 + inf];
                if (w <= 0f)
                {
                    continue;
                }

                int ji = mesh.VertexJoints[v * 3 + inf];
                if (ji < 0 || ji >= skin.Length)
                {
                    ji = 0;
                }

                acc += w * Vector3.Transform(rest, skin[ji]);
                wsum += w;
            }

            if (wsum <= 0f)
            {
                acc = rest;
            }
            else if (System.MathF.Abs(wsum - 1f) > 1e-4f)
            {
                acc /= wsum;
            }

            outp[v * 3] = acc.X;
            outp[v * 3 + 1] = acc.Y;
            outp[v * 3 + 2] = acc.Z;
        }

        return outp;
    }

    private static Quaternion Normalize(Quaternion q)
    {
        float len = System.MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return len is > 1e-6f and < 10f
            ? new Quaternion(q.X / len, q.Y / len, q.Z / len, q.W / len)
            : Quaternion.Identity;
    }
}

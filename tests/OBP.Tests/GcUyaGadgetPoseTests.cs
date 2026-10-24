using System.Buffers.Binary;
using System.Numerics;
using OBP.PS2.Geometry;

namespace OBP.Tests;

public sealed class GcUyaGadgetPoseTests
{
    [Fact]
    public void BindSpaceVertexStaysAssembledAtRestAndRotatesAroundItsJointPivot()
    {
        var mesh = new GcUyaMoby.Mesh([99, 99, 99], [0, 0], [], [], 1024, true, true)
        {
            SkinLocalPositions = [2, 0, 0],
            VertexJoints = [1, 0, 0],
            VertexWeights = [1, 0, 0]
        };
        GcUyaMoby.MobyJoint[] joints = [new(0, 0, 0, 0), new(0, 1, 0, 0) { LocalTranslation = (1, 0, 0) }];
        Matrix4x4[] bind = [Matrix4x4.Identity, Matrix4x4.CreateTranslation(-1, 0, 0)];
        var rest = GcUyaGadgetPose.Pose(mesh, joints, bind, new(1, [Quaternion.Identity, Quaternion.Identity]));
        Assert.Equal(new double[] { 2, 0, 0 }, rest);
        var animated = GcUyaGadgetPose.Pose(mesh, joints, bind,
            new(1, [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)]));
        Assert.InRange(animated[0], .99999, 1.00001);
        Assert.InRange(animated[1], -1.00001, -.99999);
        Assert.InRange(animated[2], -.00001, .00001);
    }

    [Fact]
    public void ClassInverseBindUsesOrientationAndRowTranslationWithoutPackedFlagsOrLocalTranslation()
    {
        var bytes = new byte[GcUyaMoby.ClassHeaderSize + 64];
        bytes[8] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x14), GcUyaMoby.ClassHeaderSize);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x24), 1024);
        float[] native = [0, -1, 0, 999, 1, 0, 0, 999, 0, 0, 1, 999, 2, 3, 4, 1.58e29f];
        for (int i = 0; i < native.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(
            bytes.AsSpan(GcUyaMoby.ClassHeaderSize + i * 4), native[i]);
        var inverse = Assert.Single(GcUyaGadgetPose.ReadInverseBindMatrices(bytes));
        Assert.Equal(new Vector3(2, 2, 4), Vector3.Transform(Vector3.UnitX, inverse));
        Assert.Equal(1, inverse.M44);
        Assert.Equal(0, inverse.M14);
    }
}

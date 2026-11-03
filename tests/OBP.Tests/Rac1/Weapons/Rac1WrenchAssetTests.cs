using OBP.RAC1.Player;
using OBP.RAC1.Animation;
using OBP.RAC1.Geometry;

namespace OBP.Tests;

public sealed class Rac1WrenchAssetTests
{
    [Fact]
    public void LocalHierarchyRetainsTheStoredMechanicalRetractionInsteadOfCancellingItAsRatchetBinding()
    {
        float[] root = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0];
        float[] shaft = (float[])root.Clone();
        shaft[12] = -2;
        var joints = new[]
        {
            new Rac1Moby.SkeletonJoint(0, 0, 0, 0, root, 0, 0, 0),
            new Rac1Moby.SkeletonJoint(1, 0, 0, 0, shaft, 1, 0, 0),
        };
        var mesh = new Rac1Moby.Mesh([2, 0, 0], [], [], [], 1024, 0, 0, 2)
        { VertexJoints = [1, 0, 0], VertexWeights = [1, 0, 0] };
        var identity = new Rac1MobyAnimation.JointQuaternion(0, 0, 0, 32767);
        var frame = new Rac1MobyAnimation.Frame(0, 0, 1, 16, 0, 16, 0, [identity, identity], [], []);
        Assert.Equal(new double[] { 1, 0, 0 }, Rac1MobyPose.PoseLocalHierarchy(mesh, joints, frame));
        Assert.Equal(new double[] { 2, 0, 0 }, Rac1MobyPose.PoseRatchetHierarchy(mesh, joints, frame));
    }

    [SkippableFact]
    public void NeutralWrenchRetractsItsShaftAndJawsFromTheWideBindMesh()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var asset = Rac1WrenchAsset.Load(iso!);
        var pose = Rac1WrenchAsset.DecodeAnimation(asset, Rac1WrenchAsset.NeutralNativeSequenceId).Frames[0];
        double Extent(double[] positions, int axis)
        {
            var values = positions.Where((_, index) => index % 3 == axis).ToArray();
            return values.Max() - values.Min();
        }
        Assert.InRange(Extent(pose, 0), .8520, .8522);
        Assert.InRange(Extent(pose, 1), .4506, .4509);
        Assert.InRange(Extent(asset.Mesh.Positions, 1), 1.0101, 1.0104);
        Assert.False(pose.SequenceEqual(asset.Mesh.Positions));
    }

    [SkippableFact]
    public void Level0RecoversRetailGlobalClass71Wrench()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        var wrench = Rac1WrenchAsset.Load(iso!);

        Assert.Equal(0x00f90230, wrench.CompressedSourceOffset);
        Assert.Equal(9, wrench.Mesh.HighLodPacketCount);
        Assert.Equal(12, wrench.Mesh.JointCount);
        Assert.Equal(12, wrench.Skeleton.Count);
        Assert.Equal(837, wrench.VertexCount);
        Assert.Equal(922, wrench.TriangleCount);
        Assert.Equal(20, wrench.Sequences.Count);
        Assert.Equal(
            1,
            wrench.Sequences[Rac1WrenchAsset.NeutralNativeSequenceId].Value?.Frames.Count);
    }

    [SkippableFact]
    public void Level0WrenchUsesClass71TextureMappingRatherThanClass110Lookalike()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        var wrench = Rac1WrenchAsset.Load(iso!);

        var surface = Assert.Single(wrench.Surfaces);
        var texture = Assert.Single(wrench.Textures);
        Assert.Equal(15, surface.TextureId);
        Assert.Equal(15, texture.TextureId);
        Assert.Equal(922, surface.Indices.Length / 3);
        Assert.Equal(837 * 2, surface.Uvs.Length);
        Assert.True(texture.Width > 0);
        Assert.True(texture.Height > 0);
        Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length);
    }

    [Fact]
    public void RetailIdentityConstantsStayPinned()
    {
        Assert.Equal(8, Rac1WrenchAsset.NativeItemId);
        Assert.Equal(71, Rac1WrenchAsset.NativeClassId);
        Assert.Equal(0xb8, Rac1WrenchAsset.OwnerMobyOffset);
        Assert.Equal(0x5628, Rac1WrenchAsset.CompressedSourceSize);
        Assert.Equal(
            "6923ebb76c882fd58f9a04220a722a3f9e18c720109e28aaac4aecd08a633306",
            Rac1WrenchAsset.CompressedSourceSha256);
    }
}

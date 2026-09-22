using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1WrenchAssetTests
{
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

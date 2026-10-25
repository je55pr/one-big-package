using OBP.PS2.Geometry;
using OBP.RAC2.Geometry;

namespace OBP.Tests;

public sealed class RcSharedCodecTests
{
    [Fact]
    public void Rac2TfragFacadePreservesSharedDecoderOutput()
    {
        // Small structurally valid empty tfrag container: table starts at 0x10,
        // count zero. The point of this fixture is API/facade identity; retail
        // equivalence remains covered by the gated GC/R&C1 tests.
        var bytes = new byte[0x10];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0), 0x10);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0);

        var shared = RcTfrag.Read(bytes);
        var rac2 = Rac2Tfrag.Read(bytes);

        Assert.Equal(shared.TfragCount, rac2.TfragCount);
        Assert.Equal(shared.Positions, rac2.Positions);
        Assert.Equal(shared.Uvs, rac2.Uvs);
        Assert.Equal(shared.Colors, rac2.Colors);
        Assert.Equal(shared.VertexAlpha, rac2.VertexAlpha);
        Assert.Equal(shared.Indices, rac2.Indices);
        Assert.Equal(shared.TriangleTextureIds, rac2.TriangleTextureIds);
        Assert.Equal(shared.TriangleMaterialIndices, rac2.TriangleMaterialIndices);
        Assert.Equal(shared.Materials, rac2.Materials);
        Assert.Equal(shared.TextureIds, rac2.TextureIds);
        Assert.Equal(shared.BoundsMin, rac2.BoundsMin);
        Assert.Equal(shared.BoundsMax, rac2.BoundsMax);
    }
}

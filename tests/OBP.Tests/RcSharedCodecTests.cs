using OBP.PS2.Geometry;
using OBP.RAC2.Geometry;

namespace OBP.Tests;

public sealed class RcSharedCodecTests
{
    [Fact]
    public void GcTfragFacadePreservesSharedDecoderOutput()
    {
        // Small structurally valid empty tfrag container: table starts at 0x10,
        // count zero. The point of this fixture is API/facade identity; retail
        // equivalence remains covered by the gated GC/R&C1 tests.
        var bytes = new byte[0x10];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0), 0x10);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0);

        var shared = RcTfrag.Read(bytes);
        var gc = GcTfrag.Read(bytes);

        Assert.Equal(shared.TfragCount, gc.TfragCount);
        Assert.Equal(shared.Positions, gc.Positions);
        Assert.Equal(shared.Uvs, gc.Uvs);
        Assert.Equal(shared.Colors, gc.Colors);
        Assert.Equal(shared.Indices, gc.Indices);
        Assert.Equal(shared.TriangleTextureIds, gc.TriangleTextureIds);
        Assert.Equal(shared.TextureIds, gc.TextureIds);
        Assert.Equal(shared.BoundsMin, gc.BoundsMin);
        Assert.Equal(shared.BoundsMax, gc.BoundsMax);
    }
}

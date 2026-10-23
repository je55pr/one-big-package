using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public sealed class GcLancerAssetsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(65548)]
    public void InvalidDirectorySizesAreRejectedBeforeReadingEntries(int size)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, size);
        Assert.Throws<InvalidDataException>(() => GcLancerAssets.ReadHeld(new InMemoryReader("gadget", bytes)));
    }

    [Fact]
    public void MissingLancerDoesNotSelectAnotherGadget()
    {
        var bytes = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 28);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 3077);
        Assert.Throws<InvalidDataException>(() => GcLancerAssets.ReadHeld(new InMemoryReader("gadget", bytes)));
    }

    [Theory]
    [InlineData(-1, 17, 466, 9)]
    [InlineData(449, -1, 466, 9)]
    [InlineData(449, int.MaxValue, 466, 9)]
    [InlineData(449, 17, 450, 9)]
    public void InvalidOrOverlappingAssetRangesAreRejected(int data, int size, int bank, int bankSize)
    {
        var bytes = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 28);
        int[] row = [1490, data, size, bank, bankSize];
        for (int i = 0; i < row.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 + i * 4), row[i]);
        Assert.Throws<InvalidDataException>(() => GcLancerAssets.ReadHeld(new InMemoryReader("gadget", bytes)));
    }

    [Theory]
    [InlineData(2048, 64, 2200, 64)] // model overlaps the 16-byte data header
    [InlineData(2064, 64, 2100, 64)] // texture overlaps model
    [InlineData(4096, 64, 4160, 64)] // inner range points into the sound bank
    [InlineData(2064, 64, 5000, 64)] // texture exceeds parent data slice
    public void NestedOffsetsCannotEscapeTheGadgetDataRange(int model, int modelSize, int texture, int textureSize)
    {
        var bytes = new byte[6144];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 28);
        int[] row = [1490, 1, 1, 2, 1];
        for (int i = 0; i < row.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 + i * 4), row[i]);
        int[] ranges = [model, modelSize, texture, textureSize];
        for (int i = 0; i < ranges.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2048 + i * 4), ranges[i]);
        Assert.Throws<InvalidDataException>(() => GcLancerAssets.ReadHeld(new InMemoryReader("gadget", bytes)));
    }
}

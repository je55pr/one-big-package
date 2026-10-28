using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2MobyClassBytesTests
{
    private static Rac2LevelCore.Core Core(int start = 16, int end = 32)
    {
        var index = new byte[GcUyaMoby.ClassEntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(index, start);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(4), 2827);
        var assets = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var header = new Rac2LevelCore.CoreHeader(default, 0, 0, 0, 0, new(1, 0), default, default,
            default, default, default, default, default, default, 0, 0, 0, 0, 0, 0, []);
        return new(null!, header, index, assets, [], [end]);
    }

    [Fact]
    public void ClassBytesUseTheNextSectionBoundaryOrAssetEnd()
    {
        var core = Core();
        Assert.Equal(core.Assets[16..32], Rac2MobyClasses.ClassBytes(core, 2827));
        Assert.Equal(core.Assets[16..], Rac2MobyClasses.ClassBytes(core with { SectionBoundaries = [] }, 2827));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(int.MaxValue)]
    public void InvalidStartOffsetsThrowInvalidData(int start)
    {
        Assert.Throws<InvalidDataException>(() => Rac2MobyClasses.ClassBytes(Core(start: start), 2827));
    }

    [Theory]
    [InlineData(257)]
    [InlineData(int.MaxValue)]
    public void EndOffsetsBeyondAssetsThrowInvalidData(int end)
    {
        Assert.Throws<InvalidDataException>(() => Rac2MobyClasses.ClassBytes(Core(end: end), 2827));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidTableBoundsThrowInvalidData(int offset, int count)
    {
        var core = Core();
        core = core with { Header = core.Header with { MobyClasses = new(count, offset) } };
        Assert.Throws<InvalidDataException>(() => Rac2MobyClasses.ClassBytes(core, 2827));
    }
}

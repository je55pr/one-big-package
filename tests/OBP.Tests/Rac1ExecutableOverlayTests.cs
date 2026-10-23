using System.Buffers.Binary;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1ExecutableOverlayTests
{
    [Fact]
    public void Overlay_ReadsMappedVirtualRangesWithoutTreatingGapsAsData()
    {
        var overlay = Rac1ExecutableOverlay.Parse([.. Section(0x100, 0x100, [1, 2, 3, 4]), .. Section(0x200, 0x100, [5, 6, 7, 8])]);
        Assert.Equal(0x100, overlay.EntryPoint);
        Assert.Equal(new byte[] { 2, 3 }, overlay.Read(0x101, 2).ToArray());
        Assert.Throws<InvalidDataException>(() => overlay.Read(0x102, 4));
        Assert.Throws<InvalidDataException>(() => overlay.Read(0x104, 1));
        Assert.Throws<InvalidDataException>(() => overlay.Read(int.MaxValue, 2));
        Assert.Throws<InvalidDataException>(() => overlay.Read(0x100, -1));
    }

    [Fact]
    public void Overlay_RejectsTruncationOverlapUnmappedEntryAndInvalidSectionHeaders()
    {
        Assert.Throws<InvalidDataException>(() => Rac1ExecutableOverlay.Parse([]));
        byte[] valid = Section(0x100, 0x100, [1, 2, 3, 4]);
        for (int size = 1; size < valid.Length; ++size)
            Assert.Throws<InvalidDataException>(() => Rac1ExecutableOverlay.Parse(valid.AsSpan(0, size)));
        Assert.Throws<InvalidDataException>(() => Rac1ExecutableOverlay.Parse([.. valid, .. valid]));
        Assert.Throws<InvalidDataException>(() => Rac1ExecutableOverlay.Parse([.. valid, .. Section(0x200, 0x200, [1, 2, 3, 4])]));
        Assert.Throws<InvalidDataException>(() => Rac1ExecutableOverlay.Parse(Section(0x100, 0x200, [1, 2, 3, 4])));
        foreach (var (offset, value) in new[] { (0, -4), (0, 3), (0, 32 * 1024 * 1024), (4, -4), (4, 3), (4, int.MaxValue), (8, 0) })
        {
            byte[] invalid = (byte[])valid.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(offset), value);
            Assert.Throws<InvalidDataException>(() => Rac1ExecutableOverlay.Parse(invalid));
        }
    }

    private static byte[] Section(int address, int entry, byte[] data)
    {
        var bytes = new byte[16 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, address);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), entry);
        data.CopyTo(bytes, 16);
        return bytes;
    }
}

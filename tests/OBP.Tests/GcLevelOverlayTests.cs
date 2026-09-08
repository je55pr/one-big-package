using System.Buffers.Binary;
using OBP.RAC2.Level;

namespace OBP.Tests;

public class GcLevelOverlayTests
{
    [Fact]
    public void ParseMapsAFileBackedVirtualRange()
    {
        var raw = OneSection(0x001A8000, [10, 20, 30, 40, 50, 60, 70, 80]);

        var overlay = GcLevelOverlay.Parse(raw);

        var section = Assert.Single(overlay.Sections);
        Assert.Equal(0x001A8000u, section.DestinationAddress);
        Assert.Equal(8, section.CopySize);
        Assert.Equal(new byte[] { 30, 40, 50 }, overlay.ReadVirtual(0x001A8002, 3));
    }

    [Fact]
    public void ReadVirtualRejectsRangesOutsideOneSection()
    {
        var overlay = GcLevelOverlay.Parse(OneSection(0x2000, [1, 2, 3, 4]));

        Assert.Throws<InvalidDataException>(() => overlay.ReadVirtual(0x1FFF, 1));
        Assert.Throws<InvalidDataException>(() => overlay.ReadVirtual(0x2002, 3));
    }

    [Fact]
    public void ParseRejectsSectionPayloadOverrun()
    {
        var raw = new byte[GcLevelOverlay.SectionHeaderBytes + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x00, 4), 0x3000);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04, 4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x08, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C, 4), 0x4000);

        Assert.Throws<InvalidDataException>(() => GcLevelOverlay.Parse(raw));
    }

    private static byte[] OneSection(uint destination, byte[] payload)
    {
        var raw = new byte[GcLevelOverlay.SectionHeaderBytes + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x00, 4), destination);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x08, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C, 4), 0x5000);
        payload.CopyTo(raw.AsSpan(GcLevelOverlay.SectionHeaderBytes));
        return raw;
    }
}

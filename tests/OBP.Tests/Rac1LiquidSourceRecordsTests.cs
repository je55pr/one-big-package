using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1LiquidSourceRecordsTests
{
    private static byte[] Section(uint address, int type, uint entry, byte[] data)
    {
        var bytes = new byte[data.Length + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, address);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), entry);
        data.CopyTo(bytes, 16);
        return bytes;
    }

    [Fact]
    public void OverlayReaderReadsOnlyInitializedNativeAddressSpace()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8];
        var overlay = Rac1LevelOverlay.Decode([
            ..Section(0x2000, 1, 0x1234, source),
            ..Section(0x3000, 8, 0x1234, new byte[16]),
        ]);
        Assert.Equal(2, overlay.Sections.Count);
        Assert.Equal((uint)0x1234, overlay.EntryPoint);
        Assert.Equal([3, 4, 5], overlay.Read(0x2002, 3).ToArray());
        Assert.False(overlay.TryRead(0x2007, 2, out _));
        Assert.False(overlay.TryRead(0x3000, 4, out _)); // BSS is not source-authored bytes
        Assert.Throws<InvalidDataException>(() => overlay.Read(0x1fff, 2));
    }

    [Fact]
    public void OverlayRejectsOverlapsAndTruncation()
    {
        Assert.Throws<InvalidDataException>(() => Rac1LevelOverlay.Decode([
            ..Section(0x2000, 1, 0x1234, new byte[8]),
            ..Section(0x2004, 1, 0x1234, new byte[8]),
        ]));
        var truncated = Section(0x2000, 1, 0x1234, new byte[8])[..^1];
        Assert.Throws<InvalidDataException>(() => Rac1LevelOverlay.Decode(truncated));
        Assert.Throws<InvalidDataException>(() =>
            Rac1LevelOverlay.Decode(Section(0x2000, 7, 0x1234, new byte[8])));
    }

    [SkippableTheory]
    [InlineData(1)] // Novalis water
    [InlineData(2)] // Aridia ocean
    [InlineData(9)] // Gaspar lava
    [InlineData(11)] // Pokitaru water
    public void RetailLevelOverlayCanReadInitializedSectionsWithoutUsingCollision(int levelId)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == levelId);
        var overlay = Rac1LevelOverlay.Open(disc, level);
        Assert.True(overlay.EntryPoint > 0);
        Assert.Contains(overlay.Sections, s => s.Type == 1 && s.Data.Length > 0);
        Assert.Contains(overlay.Sections, s => s.Type == 8);
    }

    [SkippableFact]
    public void RetailAridiaOceanKeepsSevenSourceDrawRecordsAndFxFrames()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 2);
        var records = Rac1AridiaOceanSource.Read(disc, level);
        Assert.Equal(7, records.Count);
        Assert.Equal(
            [0x001f3c00u, 0x001f3c60u, 0x001f3cd0u, 0x001f3d30u,
             0x001f3d90u, 0x001f3dd0u, 0x001f3e10u],
            records.Select(r => r.SourceAddress));
        Assert.Equal([84, 42, 42, 42, 21, 14, 7], records.Select(r => r.GridColumns));
        Assert.Equal([70, 28, 42, 35, 42, 7, 7], records.Select(r => r.GridRows));
        Assert.InRange(records[0].Height, 24.7f, 24.8f);
        Assert.InRange(records[5].Height, 4.7f, 4.9f);
        Assert.All(records, r => Assert.Equal((uint)0x001f3e80, r.AnimatedFramePacket));
        Assert.Equal([57, 8, 31, 20, 17, 2, 1],
            records.Select(r => r.CellMask.Count(flag => flag == 1)));
        Assert.Equal(136, records.Sum(r => r.CellMask.Count(flag => flag == 1)));
        Assert.All(records, r => Assert.Equal(r.CellColumns * r.CellRows, r.CellMask.Length));

        var overlay = Rac1LevelOverlay.Open(disc, level);
        var cellPackets = records.SelectMany(record =>
            Rac1AridiaOceanSource.ReadCellPackets(overlay, record)).ToArray();
        Assert.Equal(231, cellPackets.Length); // 120+24+36+30+18+2+1 native cells
        Assert.All(cellPackets, packet => Assert.Equal(0x7c * 4, packet.PackedValues.Length));
        Assert.Contains(cellPackets, packet => packet.PackedValues.Any(value => value != 0));

        var core = Rac1LevelCore.Open(disc, level);
        var frames = Rac1FxTextures.Read(core, Enumerable.Range(
            Rac1AridiaOceanSource.AnimatedFxStart,
            Rac1AridiaOceanSource.AnimatedFxFrameCount).ToHashSet());
        Assert.Equal(64, frames.Count);
        Assert.All(frames.Values, frame =>
        {
            Assert.Equal("fx", frame.AssetKind);
            Assert.Equal(64, frame.Width);
            Assert.Equal(64, frame.Height);
        });
    }
}
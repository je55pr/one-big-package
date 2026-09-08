using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.Tests.Helpers;

namespace OBP.Tests;

/// <summary>
/// Ported 1:1 from <c>reference-ts/tests/wad-lz.test.mjs</c> — the packet-stream
/// vectors and expected outputs are the shared equivalence oracle. Plus a
/// retail-ISO check whose expected SHA-256s were produced by the TypeScript
/// <c>readWadLz</c> on the same blocks.
/// </summary>
public class WadLzTests
{
    /// <summary>Wrap a raw packet stream in a valid 16-byte WAD LZ container header.</summary>
    private static byte[] Container(byte[] body, string name = "", int? compressedSizeOverride = null)
    {
        var header = new byte[WadLz.HeaderSize];
        header[0] = 0x57;
        header[1] = 0x41;
        header[2] = 0x44;
        int size = compressedSizeOverride ?? WadLz.HeaderSize + body.Length;
        header[3] = (byte)(size & 0xff);
        header[4] = (byte)((size >> 8) & 0xff);
        header[5] = (byte)((size >> 16) & 0xff);
        header[6] = (byte)((size >> 24) & 0xff);
        for (int i = 0; i < name.Length && i < 9; i++)
        {
            header[7 + i] = (byte)name[i];
        }

        return header.Concat(body).ToArray();
    }

    private static byte[] Fill(int count, byte value) => Enumerable.Repeat(value, count).ToArray();

    [Fact]
    public void SmallAndBigLiteralPackets()
    {
        // flag 0x05 -> literal run of 0x05 + 3 = 8 bytes.
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            WadLz.Decompress(Container([0x05, 1, 2, 3, 4, 5, 6, 7, 8])).Data);

        // flag 0x00 -> big literal, size = next + 18.
        var big = WadLz.Decompress(Container([0x00, 0x02, .. Fill(20, 0xab)])).Data;
        Assert.Equal(20, big.Length);
        Assert.All(big, b => Assert.Equal(0xab, b));
    }

    [Fact]
    public void LittleMatchPacketCopiesWithOverlapPlusTrailingInlineLiteral()
    {
        var outp = WadLz.Decompress(Container([0x01, 65, 66, 67, 68, 0x62, 0x00, 88, 89])).Data;
        Assert.Equal("ABCDDDDDXY", new string(outp.Select(b => (char)b).ToArray()));
    }

    [Fact]
    public void MediumMatchPacket()
    {
        var outp = WadLz.Decompress(Container([0x05, 0, 1, 2, 3, 4, 5, 6, 7, 0x21, 0x04, 0x00])).Data;
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 6, 7, 6 }, outp);
    }

    [Fact]
    public void FarMatchPaddingSyncEndsThePacketAndSkipsToA0x1000Boundary()
    {
        byte[] body = [0x01, 1, 2, 3, 4, 0x12, 0x00, 0x00, .. Fill(0x1000 - 8, 0)];
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, WadLz.Decompress(Container(body)).Data);
    }

    [Fact]
    public void TwoLiteralPacketsInARowAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WadLz.Decompress(Container([0x01, 1, 2, 3, 4, 0x01, 5, 6, 7, 8])));
    }

    [Fact]
    public void AMatchPointingBeforeTheBufferIsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WadLz.Decompress(Container([0x01, 1, 2, 3, 4, 0x62, 0xff, 0x00])));
    }

    [Fact]
    public void TruncatedStreamIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Container([0x05, 1, 2, 3])));
    }

    [Fact]
    public void OutputCapIsEnforced()
    {
        Assert.Equal(273, WadLz.Decompress(Container([0x00, 0xff, .. Fill(273, 1)])).Data.Length);
        Assert.Throws<InvalidDataException>(() =>
            WadLz.Decompress(Container([0x00, 0xff, .. Fill(273, 1)]), maxOutputBytes: 100));
    }

    [Fact]
    public void HeaderParsingAndMagicDetection()
    {
        var c = Container([0x01, 1, 2, 3, 4], name: "chunkcoll");
        Assert.True(WadLz.IsWadLz(c));
        Assert.False(WadLz.IsWadLz(new byte[] { 1, 2, 3, 4 }));

        var h = WadLz.ReadHeader(c);
        Assert.Equal("chunkcoll", h.Name);
        Assert.Equal(WadLz.HeaderSize + 5, h.CompressedSize);

        Assert.Throws<InvalidDataException>(() =>
            WadLz.ReadHeader(new byte[] { 0x58, 0x41, 0x44, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void ReadBlockReadsOnlyTheBlocksOwnBytesFromAReader()
    {
        var block = Container([0x05, 9, 9, 9, 9, 9, 9, 9, 9]);
        var padded = new byte[block.Length + 4096];
        block.CopyTo(padded, 128);
        var reader = new RecordingReader(new InMemoryReader("level.wad", padded));

        var result = WadLz.ReadBlock(reader, 128);
        Assert.Equal(Fill(8, 9), result.Data);
        Assert.All(reader.Reads, r => Assert.True(r.Offset >= 128 && r.Offset + r.Length <= 128 + block.Length));
    }

    // Retail equivalence. Expected SHA-256s come from the TypeScript readWadLz on
    // the same LEVEL<n>.WAD lump offsets. Skipped unless OBP_GC_ISO is set.
    [SkippableTheory]
    [InlineData("/G/LEVEL0.WAD", 14784512, 0, 1872256, "9ad3cc5555f63cacb60a00cb72660dc3d5420b6626741f348248e6f1c8bae9c2")]
    [InlineData("/G/LEVEL1.WAD", 17072128, 0, 2006400, "3f4bc4db27f26f9e5d91953aece16c0c2ae94115b17c0c73cfd00ebbf911371f")]
    [InlineData("/G/LEVEL1.WAD", 17901568, 16, 1247440, "1c0d9335fbe51f7cd5dd3bb09ff8a0b1f1e884152f05ab71e1c6fd0a37496e8e")]
    [InlineData("/G/LEVEL1.WAD", 17901568, 770640, 2836912, "a5c257465df3c02c31e6c9b1c421908baacaad8d8920e705831654fdf2f309df")]
    public void RetailWadLzBlocksDecompressToTheSameBytesAsTypeScript(string wadPath, long lumpOffset, long blockOffset, int expectedSize, string expectedSha256)
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile(wadPath) ?? throw new FileNotFoundException(wadPath);
        var lump = new SubRangeReader(wad, lumpOffset, wad.Length - lumpOffset, wadPath);

        var result = WadLz.ReadBlock(lump, blockOffset);
        Assert.Equal(expectedSize, result.Data.Length);
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(result.Data)).ToLowerInvariant());
    }
}

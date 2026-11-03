using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Audio;
using OBP.PS2.Compression;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.PS2.Textures;
using OBP.PS2.Vif;
using OBP.Tests.Helpers;

namespace OBP.Tests;

/// <summary>
/// Synthetic-only hostile-input regressions for the bounded IO layer and the
/// shared container/codec readers: truncated, zero-length, oversized-length and
/// misaligned inputs must produce a bounded, typed failure — never an
/// out-of-range index, an unbounded loop or a silent wrap-around.
/// </summary>
public sealed class MalformedInputIoAndContainerTests
{
    private static InMemoryReader Mem(params byte[] bytes) => new("synthetic", bytes);

    // ---------------------------------------------------------------- OBP.IO

    [Theory]
    [InlineData(long.MaxValue, 1)]
    [InlineData(1, long.MaxValue)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void SubRangeReaderRejectsOverflowingWindows(long start, long length)
    {
        var parent = Mem(new byte[16]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubRangeReader(parent, start, length));
    }

    [Fact]
    public void SubRangeReaderZeroLengthWindowReadsNothingAndRejectsAnyByte()
    {
        var window = new SubRangeReader(Mem(new byte[16]), 16, 0);
        window.Read(0, Span<byte>.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Read(0, new byte[1]));
    }

    [Theory]
    [InlineData(long.MaxValue, 1)]
    [InlineData(8, long.MaxValue)]
    [InlineData(17, 0)]
    public void ValidateRangeNeverWrapsOnHugeOffsetsOrLengths(long offset, long length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RandomAccessReaderExtensions.ValidateRange("x", 16, offset, length));

    [Fact]
    public void FileReaderHandlesEmptyFileAndRejectsOutOfRangeReads()
    {
        string path = Path.Combine(Path.GetTempPath(), "obp-empty-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, []);
        try
        {
            using var reader = new FileRandomAccessReader(path);
            Assert.Equal(0, reader.Length);
            reader.Read(0, Span<byte>.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(0, new byte[1]));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(long.MaxValue, new byte[1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ----------------------------------------------------------------- WAD LZ

    private static byte[] WadHeader(int compressedSize, int totalLength)
    {
        var bytes = new byte[totalLength];
        bytes[0] = (byte)'W';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'D';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(3), compressedSize);
        return bytes;
    }

    [Fact]
    public void WadLzRejectsEmptyTruncatedAndMislabelledHeaders()
    {
        Assert.False(WadLz.IsWadLz([]));
        Assert.Throws<InvalidDataException>(() => WadLz.ReadHeader(new byte[8]));
        Assert.Throws<InvalidDataException>(() => WadLz.ReadHeader(new byte[16]));
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(WadHeader(compressedSize: 4, totalLength: 16)));
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(WadHeader(compressedSize: -1, totalLength: 16)));
    }

    [Fact]
    public void WadLzRejectsCompressedSizeBeyondSource()
    {
        var oversized = WadHeader(compressedSize: int.MaxValue, totalLength: 32);
        Assert.False(WadLz.IsWadLz(oversized));
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(oversized));
        Assert.Throws<InvalidDataException>(() => WadLz.ReadBlock(Mem(oversized), 0));
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(-1)]
    [InlineData(31)]
    public void WadLzReadBlockRejectsBadOffsets(long offset) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.ReadBlock(Mem(WadHeader(16, 32)), offset));

    [Fact]
    public void WadLzTruncatedPacketsFailInsteadOfReadingPastTheStream()
    {
        // Literal packet (flag 0x01 => 4 literal bytes) with only one byte left.
        var truncatedLiteral = WadHeader(compressedSize: 18, totalLength: 18);
        truncatedLiteral[16] = 0x01;
        truncatedLiteral[17] = 0xAA;
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(truncatedLiteral));

        // Match packet (flag 0x40) missing its operand byte.
        var truncatedMatch = WadHeader(compressedSize: 17, totalLength: 17);
        truncatedMatch[16] = 0x40;
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(truncatedMatch));
    }

    [Fact]
    public void WadLzMatchBeforeAnyOutputIsRejected()
    {
        var block = WadHeader(compressedSize: 18, totalLength: 18);
        block[16] = 0x40; // short match, nothing in the window yet
        block[17] = 0x00;
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(block));
    }

    [Fact]
    public void WadLzEnforcesOutputCap()
    {
        // 4 literal bytes (flag 0x01) decompress to 4 bytes, which exceeds a cap of 2.
        var block = WadHeader(compressedSize: 21, totalLength: 21);
        block[16] = 0x01;
        block[17] = 1;
        block[18] = 2;
        block[19] = 3;
        block[20] = 4;
        Assert.Equal(4, WadLz.Decompress(block).Data.Length);
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(block, maxOutputBytes: 2));
    }

    // ---------------------------------------------------------------- ISO-9660

    [Fact]
    public void IsoWithoutDescriptorSectorsFailsCleanly()
    {
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem()));
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(new byte[16 * 2048])));
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(new byte[24 * 2048])));
    }

    [Fact]
    public void IsoDescriptorEndianCopiesMustAgree()
    {
        byte[] bytes = SyntheticPs2.Build("SCUS_971.99").Bytes;
        bytes[16 * 2048 + 84] ^= 0xFF; // corrupt the big-endian volume-space copy
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(bytes)));
    }

    [Fact]
    public void IsoShortDirectoryRecordIsRejected()
    {
        byte[] bytes = SyntheticPs2.Build("SCUS_971.99").Bytes;
        bytes[20 * 2048 + 68] = 20; // SYSTEM.CNF record length below the 34-byte minimum
        var fs = Iso9660Filesystem.Open(Mem(bytes));
        Assert.Throws<InvalidDataException>(() => fs.List("/"));
    }

    [Fact]
    public void IsoDirectoryExtentBeyondSourceIsRejectedWithoutReading()
    {
        byte[] bytes = SyntheticPs2.Build("SCUS_971.99").Bytes;
        int record = 16 * 2048 + 156;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(record + 10), 0x7FFF_F800);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 14), 0x7FFF_F800);
        var fs = Iso9660Filesystem.Open(Mem(bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.List("/"));
    }

    [Fact]
    public void IsoFileExtentBeyondSourceIsRejected()
    {
        byte[] bytes = SyntheticPs2.Build("SCUS_971.99").Bytes;
        int cnfRecord = 20 * 2048 + 68;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cnfRecord + 2), uint.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(cnfRecord + 6), uint.MaxValue);
        var fs = Iso9660Filesystem.Open(Mem(bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.OpenFile("SYSTEM.CNF"));
    }

    [Theory]
    [InlineData("../SYSTEM.CNF")]
    [InlineData("a/./b")]
    [InlineData("a\0b")]
    public void IsoPathsCannotTraverseOrCarryNul(string path) =>
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.NormalizePath(path));

    [Fact]
    public void BootRejectsOversizedSystemCnfBeforeReadingIt() =>
        Assert.Throws<InvalidDataException>(() => Ps2Boot.OpenDisc(SyntheticPs2.Reader("SCUS_971.99"), maxSystemCnfBytes: 4));

    [Fact]
    public void SystemCnfParserIsLinearOnHugeWhitespaceRuns()
    {
        string hostile = "BOOT2=" + new string(' ', 60_000) + "x\n" + "K=" + new string(' ', 60_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var parsed = Ps2Boot.ParseSystemCnf(hostile);
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"SYSTEM.CNF parse took {watch.Elapsed}.");
        Assert.Equal("x", parsed["BOOT2"]);
        Assert.Equal("", parsed["K"]);
    }

    // ------------------------------------------------------------------- ELF32

    private static byte[] ElfHeader(uint phOffset, ushort phEntrySize, ushort phCount, int totalLength = 52)
    {
        var b = new byte[totalLength];
        b[0] = 0x7f; b[1] = 0x45; b[2] = 0x4c; b[3] = 0x46;
        b[4] = 1; b[5] = 1; b[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), phOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(40), 52);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(42), phEntrySize);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(44), phCount);
        return b;
    }

    [Fact]
    public void ElfRejectsTruncatedBadMagicAndOutOfRangeTables()
    {
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem()));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(new byte[51])));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(new byte[64])));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Elf32Reader.ReadHeader(Mem(ElfHeader(0xFFFF_FF00, 32, 1))));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Elf32Reader.ReadHeader(Mem(ElfHeader(52, 0xFFFF, 0xFFFF))));
        Assert.Throws<InvalidDataException>(() =>
            Elf32Reader.ReadHeader(Mem(ElfHeader(52, 16, 1, totalLength: 128))));
    }

    [Fact]
    public void ElfProgramHeaderCountIsCapped()
    {
        var reader = Mem(ElfHeader(52, 32, 300, totalLength: 52 + 32 * 300));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadProgramHeaders(reader));
    }

    [Fact]
    public void ElfProgramSegmentBeyondFileIsRejected()
    {
        byte[] bytes = ElfHeader(52, 32, 1, totalLength: 84);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52 + 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52 + 16), 0x100);
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadProgramHeaders(Mem(bytes)));
    }

    [Fact]
    public void ElfVirtualRangeLengthCannotOverflowTheAddressSpaceCheck()
    {
        var load = new Elf32ProgramHeader(0, Elf32Reader.ProgramTypeLoad, 0, 0, 0, 0x1000, 0x1000, 0, 0);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Elf32Reader.MapVirtualRange([load], virtualAddress: 1, length: long.MaxValue));
        Assert.Contains("Invalid ELF32 virtual range", ex.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Elf32Reader.MapVirtualRange([load], virtualAddress: 0xFFFF_FFFF, length: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Elf32Reader.MapVirtualRange([load], virtualAddress: -1, length: 1));
        Assert.Empty(Elf32Reader.ReadVirtualRange(Mem(new byte[0x1000]), [load], 0x10, 0));
    }

    // -------------------------------------------------------- VAGp / SPU ADPCM

    private static byte[] Vag(int dataSize, int sampleRate, int totalLength)
    {
        var b = new byte[totalLength];
        "VAGp"u8.CopyTo(b);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0x0c), dataSize);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0x10), sampleRate);
        return b;
    }

    [Fact]
    public void VagpRejectsTruncatedMisalignedAndOversizedStreams()
    {
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem()));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(new byte[0x2f])));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(new byte[0x40])));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(0, 44100, 0x40))));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(-16, 44100, 0x40))));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(17, 44100, 0x60))));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(16, 0, 0x40))));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(int.MaxValue - 15, 44100, 0x40))));
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(32, 44100, 0x40))));
        Assert.Equal(16, Vagp.Open(Mem(Vag(16, 44100, 0x40))).Header.DataSize);
    }

    [Fact]
    public void SpuAdpcmRejectsEmptyMisalignedAndInvalidFrames()
    {
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames([]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(new byte[15]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(new byte[17]));
        var badFilter = new byte[16];
        badFilter[0] = 0x50;
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(badFilter));
        var badShift = new byte[16];
        badShift[0] = 0x0D;
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(badShift));
    }

    [Fact]
    public void SpuAdpcmAnalysisStopsAtTheRangeAndNeverReadsPartialFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ps2SpuAdpcm.AnalyzeSample(new byte[16], 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ps2SpuAdpcm.AnalyzeSample(new byte[16], -1));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample([]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(new byte[31]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(new byte[32])); // no END flag
        var ended = new byte[32];
        ended[17] = (byte)Ps2SpuAdpcm.FrameFlags.End;
        Assert.Equal(2, Ps2SpuAdpcm.AnalyzeSample(ended).FrameCount);
    }

    // --------------------------------------------------------------------- VIF

    [Fact]
    public void VifCommandListStopsOnTruncationAndZeroLength()
    {
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>([])));
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(new byte[3])));

        // UNPACK V4-32 with 256 elements needs 4 KiB of payload but only a word is present.
        var truncated = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated, 0x6C00_0000u);
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(truncated)));

        // DIRECT with the maximum quadword count exceeds the packet-size cap rather than overflowing.
        var direct = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(direct, 0x5000_0000u);
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(direct)));

        // NOP words always advance, and the packet cap bounds the work.
        var nops = new byte[4 * 64];
        Assert.Equal(10, Vif.ReadCommandList(new ArraySegment<byte>(nops), maxPackets: 10).Count);
    }

    // ----------------------------------------------------------------- Textures

    [Fact]
    public void TextureDecodeRejectsBadDimensionsAndShortPixelBuffers()
    {
        var palette = new uint[256];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(0, 4, new byte[16], palette)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(65535, 65535, new byte[16], palette)));
        Assert.Throws<InvalidDataException>(() =>
            Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(4, 4, new byte[15], palette)));
        Assert.Equal(64, Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(4, 4, new byte[16], new uint[3])).Rgba.Length);
    }

    [Fact]
    public void ShortPalettesAreRejectedAsArgumentErrorsNotIndexErrors() =>
        Assert.Throws<ArgumentException>(() => Ps2Texture.SwizzlePalette(new uint[16]));

    [Fact]
    public void LevelTextureTableRejectsOverflowingRangesAndPixelOffsets()
    {
        var index = new byte[64];
        Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(
            index, new byte[16], new byte[0x400], 0, new RcLevelTextureTable.Range(int.MaxValue, 16)));
        Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(
            index, new byte[16], new byte[0x400], 0, new RcLevelTextureTable.Range(-1, 0)));
        Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(
            index, new byte[16], new byte[0x400], 17, new RcLevelTextureTable.Range(0, 0)));

        // One entry whose data offset is int.MaxValue must be rejected, not wrapped.
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0), int.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(index.AsSpan(4), 4);
        BinaryPrimitives.WriteInt16LittleEndian(index.AsSpan(6), 4);
        Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(
            index, new byte[16], new byte[0x400], 8, new RcLevelTextureTable.Range(1, 0)));
        Assert.Empty(RcLevelTextureTable.Read(
            index, new byte[16], new byte[0x400], 8, new RcLevelTextureTable.Range(1, 0),
            new RcLevelTextureTable.Options(Strict: false)));
    }
}

using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Compression;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.Tests.Helpers;

namespace OBP.Tests;

/// <summary>
/// Truncated, zero-length, oversized-length and misaligned synthetic inputs for
/// the bounded IO layer, ISO-9660, ELF32, PS2 boot and WAD LZ readers. Every
/// fixture is generated in memory; no retail bytes are involved.
/// </summary>
public sealed class MalformedInputIoTests
{
    private static InMemoryReader Mem(byte[] bytes, string name = "mem") => new(name, bytes);

    // ---------------------------------------------------------------- IO ----

    [Fact]
    public void ReadExtensionValidatesBeforeAllocating()
    {
        var reader = Mem(new byte[16]);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(0, -1));
        // A hostile length must be rejected by the range check, not by an allocation attempt.
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(8, 9));
        Assert.Empty(reader.Read(16, 0));
    }

    [Theory]
    [InlineData(0L, 0L, 0L, true)]
    [InlineData(10L, 0L, 10L, true)]
    [InlineData(10L, 10L, 0L, true)]
    [InlineData(10L, 11L, 0L, false)]
    [InlineData(10L, 0L, 11L, false)]
    [InlineData(10L, -1L, 1L, false)]
    [InlineData(10L, 0L, -1L, false)]
    [InlineData(long.MaxValue, long.MaxValue, 1L, false)]
    [InlineData(long.MaxValue, 1L, long.MaxValue, false)]
    [InlineData(long.MaxValue, long.MaxValue - 1, long.MaxValue, false)]
    public void ValidateRangeNeverOverflows(long length, long offset, long count, bool valid)
    {
        if (valid)
        {
            RandomAccessReaderExtensions.ValidateRange("x", length, offset, count);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomAccessReaderExtensions.ValidateRange("x", length, offset, count));
        }
    }

    [Theory]
    [InlineData(-1L, 0L)]
    [InlineData(0L, -1L)]
    [InlineData(17L, 0L)]
    [InlineData(0L, 17L)]
    [InlineData(8L, 9L)]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(1L, long.MaxValue)]
    public void SubRangeReaderRejectsWindowsOutsideItsParent(long start, long length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubRangeReader(Mem(new byte[16]), start, length));
    }

    [Fact]
    public void SubRangeReaderBoundsReadsToItsWindow()
    {
        var parent = Mem(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
        var window = new SubRangeReader(parent, 4, 4);
        Assert.Equal(new byte[] { 4, 5, 6, 7 }, window.Read(0, 4));
        Assert.Empty(window.Read(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Read(1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Read(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Read(5, 0));
        Assert.Equal(0, new SubRangeReader(parent, 16, 0).Length);
    }

    [Fact]
    public void FileReaderHandlesEmptyFilesAndRejectsOutOfRangeReads()
    {
        string path = Path.Combine(Path.GetTempPath(), "obp-empty-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, []);
        try
        {
            using var reader = new FileRandomAccessReader(path);
            Assert.Equal(0, reader.Length);
            Assert.Empty(reader.Read(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(-1, 0));
            Assert.Equal(Hashing.Sha256Hex(Mem([])), Hashing.Sha256Hex(reader));
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hashing.Sha256Hex(reader));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HashingValidatesItsRangeBeforeReading()
    {
        var reader = Mem(new byte[8]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Hashing.Sha256Hex(reader, offset: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hashing.Sha256Hex(reader, offset: 4, length: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hashing.Sha256Hex(reader, offset: 0, length: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hashing.Sha256Hex(reader, offset: long.MaxValue, length: 1));
    }

    [Fact]
    public void AtomicDocumentReadEnforcesItsSizeLimit()
    {
        string path = Path.Combine(Path.GetTempPath(), "obp-doc-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, new byte[32]);
        try
        {
            Assert.Equal(32, AtomicDocumentFile.Read(path, 32).Length);
            Assert.Throws<InvalidDataException>(() => AtomicDocumentFile.Read(path, 31));
            Assert.Throws<InvalidDataException>(() => AtomicDocumentFile.Read(path, -1));
            var rejected = AtomicDocumentFile.Load<string>(path, 32,
                _ => throw new InvalidDataException("bad"), e => e is InvalidDataException or IOException);
            Assert.Equal(DocumentReadKind.Rejected, rejected.Kind);
            Assert.Null(rejected.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --------------------------------------------------------- ISO-9660 ----

    private static byte[] ValidIso() => SyntheticPs2.Build("SCUS_972.68").Bytes;

    private static void PatchBoth16(byte[] b, int at, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), value);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at + 2), value);
    }

    private static void PatchBoth32(byte[] b, int at, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at + 4), value);
    }

    private const int Pvd = 16 * SyntheticPs2.Block;
    private const int RootDir = 20 * SyntheticPs2.Block;

    [Fact]
    public void SyntheticDiscStillOpens()
    {
        var disc = Ps2Boot.OpenDisc(Mem(ValidIso()));
        Assert.Equal("SCUS_972.68", disc.Boot.ExecutableName);
    }

    [Fact]
    public void EmptyAndTinySourcesAreRejectedAsInvalidData()
    {
        foreach (int length in new[] { 0, 1, 2047, 2048, Pvd - 1, Pvd, Pvd + 1 })
        {
            Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(new byte[length])));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(511)]
    [InlineData(3000)]
    [InlineData(4096)]
    [InlineData(32768)]
    [InlineData(65535)]
    public void NonPowerOfTwoOrTinyLogicalBlockSizesAreRejected(int blockSize)
    {
        byte[] bytes = ValidIso();
        PatchBoth16(bytes, Pvd + 128, (ushort)blockSize);
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(bytes)));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    public void ValidPowerOfTwoLogicalBlockSizesAreAccepted(int blockSize)
    {
        byte[] bytes = ValidIso();
        PatchBoth16(bytes, Pvd + 128, (ushort)blockSize);
        Assert.Equal(blockSize, Iso9660Filesystem.Open(Mem(bytes)).Volume.LogicalBlockSize);
    }

    [Fact]
    public void PrimaryDescriptorFieldsAreCrossChecked()
    {
        byte[] badMagic = ValidIso();
        badMagic[Pvd + 1] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(badMagic)));

        byte[] badVersion = ValidIso();
        badVersion[Pvd + 6] = 2;
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(badVersion)));

        byte[] disagreeing = ValidIso();
        BinaryPrimitives.WriteUInt16BigEndian(disagreeing.AsSpan(Pvd + 130), 4096);
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(disagreeing)));

        byte[] fileRoot = ValidIso();
        fileRoot[Pvd + 156 + 25] = 0; // root record loses its directory flag
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(fileRoot)));

        byte[] shortRecord = ValidIso();
        shortRecord[Pvd + 156] = 10;
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(shortRecord)));
    }

    [Fact]
    public void MissingTerminatorFailsWithinTheDescriptorBound()
    {
        byte[] bytes = ValidIso();
        bytes[Pvd] = 2; // supplementary descriptor, not primary
        bytes[17 * SyntheticPs2.Block] = 2; // terminator also removed
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.Open(Mem(bytes)));
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.ReadPrimaryVolumeDescriptor(Mem(bytes), maxDescriptors: -5));
    }

    [Theory]
    [InlineData(0)]   // zero length terminates the block, nothing to parse
    [InlineData(1)]
    [InlineData(33)]  // below the 34-byte minimum
    public void RootDirectoryRecordLengthsBelowTheMinimumAreRejectedOrEndTheBlock(int recordLength)
    {
        byte[] bytes = ValidIso();
        // Corrupt the third record (SYSTEM.CNF) of the root directory.
        int third = RootDir + 34 + 34;
        bytes[third] = (byte)recordLength;
        var fs = Iso9660Filesystem.Open(Mem(bytes));
        if (recordLength == 0)
        {
            Assert.DoesNotContain(fs.List("/")!, e => e.Name == "SYSTEM.CNF");
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => fs.List("/"));
        }
    }

    [Fact]
    public void DirectoryRecordCrossingTheDeclaredExtentIsRejected()
    {
        byte[] shortDirectory = ValidIso();
        // Shrink the root extent so the second 34-byte record no longer fits the declared data length.
        PatchBoth32(shortDirectory, Pvd + 156 + 10, 40);
        var fs = Iso9660Filesystem.Open(Mem(shortDirectory));
        Assert.Throws<InvalidDataException>(() => fs.List("/"));
    }

    [Fact]
    public void ZeroIdentifierLengthIsRejected()
    {
        byte[] bytes = ValidIso();
        bytes[RootDir + 34 + 34 + 32] = 0;
        var fs = Iso9660Filesystem.Open(Mem(bytes));
        Assert.Throws<InvalidDataException>(() => fs.List("/"));
    }

    [Fact]
    public void ExtentsPastTheSourceAreRejectedForDirectoriesAndFiles()
    {
        byte[] hugeDirectory = ValidIso();
        PatchBoth32(hugeDirectory, Pvd + 156 + 10, uint.MaxValue);
        var fs = Iso9660Filesystem.Open(Mem(hugeDirectory));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.List("/"));

        byte[] farDirectory = ValidIso();
        PatchBoth32(farDirectory, Pvd + 156 + 2, uint.MaxValue);
        var fs2 = Iso9660Filesystem.Open(Mem(farDirectory));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs2.List("/"));

        byte[] hugeFile = ValidIso();
        // SYSTEM.CNF record: extent @ +2, length @ +10.
        PatchBoth32(hugeFile, RootDir + 68 + 10, uint.MaxValue);
        var fs3 = Iso9660Filesystem.Open(Mem(hugeFile));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs3.OpenFile("SYSTEM.CNF"));

        byte[] farFile = ValidIso();
        PatchBoth32(farFile, RootDir + 68 + 2, uint.MaxValue);
        var fs4 = Iso9660Filesystem.Open(Mem(farFile));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs4.OpenFile("SYSTEM.CNF"));
    }

    [Fact]
    public void ExtentReaderRejectsDirectoriesAndOutOfRangeReads()
    {
        var fs = Iso9660Filesystem.Open(Mem(ValidIso()));
        var cnf = fs.OpenFile("SYSTEM.CNF")!;
        Assert.Throws<ArgumentOutOfRangeException>(() => cnf.Read(cnf.Length, 1));
        Assert.Empty(cnf.Read(cnf.Length, 0));
        Assert.Throws<InvalidOperationException>(() => fs.OpenFile("/"));
        Assert.Throws<InvalidOperationException>(() => fs.List("SYSTEM.CNF"));
        Assert.Null(fs.OpenFile("NOPE.BIN"));
        Assert.Null(fs.Find("SYSTEM.CNF/child"));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/./b")]
    [InlineData("a\\..\\b")]
    [InlineData("a\0b")]
    public void PathsCannotTraverseOrCarryNulBytes(string path)
    {
        Assert.Throws<InvalidDataException>(() => Iso9660Filesystem.NormalizePath(path));
    }

    [Fact]
    public void EveryTruncationOfAValidDiscFailsWithExpectedExceptionsOnly()
    {
        byte[] bytes = ValidIso();
        var lengths = new SortedSet<int>(Enumerable.Range(0, bytes.Length / 997).Select(i => i * 997)) { bytes.Length - 1 };
        foreach (int boundary in new[] { Pvd, 17 * 2048, RootDir, 21 * 2048, 22 * 2048 })
        {
            foreach (int delta in new[] { -1, 0, 1 })
            {
                lengths.Add(boundary + delta);
            }
        }

        foreach (int length in lengths.Where(l => l >= 0 && l < bytes.Length))
        {
            var ex = Record.Exception(() => Ps2Boot.OpenDisc(Mem(bytes[..length])));
            if (length < 22 * 2048 + 256)
            {
                // Anything that cuts into the boot executable's extent (or earlier) must be rejected.
                Assert.True(
                    ex is InvalidDataException or ArgumentOutOfRangeException or EndOfStreamException,
                    $"Truncation to {length} bytes threw {ex?.GetType().Name ?? "nothing"}: {ex?.Message}");
            }
            else
            {
                Assert.Null(ex);
            }
        }
    }

    [Fact]
    public void SingleByteCorruptionOfTheStructuralRegionsNeverEscapesTheExpectedExceptionSet()
    {
        byte[] original = ValidIso();
        var regions = new[] { (Pvd, 190), (RootDir, 140), (21 * 2048, 48), (22 * 2048, 128) };
        foreach (var (start, length) in regions)
        {
            for (int at = start; at < start + length; at++)
            {
                foreach (byte value in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
                {
                    byte[] copy = (byte[])original.Clone();
                    copy[at] = value;
                    var ex = Record.Exception(() => Ps2Boot.OpenDisc(Mem(copy)));
                    Assert.True(
                        ex is null or InvalidDataException or ArgumentOutOfRangeException or InvalidOperationException,
                        $"Byte {at}=0x{value:x2} threw {ex?.GetType().Name}: {ex?.Message}");
                }
            }
        }
    }

    // ------------------------------------------------------------- boot ----

    [Fact]
    public void OversizedSystemCnfIsRejectedBeforeItIsRead()
    {
        byte[] bytes = ValidIso();
        // SYSTEM.CNF data length -> 1 MiB (still inside the source only because the cap check runs first).
        PatchBoth32(bytes, RootDir + 68 + 10, 1 << 20);
        Assert.Throws<InvalidDataException>(() => Ps2Boot.OpenDisc(Mem(bytes)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cdrom0:")]
    [InlineData("host0:\\SLUS_000.00;1")]
    [InlineData("cdrom0:\\..\\SLUS_000.00;1")]
    public void MalformedBootPathsAreRejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => Ps2Boot.BootPathToIso9660Path(path));
    }

    [Fact]
    public void SystemCnfParsingToleratesGarbageAndLongLines()
    {
        Assert.Empty(Ps2Boot.ParseSystemCnf(""));
        Assert.Empty(Ps2Boot.ParseSystemCnf("\0\0\0\n=\n= x\n"));
        string longLine = "A=" + new string(' ', 60_000) + "x\u0001";
        var parsed = Ps2Boot.ParseSystemCnf(longLine + "\nBOOT2 = cdrom0:\\A.B;1\r\n");
        Assert.Equal("cdrom0:\\A.B;1", parsed["BOOT2"]);
        Assert.Null(Ps2Boot.ParseExecutableSerial(new string('A', 100_000)));
    }

    // -------------------------------------------------------------- ELF ----

    private static byte[] MinimalElf(Action<byte[]>? edit = null, int length = 256)
    {
        var b = new byte[length];
        new byte[] { 0x7f, 0x45, 0x4c, 0x46, 1, 1, 1 }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 52);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(40), 52);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(42), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52), 1);          // PT_LOAD
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(56), 128);        // offset
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(60), 0x00100000); // vaddr
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(68), 16);         // filesz
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(72), 32);         // memsz
        edit?.Invoke(b);
        return b;
    }

    [Fact]
    public void ElfHeaderRejectsShortAndMalformedIdentification()
    {
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem([])));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(new byte[51])));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[0] = 0))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[4] = 2))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[5] = 3))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[6] = 0))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[20] = 2))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[40] = 51))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(Mem(MinimalElf(b => b[42] = 31))));
        Assert.Equal(1, Elf32Reader.ReadHeader(Mem(MinimalElf())).ProgramHeaderCount);
    }

    [Fact]
    public void ElfTablesCannotEscapeTheFile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadHeader(
            Mem(MinimalElf(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 0xffffffff)))));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadHeader(
            Mem(MinimalElf(b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(44), 0xffff)))));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadHeader(
            Mem(MinimalElf(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 250)))));
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadHeader(
            Mem(MinimalElf(b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(48), 2)))));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadHeader(
            Mem(MinimalElf(b =>
            {
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(46), 0xffff);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(48), 0xffff);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), 0xffffffff);
            }))));
    }

    [Fact]
    public void ElfProgramHeadersAreCappedAndRangeChecked()
    {
        var reader = Mem(MinimalElf());
        Assert.Throws<InvalidDataException>(() => Elf32Reader.ReadProgramHeaders(reader, maxProgramHeaders: 0));
        Assert.Single(Elf32Reader.ReadProgramHeaders(reader));

        var pastEnd = Mem(MinimalElf(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(68), 0xffffff00)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadProgramHeaders(pastEnd));
        var offsetPastEnd = Mem(MinimalElf(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(56), 0xffffffff)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadProgramHeaders(offsetPastEnd));
    }

    [Fact]
    public void ElfVirtualRangesNeverOverflowOrEscape()
    {
        var reader = Mem(MinimalElf());
        var headers = Elf32Reader.ReadProgramHeaders(reader);
        Assert.Equal(128, Elf32Reader.MapVirtualRange(headers, 0x00100000, 16).FileOffset);
        Assert.Empty(Elf32Reader.ReadVirtualRange(reader, headers, 0x00100000, 0));

        // Lengths that would wrap a naive virtualAddress + length sum.
        foreach (long length in new[] { long.MaxValue, long.MaxValue - 1, 0x1_0000_0000L, -1L })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(headers, 1, length));
            Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(headers, 0xffffffffL, length));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(headers, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(headers, 0x1_0000_0000L, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(headers, 0x00100000, 17)); // BSS tail
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(headers, 0x00200000, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange([], 0, 0));
    }

    [Fact]
    public void ElfVirtualReadRefusesMappingsLargerThanOneArray()
    {
        // A 3 GiB file-backed segment is valid metadata but cannot be materialised as one managed array.
        var segment = new Elf32ProgramHeader(0, 1, 0, 0x1000, 0x1000, 3_000_000_000L, 3_000_000_000L, 5, 16);
        var reader = new SparseReader(3_100_000_000L);
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadVirtualRange(reader, [segment], 0x1000, 3_000_000_000L));
        long justAboveArrayLimit = (long)Array.MaxLength + 1;
        var boundarySegment = segment with { FileSize = justAboveArrayLimit, MemorySize = justAboveArrayLimit };
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.ReadVirtualRange(reader, [boundarySegment], 0x1000, justAboveArrayLimit));
    }

    [Fact]
    public void EveryElfTruncationFailsWithExpectedExceptionsOnly()
    {
        byte[] bytes = MinimalElf();
        for (int length = 0; length < bytes.Length; length++)
        {
            var reader = Mem(bytes[..length]);
            var ex = Record.Exception(() =>
            {
                var header = Elf32Reader.ReadHeader(reader);
                Elf32Reader.ReadProgramHeaders(reader, header);
            });
            // The complete headers end at 84, but their file-backed segment
            // occupies [128, 144). Removing only trailing padding is valid.
            if (length < 128 + 16)
            {
                Assert.True(ex is InvalidDataException or ArgumentOutOfRangeException, $"length {length}: {ex?.GetType().Name}");
            }
            else
            {
                Assert.Null(ex);
            }
        }
    }

    private sealed class SparseReader(long length) : IRandomAccessReader
    {
        public string Name => "sparse";
        public long Length => length;
        public void Read(long offset, Span<byte> destination)
        {
            RandomAccessReaderExtensions.ValidateRange(Name, Length, offset, destination.Length);
            destination.Clear();
        }
    }

    // ----------------------------------------------------------- WAD LZ ----

    private static byte[] Wad(params byte[] body)
    {
        var bytes = new byte[WadLz.HeaderSize + body.Length];
        bytes[0] = (byte)'W';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'D';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(3), bytes.Length);
        "TEST"u8.CopyTo(bytes.AsSpan(7));
        body.CopyTo(bytes, WadLz.HeaderSize);
        return bytes;
    }

    private static readonly byte[] LiteralAbcd = [0x01, (byte)'A', (byte)'B', (byte)'C', (byte)'D'];

    [Fact]
    public void WadLzDecodesLiteralAndShortMatchPackets()
    {
        var literal = WadLz.Decompress(Wad(LiteralAbcd));
        Assert.Equal("ABCD"u8.ToArray(), literal.Data);
        Assert.Equal("TEST", literal.Name);

        // 0x40 flag: three-byte match one byte back, no trailing literal.
        var match = WadLz.Decompress(Wad([.. LiteralAbcd, 0x40, 0x00]));
        Assert.Equal("ABCDDDD"u8.ToArray(), match.Data);
        Assert.Empty(WadLz.Decompress(Wad()).Data);
    }

    [Fact]
    public void WadLzHeaderIsValidatedWithoutReadingPastIt()
    {
        Assert.False(WadLz.IsWadLz([]));
        Assert.False(WadLz.IsWadLz(new byte[15]));
        Assert.False(WadLz.IsWadLz(new byte[16]));
        var tooBig = Wad(LiteralAbcd);
        BinaryPrimitives.WriteInt32LittleEndian(tooBig.AsSpan(3), int.MaxValue);
        Assert.False(WadLz.IsWadLz(tooBig));
        BinaryPrimitives.WriteInt32LittleEndian(tooBig.AsSpan(3), -1);
        Assert.False(WadLz.IsWadLz(tooBig));
        BinaryPrimitives.WriteInt32LittleEndian(tooBig.AsSpan(3), 15);
        Assert.False(WadLz.IsWadLz(tooBig));

        Assert.Throws<InvalidDataException>(() => WadLz.ReadHeader(new byte[15]));
        Assert.Throws<InvalidDataException>(() => WadLz.ReadHeader(new byte[16]));
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(tooBig));

        // An unterminated 9-byte name must not read beyond the header.
        var unterminated = Wad(LiteralAbcd);
        for (int i = 7; i < 16; i++)
        {
            unterminated[i] = (byte)'N';
        }

        Assert.Equal(9, WadLz.ReadHeader(unterminated).Name.Length);
    }

    [Fact]
    public void WadLzRejectsCompressedSizesBeyondTheSource()
    {
        var bytes = Wad(LiteralAbcd);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(3), bytes.Length + 1);
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(3), int.MinValue);
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(bytes));
    }

    [Fact]
    public void WadLzRejectsTruncatedAndMalformedPackets()
    {
        // Literal run longer than the stream.
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad(0x0f, 1, 2)));
        // Extended literal count byte missing.
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad(0x00)));
        // Two literal packets in a row.
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad([.. LiteralAbcd, .. LiteralAbcd])));
        // Match with no preceding output.
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad(0x40, 0x00)));
        // Match whose operand bytes are cut off.
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad([.. LiteralAbcd, 0x40])));
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad([.. LiteralAbcd, 0x20, 0x00])));
        // Long match looking back past the start of the output.
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad([.. LiteralAbcd, 0x40, 0xff])));
    }

    [Fact]
    public void WadLzOutputCapIsEnforcedAndCannotBeNegative()
    {
        // 0x00 flag + count byte 0xff => a 273-byte literal.
        byte[] big = [0x00, 0xff, .. new byte[273]];
        Assert.Equal(273, WadLz.Decompress(Wad(big)).Data.Length);
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad(big), maxOutputBytes: 272));
        Assert.Throws<InvalidDataException>(() => WadLz.Decompress(Wad(big), maxOutputBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.Decompress(Wad(big), maxOutputBytes: -1));
        Assert.Equal(273, WadLz.Decompress(Wad(big), maxOutputBytes: long.MaxValue).Data.Length);
    }

    [Fact]
    public void WadLzReadBlockBoundsItsOffsetsAndSizes()
    {
        byte[] block = Wad(LiteralAbcd);
        byte[] padded = [.. new byte[8], .. block];
        var reader = Mem(padded);
        Assert.Equal("ABCD"u8.ToArray(), WadLz.ReadBlock(reader, 8).Data);
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.ReadBlock(reader, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.ReadBlock(reader, padded.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.ReadBlock(reader, padded.Length - 15));
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.ReadBlock(reader, long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => WadLz.ReadBlock(reader, long.MaxValue - 4));
        Assert.Throws<InvalidDataException>(() => WadLz.ReadBlock(Mem(padded[..^1]), 8));
    }

    [Fact]
    public void EveryWadLzTruncationAndRandomBodyFailsWithInvalidDataOnly()
    {
        byte[] valid = Wad([.. LiteralAbcd, 0x40, 0x00]);
        for (int length = 0; length < valid.Length; length++)
        {
            var ex = Record.Exception(() => WadLz.Decompress(valid[..length], maxOutputBytes: 1 << 16));
            Assert.IsType<InvalidDataException>(ex);
        }

        // Deterministic pseudo-random packet streams. Decoding may succeed or fail,
        // but must only ever fail with InvalidDataException and must terminate.
        var random = new Random(0x0BADC0DE);
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            var body = new byte[random.Next(0, 96)];
            random.NextBytes(body);
            var ex = Record.Exception(() => WadLz.Decompress(Wad(body), maxOutputBytes: 1 << 16));
            Assert.True(ex is null or InvalidDataException, $"iteration {iteration}: {ex?.GetType().Name}: {ex?.Message}");
        }
    }
}

using OBP.IO;
using OBP.PS2;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public class Ps2Tests
{
    private const int Block = SyntheticPs2.Block;

    [Fact]
    public void Iso9660_ReadsThePvdAndResolvesNestedPathsWithBoundedReads()
    {
        var reader = new RecordingReader(SyntheticPs2.Reader("SCUS_972.68"));
        var fs = Iso9660Filesystem.Open(reader);

        Assert.Equal("RATCHET TEST", fs.Volume.VolumeIdentifier);
        Assert.Equal(2048, fs.Volume.LogicalBlockSize);

        var root = fs.List("/");
        Assert.NotNull(root);
        Assert.Contains(root!, e => e.Name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase));

        var cnf = fs.OpenFile("/SYSTEM.CNF");
        Assert.NotNull(cnf);
        Assert.Equal("SYSTEM.CNF", cnf!.Name);

        // Nothing read past the directory / descriptor blocks + the file extent.
        Assert.All(reader.Reads, r => Assert.True(r.Length <= Block || r.Offset >= 21L * Block));
    }

    [Fact]
    public void Iso9660_RejectsPathTraversalAndMissingFiles()
    {
        var fs = Iso9660Filesystem.Open(SyntheticPs2.Reader("SCUS_972.68"));
        Assert.Throws<InvalidDataException>(() => fs.Find("/../etc/passwd"));
        Assert.Null(fs.Find("/NOPE.BIN"));
    }

    [Fact]
    public void Ps2Boot_IdentifiesSerialAndValidatesAMipsElfThroughExactlyTheseReads()
    {
        var fixture = SyntheticPs2.Build("SCUS_971.99");
        var reader = new RecordingReader(new InMemoryReader("rac1.iso", fixture.Bytes));

        var info = Ps2Boot.ReadBootInfo(reader);

        Assert.Equal("RATCHET TEST", info.VolumeIdentifier);
        Assert.Equal("BOOT2", info.BootKey);
        Assert.Equal("SCUS_971.99;1", info.ExecutableIsoPath);
        Assert.Equal("SCUS_971.99", info.ExecutableName);
        Assert.Equal(fixture.ExecutableLength, info.ExecutableSize);
        Assert.Equal(0x00100008, info.ExecutableEntryPoint);
        Assert.Equal(1, info.ExecutableProgramHeaderCount);
        Assert.Equal("SCUS-97199", info.Serial);
        Assert.Equal("NTSC", info.Config["VMODE"]);

        Assert.Equal(
            new (long, int)[]
            {
                (16L * Block, Block),
                (20L * Block, Block),
                (21L * Block, fixture.ConfigLength),
                (20L * Block, Block),
                (22L * Block, 52),
            },
            reader.Reads);
    }

    [Fact]
    public void Ps2Boot_ExposesProgramHeadersWithoutReadingSegmentPayloads()
    {
        var fixture = SyntheticPs2.Build("SCUS_971.99");
        var reader = new RecordingReader(new InMemoryReader("rac1.iso", fixture.Bytes));

        var disc = Ps2Boot.OpenDisc(reader);
        Assert.Equal(8, disc.BootExecutableHeader.Machine);

        var phs = Ps2Boot.ReadBootProgramHeaders(disc);
        Assert.Single(phs);
        Assert.Equal((128L, 16L, 32L), (phs[0].Offset, phs[0].FileSize, phs[0].MemorySize));

        Assert.Equal((22L * Block + 52, 32), reader.Reads[^1]);
        Assert.DoesNotContain(reader.Reads, r => r.Offset == 22L * Block + 128);
    }

    [Fact]
    public void Ps2Boot_RejectsMissingTargetsAndNonMipsExecutables()
    {
        Assert.Throws<InvalidDataException>(() =>
            Ps2Boot.ReadBootInfo(SyntheticPs2.Reader("SCUS_971.99", new(IncludeBootExecutable: false))));
        Assert.Throws<InvalidDataException>(() =>
            Ps2Boot.ReadBootInfo(SyntheticPs2.Reader("SCUS_971.99", new(ElfMachine: 3))));
        Assert.Throws<InvalidDataException>(() =>
            Ps2Boot.ReadBootInfo(SyntheticPs2.Reader("SCUS_971.99", new(ElfType: 3))));
    }

    [Theory]
    [InlineData("SCUS_971.99", "SCUS-97199")]
    [InlineData("SCUS_972.68", "SCUS-97268")]
    [InlineData("SCUS_973.53", "SCUS-97353")]
    [InlineData("not-a-ps2-serial", null)]
    public void Ps2Boot_NormalisesTrilogyExecutableNamesToAuthoritySerials(string name, string? expected)
    {
        Assert.Equal(expected, Ps2Boot.ParseExecutableSerial(name));
    }

    [Theory]
    [InlineData("cdrom0:\\SCUS_972.68;1", "SCUS_972.68;1")]
    [InlineData("CDROM0:/BOOT/SCUS_973.53;1", "BOOT/SCUS_973.53;1")]
    public void Ps2Boot_NormalisesCdrom0Paths(string bootPath, string expected)
    {
        Assert.Equal(expected, Ps2Boot.BootPathToIso9660Path(bootPath));
    }

    [Theory]
    [InlineData("host0:\\SCUS_971.99")]
    [InlineData("cdrom0:\\..\\SCUS_971.99")]
    public void Ps2Boot_RejectsUnsupportedDevicesAndTraversal(string bootPath)
    {
        Assert.Throws<InvalidDataException>(() => Ps2Boot.BootPathToIso9660Path(bootPath));
    }

    [Fact]
    public void Elf32_MapsFileBackedVirtualRangesAndRejectsBss()
    {
        var reader = new InMemoryReader("boot.elf", SyntheticPs2.Build("SCUS_972.68").Bytes[(22 * Block)..(22 * Block + 256)]);
        var phs = Elf32Reader.ReadProgramHeaders(reader);

        var mapped = Elf32Reader.ReadVirtualRange(reader, phs, 0x00100000, 8);
        Assert.Equal(reader.Read(128, 8), mapped);

        // vaddr+16..+32 is memsz-only (BSS).
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(phs, 0x00100000 + 20, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Elf32Reader.MapVirtualRange(phs, 0x00200000, 4));
    }
}

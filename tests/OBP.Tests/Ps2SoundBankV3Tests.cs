using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public sealed class Ps2SoundBankV3Tests
{
    [Fact]
    public void CompactBodyOffsetsSignedParametersAndOriginalOpcodeSurviveParsing()
    {
        byte[] bytes = Bank();
        var bank = Ps2SoundBank989.Open(new InMemoryReader("synthetic-v3", bytes));
        Assert.Equal(3, bank.Version);
        var grains = Assert.Single(bank.Sounds).Grains;
        Assert.Equal(0x1f7e7300u, grains[0].PackedOpcode);
        Assert.Equal(new short[] { 0, 115, 126 }, Enumerable.Range(0, 3).Select(i =>
            BinaryPrimitives.ReadInt16LittleEndian(grains[0].Parameters!.AsSpan(i * 2))));
        Assert.Equal(-1, grains[1].Tone!.Volume);
        Assert.Equal(-60, grains[1].Tone!.CenterNote);
        Assert.Equal(48, grains[1].Delay);
        Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(grains[2].Parameters!));
        Assert.Equal(-5, BinaryPrimitives.ReadInt16LittleEndian(grains[3].Parameters!));
        Assert.Equal(new[] { 0 }, bank.SampleOffsets);
        Assert.Equal(28, bank.DecodeTone(grains[1].Tone!).Samples.Length);
    }

    [Theory]
    [InlineData(24 + 0x34, 0x50)] // GrainData overlaps the compact grain stream.
    [InlineData(24 + 0x54, 0x0100ffff)] // Body offset escapes its metadata chunk.
    [InlineData(24 + 0x48, 8)] // Sound's declared grains exceed the global count.
    [InlineData(24 + 0x48, 1)] // Misaligned first grain.
    [InlineData(24 + 0x80, 1)] // Tone sample is not ADPCM aligned.
    public void CompactRangesCannotEscapeTheirOwningChunks(int at, int value)
    {
        byte[] bytes = Bank();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        Assert.Throws<InvalidDataException>(() => Ps2SoundBank989.Open(new InMemoryReader("bad-v3", bytes)));
    }

    [Fact]
    public void UnrecoveredVersionIsNotSilentlyReadAsV1OrV3()
    {
        byte[] bytes = Bank();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 2);
        Assert.Throws<NotSupportedException>(() => Ps2SoundBank989.Open(new InMemoryReader("v2", bytes)));
    }

    private static byte[] Bank()
    {
        const int metadata = 24, size = 0x90, samples = metadata + size;
        var bytes = new byte[samples + 16];
        void I32(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        void I16(int at, short value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(at), value);
        I32(0, 3); I32(4, 2); I32(8, metadata); I32(12, size); I32(16, samples); I32(20, 16);
        "SBlk"u8.CopyTo(bytes.AsSpan(metadata));
        I32(metadata + 4, 3); I16(metadata + 0x16, 1); I16(metadata + 0x18, 4); I16(metadata + 0x1a, 1);
        I32(metadata + 0x1c, 0x40); I32(metadata + 0x20, 0x4c); I32(metadata + 0x28, 16); I32(metadata + 0x34, 0x70);
        bytes[metadata + 0x40] = 127; bytes[metadata + 0x44] = 4;
        I32(metadata + 0x4c, 0x1f7e7300); I32(metadata + 0x54, 0x01000000); I32(metadata + 0x58, 48);
        I32(metadata + 0x5c, 0x1a000005); I32(metadata + 0x64, 0x1b0000fb);
        bytes[metadata + 0x71] = 255; bytes[metadata + 0x72] = unchecked((byte)-60);
        bytes[samples] = 0x0c; bytes[samples + 1] = (byte)Ps2SpuAdpcm.FrameFlags.End;
        return bytes;
    }
}

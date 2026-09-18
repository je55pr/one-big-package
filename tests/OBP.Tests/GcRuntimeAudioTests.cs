using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.RAC2.Audio;
using OBP.Runtime.Audio;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public sealed class GcRuntimeAudioTests
{
    [Fact]
    public void LevelMusicDecodesNativePairWithoutInventingLoopPolicy()
    {
        var wad = GcLevelAudioWad.Open(new InMemoryReader(
            "AUDIO-test.WAD",
            MakeAudioWad()));

        RuntimeAudioPlaybackIntent intent = GcRuntimeAudio.DecodeLevelMusic(wad, 0);

        Assert.Equal(RuntimeAudioCategory.Music, intent.Category);
        Assert.False(intent.Loop);
        Assert.False(intent.IsPositional);
        Assert.Equal(44_100, intent.Clip.SampleRateHz);
        Assert.Equal(2, intent.Clip.ChannelCount);
        Assert.Equal(28, intent.Clip.FrameCount);
        Assert.Equal((short)1, intent.Clip.InterleavedPcm16[0]);
        Assert.Equal((short)2, intent.Clip.InterleavedPcm16[1]);
    }

    [Fact]
    public void RepresentativeOneShotUsesSelfDescribingUpgradeVag()
    {
        var wad = GcLevelAudioWad.Open(new InMemoryReader(
            "AUDIO-test.WAD",
            MakeAudioWad()));

        RuntimeAudioClip clip = GcRuntimeAudio.DecodeRepresentativeOneShot(wad);

        Assert.Equal(22_050, clip.SampleRateHz);
        Assert.Equal(1, clip.ChannelCount);
        Assert.Equal(28, clip.FrameCount);
        Assert.Equal((short)3, clip.InterleavedPcm16[0]);
    }

    [Fact]
    public void StereoPairRejectsMismatchedNativeRates()
    {
        var left = Vagp.Open(new InMemoryReader(
            "left.vag",
            MakeVag("left", 44_100, 1, repeat: true)));
        var right = Vagp.Open(new InMemoryReader(
            "right.vag",
            MakeVag("right", 48_000, 2, repeat: true)));

        Assert.Throws<InvalidDataException>(
            () => GcRuntimeAudio.DecodeStereoPair(left, right));
    }

    private static byte[] MakeAudioWad()
    {
        var bytes = new byte[7 * 2048];
        I32(bytes, 0x00, GcLevelAudioWad.HeaderSize);
        Range(bytes, 0x08, sector: 3, size: 64);
        Range(bytes, 0x10, sector: 4, size: 64);
        Range(bytes, 0x1000, sector: 5, size: 64);

        MakeVag("left", 44_100, 1, repeat: true).CopyTo(bytes, 3 * 2048);
        MakeVag("right", 44_100, 2, repeat: true).CopyTo(bytes, 4 * 2048);
        MakeVag("upgrade", 22_050, 3, repeat: false).CopyTo(bytes, 5 * 2048);
        return bytes;
    }

    private static byte[] MakeVag(
        string name,
        int sampleRate,
        byte sampleNibble,
        bool repeat)
    {
        var bytes = new byte[64];
        "VAGp"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x04, 4), 0x20);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x0c, 4), 16);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x10, 4), sampleRate);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0x20);

        bytes[0x30] = 0x0c;
        bytes[0x31] = (byte)(Ps2SpuAdpcm.FrameFlags.End
            | (repeat ? Ps2SpuAdpcm.FrameFlags.Repeat : 0));
        byte packed = (byte)(sampleNibble | (sampleNibble << 4));
        for (int i = 0x32; i < 0x40; i++)
        {
            bytes[i] = packed;
        }

        return bytes;
    }

    private static void Range(byte[] bytes, int at, int sector, int size)
    {
        I32(bytes, at, sector);
        I32(bytes, at + 4, size);
    }

    private static void I32(byte[] bytes, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
}

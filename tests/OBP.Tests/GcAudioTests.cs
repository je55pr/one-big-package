using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.PS2.Iso;
using OBP.RAC2.Audio;
using OBP.RAC2.Level;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public class GcAudioTests
{
    [Fact]
    public void SpuAdpcm_DecodesFramesAndPreservesLoopMetadata()
    {
        var encoded = new byte[32];
        encoded[0] = 0x0c;
        encoded[1] = (byte)(Ps2SpuAdpcm.FrameFlags.LoopStart | Ps2SpuAdpcm.FrameFlags.Repeat);
        for (int i = 2; i < 16; i++)
        {
            encoded[i] = 0x1f;
        }

        encoded[16] = 0x0c;
        encoded[17] = (byte)(Ps2SpuAdpcm.FrameFlags.End | Ps2SpuAdpcm.FrameFlags.Repeat);

        var decoded = Ps2SpuAdpcm.DecodeSample(encoded);
        Assert.Equal(32, decoded.Info.EncodedBytes);
        Assert.Equal(2, decoded.Info.FrameCount);
        Assert.Equal(56, decoded.Info.PcmSampleCount);
        Assert.Equal(0, decoded.Info.LoopStartSample);
        Assert.Equal(56, decoded.Info.LoopEndSample);
        Assert.True(decoded.Info.Repeats);
        Assert.Equal(-1, decoded.Samples[0]);
        Assert.Equal(1, decoded.Samples[1]);
    }

    [Fact]
    public void Vagp_ReadsBigEndianMetadataAndZeroChannelAsMono()
    {
        byte[] bytes = MakeVag("unit_test", 44_100);
        var stream = Vagp.Open(new InMemoryReader("synthetic.vag", bytes));

        Assert.Equal(0x20u, stream.Header.Version);
        Assert.Equal(16, stream.Header.DataSize);
        Assert.Equal(44_100, stream.Header.SampleRate);
        Assert.Equal(0, stream.Header.RawChannelCount);
        Assert.Equal(1, stream.Header.ChannelCount);
        Assert.Equal("unit_test", stream.Header.Name);
        Assert.Equal(28, stream.DecodeMono().Length);
        var markers = stream.AnalyzeFrameMarkers();
        Assert.Equal(new[] { 0 }, markers.EndFrames);
        Assert.Empty(markers.LoopStartFrames);
        Assert.Empty(markers.RepeatFrames);
    }

    [Fact]
    public void LevelAudioWad_ResolvesNativeMusicSelectorToPairedVags()
    {
        var bytes = new byte[5 * 2048];
        I32(bytes, 0x00, GcLevelAudioWad.HeaderSize);
        Range(bytes, 0x08, 3, 64);
        Range(bytes, 0x10, 4, 64);
        MakeVag("left", 44_100).CopyTo(bytes, 3 * 2048);
        MakeVag("right", 44_100).CopyTo(bytes, 4 * 2048);

        var wad = GcLevelAudioWad.Open(new InMemoryReader("AUDIO0.WAD", bytes));
        var pair = wad.ResolveMusicPair(0);

        Assert.Equal("left", pair.Left.Header.Name);
        Assert.Equal("right", pair.Right.Header.Name);
        Assert.Equal(44_100, pair.Left.Header.SampleRate);
        Assert.Throws<InvalidDataException>(() => wad.ResolveMusicPair(2));
    }

    [Fact]
    public void SoundBank_MapsNativeSoundIdToToneAndRawSample()
    {
        byte[] bank = MakeSoundBank();
        var parsed = GcSoundBank.Open(new InMemoryReader("level1#soundbank", bank));

        Assert.Equal(3, parsed.FileType);
        Assert.Equal(1, parsed.Version);
        Assert.Single(parsed.Sounds);
        Assert.Equal(1, parsed.NumVags);

        var tone = Assert.Single(parsed.ResolveToneVoices(0));
        Assert.Equal(0, tone.SampleOffset);
        Assert.Equal(60, tone.CenterNote);
        Assert.Equal(0, tone.CenterFine);

        var voice = parsed.ReadSampleVoice(tone);
        Assert.Equal(16, voice.Info.EncodedBytes);
        Assert.Equal(28, voice.Info.PcmSampleCount);
        Assert.Equal(28, parsed.DecodeTone(tone).Samples.Length);
    }

    [SkippableFact]
    public void RetailGc_AudioCataloguesAndOneShotBankMatchRecoveredWitness()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);

        var audio0Reader = fs.OpenFile("/G/AUDIO0.WAD") ?? throw new FileNotFoundException("/G/AUDIO0.WAD");
        var audio0 = GcLevelAudioWad.Open(audio0Reader);
        Assert.Equal(
            new[]
            {
                new GcAudioRange(3, 372_272),
                new GcAudioRange(185, 372_272),
                new GcAudioRange(367, 2_233_200),
                new GcAudioRange(1458, 2_233_200),
            },
            audio0.Bins.Where(b => b.Present).ToArray());

        var pairA = audio0.ResolveMusicPair(0);
        var pairB = audio0.ResolveMusicPair(4);
        Assert.Equal(("rc2_l00_main_al", "rc2_l00_main_ar"), (pairA.Left.Header.Name, pairA.Right.Header.Name));
        Assert.Equal(("rc2_l00_main_bl", "rc2_l00_main_br"), (pairB.Left.Header.Name, pairB.Right.Header.Name));
        Assert.All(new[] { pairA.Left, pairA.Right, pairB.Left, pairB.Right }, s =>
        {
            Assert.Equal(44_100, s.Header.SampleRate);
            Assert.Equal(1, s.Header.ChannelCount);
        });

        var pairAMarkers = pairA.Left.AnalyzeFrameMarkers();
        Assert.Equal(new[] { 23_262, 23_263 }, pairAMarkers.EndFrames);
        Assert.Equal(new[] { 23_263 }, pairAMarkers.LoopStartFrames);
        Assert.Contains(23_263, pairAMarkers.RepeatFrames);

        var pairBMarkers = pairB.Left.AnalyzeFrameMarkers();
        Assert.Equal(new[] { 139_570, 139_571 }, pairBMarkers.EndFrames);
        Assert.Equal(new[] { 139_571 }, pairBMarkers.LoopStartFrames);
        Assert.Contains(139_571, pairBMarkers.RepeatFrames);

        var globalReader = fs.OpenFile("/G/AUDIO.WAD") ?? throw new FileNotFoundException("/G/AUDIO.WAD");
        var global = GcGlobalAudioWad.Open(globalReader);
        Assert.Equal(215, global.Entries.Count(e => e.Group == GcGlobalAudioWad.Group.Vendor));
        Assert.Equal(144, global.Entries.Count(e => e.Group == GcGlobalAudioWad.Group.HelpEnglish));
        Assert.Equal(44_100, global.OpenVag(GcGlobalAudioWad.Group.Vendor, 0).Header.SampleRate);

        var levelReader = fs.OpenFile("/G/LEVEL0.WAD") ?? throw new FileNotFoundException("/G/LEVEL0.WAD");
        var levelHeader = GcLevelWad.ReadHeader(levelReader);
        var bank = GcSoundBank.Open(GcLevelWad.RequireLump(levelReader, levelHeader, 1));
        Assert.Equal(192, bank.Sounds.Count);
        Assert.Equal(209, bank.NumVags);
        Assert.Equal(209, bank.SampleOffsets.Count);

        var tone0 = Assert.Single(bank.ResolveToneVoices(0));
        Assert.Equal(0, tone0.SampleOffset);
        var sample0 = bank.ReadSampleVoice(tone0);
        Assert.Equal(475, sample0.Info.FrameCount);
        Assert.Equal(7_600, sample0.Info.EncodedBytes);
        Assert.False(sample0.Info.Repeats);
    }

    private static byte[] MakeVag(string name, int sampleRate)
    {
        var bytes = new byte[64];
        "VAGp"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x04, 4), 0x20);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x0c, 4), 16);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x10, 4), sampleRate);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0x20);
        bytes[0x30] = 0x0c;
        bytes[0x31] = (byte)Ps2SpuAdpcm.FrameFlags.End;
        return bytes;
    }

    private static byte[] MakeSoundBank()
    {
        const int metaOffset = 24;
        const int metaSize = 0x70;
        const int sampleOffset = metaOffset + metaSize;
        var bytes = new byte[sampleOffset + 16];

        I32(bytes, 0, 3);
        I32(bytes, 4, 2);
        I32(bytes, 8, metaOffset);
        I32(bytes, 12, metaSize);
        I32(bytes, 16, sampleOffset);
        I32(bytes, 20, 16);

        int m = metaOffset;
        "SBlk"u8.CopyTo(bytes.AsSpan(m));
        I32(bytes, m + 0x04, 1);
        I32(bytes, m + 0x0c, 0x574144);
        I16(bytes, m + 0x16, 1);
        I16(bytes, m + 0x18, 1);
        I16(bytes, m + 0x1a, 1);
        I32(bytes, m + 0x1c, 0x3c);
        I32(bytes, m + 0x20, 0x48);
        I32(bytes, m + 0x28, 16);

        bytes[m + 0x3c] = 127;
        bytes[m + 0x40] = 1;
        I32(bytes, m + 0x44, 0);

        I32(bytes, m + 0x48, 1);
        int tone = m + 0x50;
        bytes[tone] = 1;
        bytes[tone + 1] = 127;
        bytes[tone + 2] = 60;
        bytes[tone + 6] = 0;
        bytes[tone + 7] = 127;
        I32(bytes, tone + 0x10, 0);

        bytes[sampleOffset] = 0x0c;
        bytes[sampleOffset + 1] = (byte)Ps2SpuAdpcm.FrameFlags.End;
        return bytes;
    }

    private static void Range(byte[] bytes, int at, int sector, int size)
    {
        I32(bytes, at, sector);
        I32(bytes, at + 4, size);
    }

    private static void I16(byte[] bytes, int at, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(at, 2), value);

    private static void I32(byte[] bytes, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
}

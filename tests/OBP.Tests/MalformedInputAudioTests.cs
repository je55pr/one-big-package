using System.Buffers.Binary;
using OBP.PS2.Audio;
using OBP.PS2.Vif;
using OBP.Tests.Helpers;

namespace OBP.Tests;

/// <summary>
/// Truncated, zero-length, oversized-length and misaligned synthetic inputs for
/// the VAGp, SPU ADPCM, 989snd bank/renderer and VIF readers.
/// </summary>
public sealed class MalformedInputAudioTests
{
    private static InMemoryReader Mem(byte[] bytes) => new("mem", bytes);

    // -------------------------------------------------------------- VAGp ----

    private static byte[] Vag(int dataSize = 16, int sampleRate = 22050, int frames = 1, byte channels = 0, byte flags = 1)
    {
        var bytes = new byte[Vagp.HeaderSize + frames * 16];
        "VAGp"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0x20);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x0c), dataSize);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x10), sampleRate);
        bytes[0x1e] = channels;
        "TESTNAME"u8.CopyTo(bytes.AsSpan(0x20));
        for (int f = 0; f < frames; f++)
        {
            bytes[Vagp.HeaderSize + f * 16] = 0x0c;
            bytes[Vagp.HeaderSize + f * 16 + 1] = flags;
        }

        return bytes;
    }

    [Fact]
    public void ValidVagpDecodesAndReportsFrameMarkers()
    {
        var stream = Vagp.Open(Mem(Vag()));
        Assert.Equal("TESTNAME", stream.Header.Name);
        Assert.Equal(28, stream.DecodeMono().Length);
        Assert.Equal([0], stream.AnalyzeFrameMarkers().EndFrames);
    }

    [Fact]
    public void VagpRejectsEveryTruncatedHeaderAndPayload()
    {
        byte[] bytes = Vag();
        for (int length = 0; length < bytes.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(bytes[..length])));
        }
    }

    [Theory]
    [InlineData(0, 22050)]
    [InlineData(-16, 22050)]
    [InlineData(int.MinValue, 22050)]
    [InlineData(15, 22050)]      // misaligned to the 16-byte SPU frame
    [InlineData(17, 22050)]
    [InlineData(int.MaxValue - 15, 22050)] // huge, aligned, and beyond the source
    [InlineData(32, 22050)]      // larger than the one-frame payload
    [InlineData(16, 0)]
    [InlineData(16, -1)]
    [InlineData(16, 192_001)]
    public void VagpRejectsInconsistentSizesAndRates(int dataSize, int sampleRate)
    {
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(Vag(dataSize, sampleRate))));
    }

    [Fact]
    public void VagpRejectsBadMagicAndMultichannelDecode()
    {
        byte[] bad = Vag();
        bad[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => Vagp.Open(Mem(bad)));
        var stereo = Vagp.Open(Mem(Vag(channels: 2)));
        Assert.Throws<NotSupportedException>(() => stereo.DecodeMono());
    }

    [Fact]
    public void VagpNameWithoutTerminatorStaysInsideTheHeader()
    {
        byte[] bytes = Vag();
        for (int i = 0x20; i < 0x30; i++)
        {
            bytes[i] = (byte)'N';
        }

        Assert.Equal(16, Vagp.Open(Mem(bytes)).Header.Name.Length);
    }

    // ----------------------------------------------------------- ADPCM ----

    [Fact]
    public void AdpcmRejectsEmptyMisalignedAndInvalidFrames()
    {
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames([]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(new byte[15]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(new byte[17]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(Frame(0x0d))); // shift 13
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(Frame(0x50))); // filter 5
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.DecodeFrames(Frame(0xf0)));
        Assert.Equal(28, Ps2SpuAdpcm.DecodeFrames(Frame(0x4c)).Length);
    }

    private static byte[] Frame(byte predictorShift, byte flags = 0, byte fill = 0)
    {
        var frame = new byte[16];
        frame[0] = predictorShift;
        frame[1] = flags;
        Array.Fill(frame, fill, 2, 14);
        return frame;
    }

    [Fact]
    public void AdpcmExtremeNibblesSaturateInsteadOfOverflowing()
    {
        // Worst-case magnitude every sample with the strongest filter: must clamp to 16-bit PCM.
        foreach (byte header in new byte[] { 0x40, 0x4c, 0x3c, 0x30, 0x00 })
        {
            short[] pcm = Ps2SpuAdpcm.DecodeFrames([.. Frame(header, fill: 0x88), .. Frame(header, fill: 0x77), .. Frame(header, fill: 0xff)]);
            Assert.Equal(84, pcm.Length);
        }
    }

    [Fact]
    public void AdpcmSampleAnalysisIsBoundedByItsRange()
    {
        byte[] noEnd = [.. Frame(0x0c), .. Frame(0x0c)];
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(noEnd));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample([]));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ps2SpuAdpcm.AnalyzeSample(noEnd, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ps2SpuAdpcm.AnalyzeSample(noEnd, noEnd.Length + 1));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(noEnd, noEnd.Length));
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(noEnd, 1)); // misaligned start

        // END flag in a trailing partial frame must not be honoured.
        byte[] partialEnd = [.. Frame(0x0c), 0x0c, 0x01, 0x00];
        Assert.Throws<InvalidDataException>(() => Ps2SpuAdpcm.AnalyzeSample(partialEnd));

        byte[] ok = [.. Frame(0x0c, flags: 4), .. Frame(0x0c, flags: 3)];
        var info = Ps2SpuAdpcm.AnalyzeSample(ok);
        Assert.Equal(2, info.FrameCount);
        Assert.Equal(0, info.LoopStartSample);
        Assert.True(info.Repeats);
        Assert.Equal(56, Ps2SpuAdpcm.DecodeSample(ok).Samples.Length);
    }

    // --------------------------------------------------------- 989 bank ----

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

    private static bool Expected(Exception? ex) => ex is null or InvalidDataException or NotSupportedException;

    private static Exception? OpenAndDecode(byte[] bytes) => Record.Exception(() =>
    {
        var bank = Ps2SoundBank989.Open(Mem(bytes));
        foreach (var sound in bank.Sounds)
        {
            foreach (var tone in bank.ResolveToneVoices(sound.Id))
            {
                bank.DecodeTone(tone);
            }
        }
    });

    [Fact]
    public void EveryBankTruncationFailsWithExpectedExceptionsOnly()
    {
        byte[] bytes = Bank();
        Assert.Null(OpenAndDecode(bytes));
        for (int length = 0; length < bytes.Length; length++)
        {
            var ex = OpenAndDecode(bytes[..length]);
            Assert.True(ex is not null && Expected(ex), $"length {length}: {ex?.GetType().Name}: {ex?.Message}");
        }
    }

    [Fact]
    public void EverySingleByteCorruptionOfABankFailsWithExpectedExceptionsOnly()
    {
        byte[] original = Bank();
        for (int at = 0; at < original.Length; at++)
        {
            foreach (byte value in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
            {
                byte[] copy = (byte[])original.Clone();
                copy[at] = value;
                var ex = OpenAndDecode(copy);
                Assert.True(Expected(ex), $"byte {at}=0x{value:x2}: {ex?.GetType().Name}: {ex?.Message}");
            }
        }
    }

    [Fact]
    public void BankSoundIdsAreRangeChecked()
    {
        var bank = Ps2SoundBank989.Open(Mem(Bank()));
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.ResolveToneVoices(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.ResolveToneVoices(bank.Sounds.Count));
        var foreign = new Ps2SoundBank989.Tone(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x30);
        Assert.Throws<InvalidDataException>(() => bank.ReadSampleVoice(foreign));
    }

    // ----------------------------------------------------- 989 renderer ----

    private static Ps2SoundBank989.Tone Tone() => new(0, 127, -60, 0, 0, 0, 0, 2, 2, 0x00ff, 0, 0, 0);
    private static Ps2SpuAdpcm.DecodedSample Sample() =>
        new(Enumerable.Repeat((short)10000, 960).ToArray(), new(16, 1, 960, null, 960, false));
    private static Ps2SoundBank989.Grain G(int type, int delay, Ps2SoundBank989.Tone? tone, byte[]? parameters = null) =>
        new(type, delay, tone, parameters);
    private static Ps2SoundBank989.Sound Sound(params Ps2SoundBank989.Grain[] grains) => new(1, 127, 0, 0, 0, 0, grains);

    [Fact]
    public void RendererSurvivesHugeGrainDelays()
    {
        var sound = Sound(G(1, int.MaxValue, Tone()));
        var rendered = Ps2SoundRenderer989.TryRender(sound, _ => Sample());
        Assert.NotNull(rendered);
        // The voice starts far past the clamped render window, so nothing is audible and nothing is indexed out of range.
        Assert.Equal(Ps2SoundRenderer989.SampleRate * 8, rendered!.Samples.Length);
        Assert.All(rendered.Samples, sample => Assert.Equal(0, sample));

        // An out-of-range looping start must not wrap to a negative int.
        var repeating = new Ps2SpuAdpcm.DecodedSample(
            Enumerable.Repeat((short)10000, 960).ToArray(), new(16, 1, 960, 0, 960, true));
        Assert.Null(Ps2SoundRenderer989.TryRender(sound with { Flags = 1 }, _ => repeating));

        var voices = Ps2SoundRenderer989.TryRenderVoices(sound, _ => Sample());
        Assert.NotNull(voices);
        Assert.Equal(int.MaxValue, Assert.Single(voices!).StartFrame);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RendererRejectsNegativeCumulativeDelays(int delay)
    {
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(1, delay, Tone())), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRenderVoices(Sound(G(1, delay, Tone())), _ => Sample()));
    }

    [Theory]
    [InlineData(4, 15)]
    [InlineData(25, 3)]
    [InlineData(26, 3)]
    [InlineData(27, 1)]
    [InlineData(28, 1)]
    [InlineData(29, 1)]
    [InlineData(30, 3)]
    [InlineData(31, 5)]
    [InlineData(32, 1)]
    [InlineData(33, 1)]
    [InlineData(34, 5)]
    [InlineData(36, 1)]
    public void RendererRejectsTruncatedGrainParametersWithoutCrashing(int type, int payloadLength)
    {
        var sound = Sound(G(type, 0, null, new byte[payloadLength]));
        Assert.Null(Ps2SoundRenderer989.TryRender(sound, _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRenderVoices(sound, _ => Sample()));
    }

    [Fact]
    public void RendererRejectsTruncatedJumpMarker()
    {
        var jump = new byte[2];
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(
            G(35, 0, null, new byte[1]), G(36, 0, null, jump)), _ => Sample()));
    }

    [Fact]
    public void RendererProgramsCannotLoopForever()
    {
        // Grain 36 (jump) back to the marker grain 35 forever: the operation cap must stop it.
        byte[] marker = new byte[32];
        var sound = Sound(G(35, 0, null, marker), G(36, 0, null, marker));
        Assert.Null(Ps2SoundRenderer989.TryRender(sound, _ => Sample()));
    }

    [Fact]
    public void RendererRejectsBadRegistersAndSelectors()
    {
        byte[] Params(params short[] values)
        {
            var p = new byte[32];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(i * 2), values[i]);
            }

            return p;
        }

        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(30, 0, null, Params(4, 1))), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(30, 0, null, Params(-1, 1))), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(34, 0, null, Params(99, 0, 0))), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(31, 0, null, Params(0, 5, 1))), _ => Sample()));
        // Random-choice group larger than the remaining grains / zero-width.
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(25, 0, null, Params(short.MaxValue, short.MaxValue))), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(25, 0, null, Params(0, 1))), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(25, 0, null, Params(1, short.MinValue))), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(26, 0, null, new byte[32])), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(4, 0, null, new byte[8])), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(1, 0, null)), _ => Sample()));
        Assert.Null(Ps2SoundRenderer989.TryRender(Sound(G(1, 0, Tone() with { Volume = -9 })), _ => Sample()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ps2SoundRenderer989.TryRenderVoices(Sound(), _ => Sample(), null, int.MaxValue));
        var empty = Ps2SoundRenderer989.TryRender(Sound(), _ => Sample());
        Assert.NotNull(empty);
        Assert.Empty(empty!.Samples);
    }

    // --------------------------------------------------------------- VIF ----

    private static byte[] Words(params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        }

        return bytes;
    }

    [Fact]
    public void VifListsStopAtTruncatedPacketsWithoutThrowing()
    {
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>([])));
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(new byte[3])));

        // UNPACK V4-32 (0x6c) of 2 elements needs 32 data bytes; give it 8.
        byte[] truncated = Words(0x6c02_0000u, 1, 2);
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(truncated)));

        // A complete NOP followed by a truncated STROW keeps the first packet only.
        byte[] mixed = Words(0, 0x3000_0000u, 1, 2);
        var packets = Vif.ReadCommandList(new ArraySegment<byte>(mixed));
        Assert.Single(packets);
        Assert.Equal(Vif.CmdNop, packets[0].Code.Cmd);
    }

    [Fact]
    public void VifOversizedDirectAndMpgPacketsAreRejectedNotAllocated()
    {
        // DIRECT with size 0 means 0x10000 quadwords (1 MiB): above the 64 KiB packet cap.
        byte[] direct = Words(0x5000_0000u, 0, 0, 0, 0);
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(direct)));
        // MPG num=0 means 256 instructions; the packet exceeds the source.
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(Words(0x4a00_0000u, 0, 0))));
    }

    [Fact]
    public void VifPacketCapStopsRunawayLists()
    {
        byte[] nops = new byte[4 * 1000];
        Assert.Equal(10, Vif.ReadCommandList(new ArraySegment<byte>(nops), maxPackets: 10).Count);
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(nops), maxPackets: 0));
        Assert.Empty(Vif.ReadCommandList(new ArraySegment<byte>(nops), maxPackets: -1));
    }

    [Fact]
    public void VifListsHonourSegmentOffsets()
    {
        byte[] backing = [0xff, 0xff, .. Words(0, 0)];
        var packets = Vif.ReadCommandList(new ArraySegment<byte>(backing, 2, 8));
        Assert.Equal(2, packets.Count);
        Assert.Equal(0, packets[0].Offset);
        Assert.Equal(4, packets[1].Offset);
    }

    [Fact]
    public void VifPacketSizesAreAlwaysPositiveAndBounded()
    {
        for (uint cmd = 0; cmd < 0x80; cmd++)
        {
            foreach (uint num in new uint[] { 0, 1, 255 })
            {
                var code = Vif.DecodeCode((cmd << 24) | (num << 16) | 0xffff);
                int size = Vif.PacketSize(code);
                Assert.InRange(size, 4, 0x10_0004);
            }
        }

        for (int vnvl = 0; vnvl < 16; vnvl++)
        {
            Assert.InRange(Vif.UnpackElementSize(vnvl), 0, 16);
        }
    }
}

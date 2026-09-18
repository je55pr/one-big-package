namespace OBP.PS2.Audio;

/// <summary>
/// PlayStation SPU ADPCM frame decoding shared by PS2-era Ratchet formats.
/// A frame is 16 bytes: predictor/shift, loop flags, then 28 4-bit samples.
/// </summary>
public static class Ps2SpuAdpcm
{
    public const int FrameSize = 16;
    public const int SamplesPerFrame = 28;

    [Flags]
    public enum FrameFlags : byte
    {
        End = 1,
        Repeat = 2,
        LoopStart = 4,
    }

    public readonly record struct SampleInfo(
        int EncodedBytes,
        int FrameCount,
        int PcmSampleCount,
        int? LoopStartSample,
        int? LoopEndSample,
        bool Repeats);

    public sealed record DecodedSample(short[] Samples, SampleInfo Info);

    private static readonly (int A, int B)[] Coefficients =
    [
        (0, 0),
        (60, 0),
        (115, -52),
        (98, -55),
        (122, -60),
    ];

    /// <summary>
    /// Scan one raw SPU sample beginning at <paramref name="offset"/> through
    /// its first END frame. No sample payload is copied.
    /// </summary>
    public static SampleInfo AnalyzeSample(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset < 0 || offset > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        int frame = 0;
        int? loopStart = null;
        while (offset + (frame + 1L) * FrameSize <= data.Length)
        {
            int at = offset + frame * FrameSize;
            ValidateHeader(data[at], frame);
            var flags = (FrameFlags)data[at + 1];
            if ((flags & FrameFlags.LoopStart) != 0 && loopStart is null)
            {
                loopStart = frame * SamplesPerFrame;
            }

            frame++;
            if ((flags & FrameFlags.End) != 0)
            {
                return new SampleInfo(
                    frame * FrameSize,
                    frame,
                    frame * SamplesPerFrame,
                    loopStart,
                    frame * SamplesPerFrame,
                    (flags & FrameFlags.Repeat) != 0);
            }
        }

        throw new InvalidDataException("SPU ADPCM sample has no complete END frame before the containing range ends.");
    }

    /// <summary>Decode exactly one END-terminated mono SPU sample.</summary>
    public static DecodedSample DecodeSample(ReadOnlySpan<byte> data, int offset = 0)
    {
        var info = AnalyzeSample(data, offset);
        return new DecodedSample(DecodeFrames(data.Slice(offset, info.EncodedBytes)), info);
    }

    /// <summary>
    /// Decode a whole number of SPU ADPCM frames. Loop flags are metadata only;
    /// this returns one linear pass and does not synthesize repeats.
    /// </summary>
    public static short[] DecodeFrames(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length == 0 || encoded.Length % FrameSize != 0)
        {
            throw new InvalidDataException($"SPU ADPCM data length {encoded.Length} is not a non-zero multiple of {FrameSize}.");
        }

        var output = new short[encoded.Length / FrameSize * SamplesPerFrame];
        int outAt = 0;
        int history1 = 0;
        int history2 = 0;

        for (int frame = 0; frame < encoded.Length / FrameSize; frame++)
        {
            int at = frame * FrameSize;
            byte predictorShift = encoded[at];
            ValidateHeader(predictorShift, frame);
            int shift = predictorShift & 0x0f;
            int filter = (predictorShift >> 4) & 0x0f;
            var (a, b) = Coefficients[filter];

            for (int i = 0; i < 14; i++)
            {
                byte packed = encoded[at + 2 + i];
                DecodeNibble(packed & 0x0f);
                DecodeNibble(packed >> 4);
            }

            void DecodeNibble(int nibble)
            {
                int signed = nibble >= 8 ? nibble - 16 : nibble;
                int sample = (signed << 12) >> shift;
                sample += (a * history1) >> 6;
                sample += (b * history2) >> 6;
                sample = System.Math.Clamp(sample, short.MinValue, short.MaxValue);
                history2 = history1;
                history1 = sample;
                output[outAt++] = (short)sample;
            }
        }

        return output;
    }

    private static void ValidateHeader(byte predictorShift, int frame)
    {
        int shift = predictorShift & 0x0f;
        int filter = (predictorShift >> 4) & 0x0f;
        if (shift > 12 || filter >= Coefficients.Length)
        {
            throw new InvalidDataException($"Invalid SPU ADPCM frame {frame}: filter={filter}, shift={shift}.");
        }
    }
}

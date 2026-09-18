using System.Buffers.Binary;
using System.Text;
using OBP.IO;

namespace OBP.PS2.Audio;

/// <summary>Bounded reader for Sony VAGp streams carrying mono SPU ADPCM.</summary>
public static class Vagp
{
    public const int HeaderSize = 0x30;

    public sealed record Header(
        uint Version,
        int DataSize,
        int SampleRate,
        byte RawChannelCount,
        string Name)
    {
        // GC VAG version 0x20 leaves the channel byte zero; each file is one
        // mono channel (stereo music is stored as paired L/R VAG entries).
        public int ChannelCount => RawChannelCount == 0 ? 1 : RawChannelCount;
    }

    public sealed record FrameMarkers(
        IReadOnlyList<int> EndFrames,
        IReadOnlyList<int> LoopStartFrames,
        IReadOnlyList<int> RepeatFrames);

    public sealed record Stream(IRandomAccessReader Reader, Header Header)
    {
        public byte[] ReadEncodedData() => Reader.Read(HeaderSize, Header.DataSize);

        public short[] DecodeMono()
        {
            if (Header.ChannelCount != 1)
            {
                throw new NotSupportedException($"VAGp stream declares {Header.ChannelCount} channels; only mono elementary streams are supported.");
            }

            return Ps2SpuAdpcm.DecodeFrames(ReadEncodedData());
        }

        /// <summary>
        /// Return raw SPU frame-marker positions across the full VAG payload.
        /// This deliberately does not reinterpret trailer markers as a semantic
        /// music loop point.
        /// </summary>
        public FrameMarkers AnalyzeFrameMarkers()
        {
            byte[] encoded = ReadEncodedData();
            var ends = new List<int>();
            var starts = new List<int>();
            var repeats = new List<int>();
            for (int frame = 0; frame < encoded.Length / Ps2SpuAdpcm.FrameSize; frame++)
            {
                var flags = (Ps2SpuAdpcm.FrameFlags)encoded[frame * Ps2SpuAdpcm.FrameSize + 1];
                if ((flags & Ps2SpuAdpcm.FrameFlags.End) != 0)
                {
                    ends.Add(frame);
                }

                if ((flags & Ps2SpuAdpcm.FrameFlags.LoopStart) != 0)
                {
                    starts.Add(frame);
                }

                if ((flags & Ps2SpuAdpcm.FrameFlags.Repeat) != 0)
                {
                    repeats.Add(frame);
                }
            }

            return new FrameMarkers(ends, starts, repeats);
        }
    }

    public static Stream Open(IRandomAccessReader reader)
    {
        if (reader.Length < HeaderSize)
        {
            throw new InvalidDataException($"VAGp source '{reader.Name}' is shorter than the {HeaderSize}-byte header.");
        }

        byte[] bytes = reader.Read(0, HeaderSize);
        if (!bytes.AsSpan(0, 4).SequenceEqual("VAGp"u8))
        {
            throw new InvalidDataException($"VAGp magic not found in '{reader.Name}'.");
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0x04, 4));
        int dataSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0x0c, 4));
        int sampleRate = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0x10, 4));
        byte channels = bytes[0x1e];
        if (dataSize <= 0 || dataSize % Ps2SpuAdpcm.FrameSize != 0)
        {
            throw new InvalidDataException($"VAGp data size {dataSize} is not a positive SPU-frame multiple.");
        }

        if (sampleRate <= 0 || sampleRate > 192_000)
        {
            throw new InvalidDataException($"VAGp sample rate {sampleRate} is outside the supported range.");
        }

        if ((long)HeaderSize + dataSize > reader.Length)
        {
            throw new InvalidDataException($"VAGp declares {dataSize} data bytes beyond '{reader.Name}' ({reader.Length} bytes).");
        }

        int nameLength = Array.IndexOf(bytes, (byte)0, 0x20, 0x10);
        if (nameLength < 0)
        {
            nameLength = 0x30;
        }

        string name = Encoding.ASCII.GetString(bytes, 0x20, nameLength - 0x20);
        return new Stream(reader, new Header(version, dataSize, sampleRate, channels, name));
    }
}

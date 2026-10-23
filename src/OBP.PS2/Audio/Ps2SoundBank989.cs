using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;

namespace OBP.PS2.Audio;

/// <summary>
/// Bounded parser for 989snd SFX blocks. Original R&C1/GC use SBlk v1;
/// original UYA global gadget banks use v3's compact grain representation.
/// </summary>
public class Ps2SoundBank989
{
    public const int SoundEntrySize = 0x0c;
    public const int GrainV1Size = 0x28;
    public const int GrainV3Size = 8;
    public const int ToneSize = 0x18;

    public sealed record Tone(
        sbyte Priority,
        sbyte Volume,
        sbyte CenterNote,
        sbyte CenterFine,
        short Pan,
        sbyte MapLow,
        sbyte MapHigh,
        sbyte PitchBendLow,
        sbyte PitchBendHigh,
        ushort Adsr1,
        ushort Adsr2,
        ushort Flags,
        int SampleOffset);

    public sealed record Grain(int Type, int Delay, Tone? Tone, byte[]? Parameters = null)
    {
        /// <summary>Original v3 opcode, retained separately from normalized parameters.</summary>
        public uint? PackedOpcode { get; init; }
    }

    public sealed record Sound(
        int Id,
        sbyte Volume,
        sbyte VolumeGroup,
        short Pan,
        sbyte InstanceLimit,
        ushort Flags,
        IReadOnlyList<Grain> Grains)
    {
        public bool Loop => (Flags & 1) != 0;
        public IReadOnlyList<Tone> Tones => Grains.Where(g => g.Tone is not null).Select(g => g.Tone!).ToArray();
    }

    public sealed record SampleVoice(
        int Offset,
        int ExtentSize,
        Ps2SpuAdpcm.SampleInfo Info);

    public int FileType { get; }
    public int Version { get; }
    public uint BlockFlags { get; }
    public uint BankId { get; }
    public sbyte BankNumber { get; }
    public int NumVags { get; }
    public IReadOnlyList<Sound> Sounds { get; }
    public IReadOnlyList<int> SampleOffsets { get; }

    private readonly IRandomAccessReader _samples;

    private Ps2SoundBank989(
        int fileType,
        int version,
        uint blockFlags,
        uint bankId,
        sbyte bankNumber,
        int numVags,
        IReadOnlyList<Sound> sounds,
        IReadOnlyList<int> sampleOffsets,
        IRandomAccessReader samples)
    {
        FileType = fileType;
        Version = version;
        BlockFlags = blockFlags;
        BankId = bankId;
        BankNumber = bankNumber;
        NumVags = numVags;
        Sounds = sounds;
        SampleOffsets = sampleOffsets;
        _samples = samples;
    }

    protected Ps2SoundBank989(Ps2SoundBank989 source)
        : this(source.FileType, source.Version, source.BlockFlags, source.BankId,
            source.BankNumber, source.NumVags, source.Sounds, source.SampleOffsets, source._samples)
    { }
    public static Ps2SoundBank989 Open(IRandomAccessReader reader)
    {
        if (reader.Length < 24)
        {
            throw new InvalidDataException($"989snd bank '{reader.Name}' is shorter than the two-chunk file header.");
        }

        byte[] fileHeader = reader.Read(0, 24);
        int fileType = I32(fileHeader, 0);
        int chunkCount = I32(fileHeader, 4);
        if (fileType is not (1 or 3) || chunkCount != 2)
        {
            throw new InvalidDataException($"989snd bank expects type 1/3 with 2 chunks, got type={fileType}, chunks={chunkCount}.");
        }

        var metaRange = ReadChunk(fileHeader, 8, reader, "bank metadata");
        var sampleRange = ReadChunk(fileHeader, 16, reader, "sample data");
        byte[] meta = reader.Read(metaRange.Offset, metaRange.Size);
        var samples = new SubRangeReader(reader, sampleRange.Offset, sampleRange.Size, $"{reader.Name}#samples");

        if (meta.Length < 0x3c || !meta.AsSpan(0, 4).SequenceEqual("SBlk"u8))
        {
            throw new InvalidDataException("989snd SFX metadata does not begin with SBlk.");
        }

        int version = I32(meta, 0x04);
        if (version is not (1 or 3))
        {
            throw new NotSupportedException($"989snd SBlk version {version} is not in the recovered v1/v3 slice.");
        }

        uint blockFlags = U32(meta, 0x08);
        uint bankId = U32(meta, 0x0c);
        sbyte bankNumber = unchecked((sbyte)meta[0x10]);
        int soundCount = I16(meta, 0x16);
        int grainCount = I16(meta, 0x18);
        int numVags = I16(meta, 0x1a);
        int firstSound = I32(meta, 0x1c);
        int firstGrain = I32(meta, 0x20);
        int declaredVagBytes = I32(meta, 0x28);
        int grainStride = version == 1 ? GrainV1Size : GrainV3Size;
        int grainData = version == 3 ? I32(meta, 0x34) : 0;

        if (soundCount < 0 || grainCount < 0 || numVags < 0 ||
            declaredVagBytes != sampleRange.Size ||
            !Fits(meta, firstSound, (long)soundCount * SoundEntrySize) ||
            !Fits(meta, firstGrain, (long)grainCount * grainStride) ||
            (version == 3 && (grainData < firstGrain + (long)grainCount * grainStride || grainData > meta.Length)))
        {
            throw new InvalidDataException("989snd SBlk counts/ranges are inconsistent with the containing chunks.");
        }

        var sounds = new List<Sound>(soundCount);
        var sampleOffsets = new SortedSet<int>();
        for (int soundId = 0; soundId < soundCount; soundId++)
        {
            int at = firstSound + soundId * SoundEntrySize;
            sbyte volume = unchecked((sbyte)meta[at]);
            sbyte group = unchecked((sbyte)meta[at + 1]);
            short pan = I16(meta, at + 2);
            sbyte numGrains = unchecked((sbyte)meta[at + 4]);
            sbyte instanceLimit = unchecked((sbyte)meta[at + 5]);
            ushort flags = U16(meta, at + 6);
            int firstSoundGrain = I32(meta, at + 8);
            if (numGrains < 0 || firstSoundGrain < 0 || firstSoundGrain % grainStride != 0 ||
                (long)firstSoundGrain + (long)numGrains * grainStride > (long)grainCount * grainStride ||
                !Fits(meta, firstGrain + (long)firstSoundGrain, (long)numGrains * grainStride))
            {
                throw new InvalidDataException($"989snd sound {soundId} has an invalid grain range.");
            }

            var grains = new List<Grain>(numGrains);
            for (int i = 0; i < numGrains; i++)
            {
                int grainAt = checked(firstGrain + firstSoundGrain + i * grainStride);
                uint? packed = version == 3 ? U32(meta, grainAt) : null;
                int type = packed is uint opcode ? (int)(opcode >> 24) : I32(meta, grainAt);
                int delay = I32(meta, grainAt + 4);
                byte[] parameters = version == 1
                    ? meta.AsSpan(grainAt + 8, GrainV1Size - 8).ToArray()
                    : ReadCompactParameters(meta, packed!.Value, grainData);
                Tone? tone = type is 1 or 9 ? ReadTone(parameters, 0) : null;
                if (tone is not null)
                {
                    if (tone.SampleOffset < 0 || tone.SampleOffset >= samples.Length ||
                        tone.SampleOffset % Ps2SpuAdpcm.FrameSize != 0)
                    {
                        throw new InvalidDataException($"989snd sound {soundId} tone points outside/alignment of sample chunk.");
                    }

                    sampleOffsets.Add(tone.SampleOffset);
                }

                grains.Add(new Grain(type, delay, tone, parameters) { PackedOpcode = packed });
            }

            sounds.Add(new Sound(soundId, volume, group, pan, instanceLimit, flags, grains));
        }

        if (sampleOffsets.Count != numVags)
        {
            throw new InvalidDataException($"989snd declares {numVags} VAG voices but tones reference {sampleOffsets.Count} unique sample offsets.");
        }

        return new Ps2SoundBank989(fileType, version, blockFlags, bankId, bankNumber, numVags, sounds, sampleOffsets.ToArray(), samples);
    }

    /// <summary>Map a native 989snd sound id to the directly referenced tone voices.</summary>
    public IReadOnlyList<Tone> ResolveToneVoices(int soundId)
    {
        if ((uint)soundId >= Sounds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(soundId));
        }

        return Sounds[soundId].Tones;
    }

    public SampleVoice ReadSampleVoice(Tone tone)
    {
        int index = Array.BinarySearch(SampleOffsets.ToArray(), tone.SampleOffset);
        if (index < 0)
        {
            throw new InvalidDataException($"Tone sample offset 0x{tone.SampleOffset:x} is not in the recovered sample catalogue.");
        }

        long next = index + 1 < SampleOffsets.Count ? SampleOffsets[index + 1] : _samples.Length;
        int extent = checked((int)(next - tone.SampleOffset));
        byte[] bytes = _samples.Read(tone.SampleOffset, extent);
        var info = Ps2SpuAdpcm.AnalyzeSample(bytes);
        return new SampleVoice(tone.SampleOffset, extent, info);
    }

    public Ps2SpuAdpcm.DecodedSample DecodeTone(Tone tone)
    {
        SampleVoice voice = ReadSampleVoice(tone);
        byte[] encoded = _samples.Read(voice.Offset, voice.Info.EncodedBytes);
        return Ps2SpuAdpcm.DecodeSample(encoded);
    }

    private static Tone ReadTone(byte[] data, int at)
    {
        if (!Fits(data, at, ToneSize))
        {
            throw new InvalidDataException("989snd tone runs past metadata.");
        }

        return new Tone(
            unchecked((sbyte)data[at]),
            unchecked((sbyte)data[at + 1]),
            unchecked((sbyte)data[at + 2]),
            unchecked((sbyte)data[at + 3]),
            I16(data, at + 4),
            unchecked((sbyte)data[at + 6]),
            unchecked((sbyte)data[at + 7]),
            unchecked((sbyte)data[at + 8]),
            unchecked((sbyte)data[at + 9]),
            U16(data, at + 0x0a),
            U16(data, at + 0x0c),
            U16(data, at + 0x0e),
            I32(data, at + 0x10));
    }

    private static byte[] ReadCompactParameters(byte[] metadata, uint opcode, int grainData)
    {
        int type = (int)(opcode >> 24);
        int value = (int)(opcode & 0x00ffffff);
        var parameters = new byte[GrainV1Size - 8];
        int bodySize = type switch
        {
            1 or 9 => ToneSize,
            4 => 0x10,
            5 or 6 or 8 or 7 => 0x20,
            _ => 0,
        };
        if (bodySize != 0)
        {
            long at = (long)grainData + value;
            if (at > int.MaxValue || !Fits(metadata, at, bodySize))
                throw new InvalidDataException("989snd compact grain body lies outside metadata.");
            metadata.AsSpan((int)at, bodySize).CopyTo(parameters);
        }
        else if (type == 26)
            BinaryPrimitives.WriteInt32LittleEndian(parameters, value + 1);
        else
            for (int i = 0; i < 3; i++)
                BinaryPrimitives.WriteInt16LittleEndian(parameters.AsSpan(i * 2), unchecked((sbyte)(opcode >> (i * 8))));
        return parameters;
    }

    private readonly record struct Chunk(int Offset, int Size);

    private static Chunk ReadChunk(byte[] header, int at, IRandomAccessReader reader, string label)
    {
        int offset = I32(header, at);
        int size = I32(header, at + 4);
        if (offset < 0 || size <= 0 || offset > reader.Length || size > reader.Length - offset)
        {
            throw new InvalidDataException($"989snd {label} chunk {offset}+{size} lies outside '{reader.Name}'.");
        }

        return new Chunk(offset, size);
    }

    private static bool Fits(byte[] data, long at, long size) =>
        at >= 0 && size >= 0 && at <= data.Length && size <= data.Length - (long)at;

    private static short I16(byte[] data, int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at, 2));
    private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
    private static int I32(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at, 4));
    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}

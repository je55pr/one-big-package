using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.PS2.Compression;
using OBP.PS2.Geometry;
using OBP.PS2.Textures;

namespace OBP.RAC3.Level;

/// <summary>
/// Original NTSC-U global gadget directory and its two-WAD model/texture
/// package. Audio is a separate per-entry 989snd bank, not a level remap.
/// See research/UYA_SHOCK_CANNON.md. The caller owns the disc reader lifetime.
/// </summary>
public static class UyaGlobalGadget
{
    public const int TocHeaderOffset = 0x4588;
    public const int HeaderSize = 0x3c8;
    public const int EntryCount = 48;
    public const int EntrySize = 0x14;
    private const int MaxDecodedBytes = 16 * 1024 * 1024;
    public sealed record Range(int Sector, int SectorCount)
    {
        public long Offset => (long)Sector * UyaDiscIndex.SectorBytes;
        public long Size => (long)SectorCount * UyaDiscIndex.SectorBytes;
        public bool Present => Sector != 0 && SectorCount != 0;
    }
    public sealed record Entry(int DirectoryIndex, int NativeClassId, Range Model, Range SoundBank);
    public sealed record Source(
        Entry Entry,
        byte[] ClassBytes,
        byte[] TextureBytes,
        GcUyaMoby.Mesh Mesh,
        IReadOnlyList<GcUyaMoby.MobyJoint> Joints,
        IReadOnlyList<GcUyaMoby.MobySequence> Sequences,
        IReadOnlyList<Ps2Texture.Decoded> Textures,
        IReadOnlyList<SoundDefinition> SoundDefinitions,
        Ps2SoundBank989? SoundBank)
    {
        public IReadOnlyList<Matrix4x4> InverseBindMatrices { get; init; } = [];
    }
    public sealed record SoundDefinition(
        float MinimumRange, float MaximumRange,
        int MinimumVolume, int MaximumVolume,
        int MinimumPitch, int MaximumPitch,
        byte Loop, byte Flags, short SoundIndex, int BankIndex);

    public static IReadOnlyList<Entry> ReadDirectory(IRandomAccessReader disc)
    {
        var (entries, _) = Open(disc);
        return entries;
    }

    public static Source Read(IRandomAccessReader disc, int nativeClassId)
    {
        if (nativeClassId <= 0) throw new ArgumentOutOfRangeException(nameof(nativeClassId));
        var (entries, payload) = Open(disc);
        Entry[] matches = entries.Where(e => e.NativeClassId == nativeClassId).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"UYA global gadget class {nativeClassId} has {matches.Length} directory entries.");
        Entry entry = matches[0];
        if (!entry.Model.Present || entry.Model.Size < 0x20)
            throw new InvalidDataException("UYA gadget has no bounded model package.");
        byte[] header = payload.Read(entry.Model.Offset, 0x10);
        long firstAt = U32(header, 0), firstSize = U32(header, 4);
        long secondAt = U32(header, 8), secondSize = U32(header, 12);
        long packageEnd = checked(entry.Model.Offset + entry.Model.Size);
        if (firstAt < entry.Model.Offset + 0x10 || firstSize < WadLz.HeaderSize ||
            secondAt < firstAt + firstSize || secondSize < WadLz.HeaderSize ||
            firstSize > packageEnd - firstAt || secondAt > packageEnd || secondSize > packageEnd - secondAt)
            throw new InvalidDataException("UYA gadget's compressed class/texture ranges escape or overlap its package.");
        byte[] classBytes = Decode(firstAt, firstSize);
        byte[] textureBytes = Decode(secondAt, secondSize);
        var mesh = GcUyaMoby.ReadClass(classBytes);
        if (mesh.Positions.Length == 0 || mesh.Indices.Length == 0 ||
            (mesh.Skinned && (!mesh.SkinningApplied || !mesh.SkinStateFullyResolved)) ||
            mesh.Positions.Any(p => !double.IsFinite(p)))
            throw new InvalidDataException("UYA gadget class does not have admitted, finite model geometry.");
        int jointCount = classBytes[8];
        var joints = GcUyaMoby.ReadJoints(classBytes, jointCount);
        var sequences = GcUyaMoby.ReadSequences(classBytes, jointCount);
        var textures = ReadTextureBundle(textureBytes);
        foreach (int id in mesh.TriangleMaterialSlots)
            if (id is >= 0 and < 0xf0 && id >= textures.Count)
                throw new InvalidDataException("UYA gadget mesh references an absent local texture.");
        int soundCount = classBytes[0xd], soundAt = I32(classBytes, 0x28);
        if (soundCount > 0 && (soundAt < GcUyaMoby.ClassHeaderSize || (long)soundAt + soundCount * 0x20L > classBytes.Length))
            throw new InvalidDataException("UYA gadget SoundDef table lies outside its class.");
        var definitions = new List<SoundDefinition>();
        for (int i = 0; i < soundCount; i++)
        {
            int at = soundAt + i * 0x20;
            var sound = new SoundDefinition(F32(classBytes, at), F32(classBytes, at + 4),
                I32(classBytes, at + 8), I32(classBytes, at + 12), I32(classBytes, at + 16), I32(classBytes, at + 20),
                classBytes[at + 24], classBytes[at + 25], BinaryPrimitives.ReadInt16LittleEndian(classBytes.AsSpan(at + 26)), I32(classBytes, at + 28));
            if (!float.IsFinite(sound.MinimumRange) || !float.IsFinite(sound.MaximumRange) ||
                sound.MinimumRange < 0 || sound.MaximumRange < sound.MinimumRange ||
                sound.MinimumVolume < 0 || sound.MaximumVolume < sound.MinimumVolume ||
                sound.MinimumPitch < short.MinValue || sound.MaximumPitch > short.MaxValue ||
                sound.MinimumPitch > sound.MaximumPitch || sound.Loop > 1)
                throw new InvalidDataException("UYA gadget SoundDef has invalid range, volume, pitch or loop fields.");
            definitions.Add(sound);
        }
        Ps2SoundBank989? bank = entry.SoundBank.Present
            ? Ps2SoundBank989.Open(new SubRangeReader(payload, entry.SoundBank.Offset, entry.SoundBank.Size)) : null;
        foreach (SoundDefinition sound in definitions)
            if (sound.BankIndex != 0 || bank is null || sound.SoundIndex < 0 || sound.SoundIndex >= bank.Sounds.Count)
                throw new NotSupportedException("UYA gadget sound references an unresolved external bank or sound.");
        return new(entry, classBytes, textureBytes, mesh, joints, sequences, textures, definitions.AsReadOnly(), bank)
        { InverseBindMatrices = GcUyaGadgetPose.ReadInverseBindMatrices(classBytes) };

        byte[] Decode(long offset, long size)
        {
            if (size > int.MaxValue) throw new InvalidDataException("UYA gadget compressed block is too large.");
            byte[] compressed = payload.Read(offset, (int)size);
            if (WadLz.ReadHeader(compressed).CompressedSize != size)
                throw new InvalidDataException("UYA gadget compressed range and WAD size disagree.");
            return WadLz.Decompress(compressed, MaxDecodedBytes).Data;
        }
    }

    public static IReadOnlyList<Ps2Texture.Decoded> ReadTextureBundle(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 0x10) throw new InvalidDataException("UYA gadget texture directory is truncated.");
        int count = I32(bytes, 0);
        if (count is < 1 or > 16 || 4L + count * 4L > bytes.Length)
            throw new InvalidDataException("UYA gadget texture directory count is invalid.");
        var result = new List<Ps2Texture.Decoded>();
        var seen = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int at = I32(bytes, 4 + i * 4);
            if (at < 4 + count * 4 || at > bytes.Length - 0x20 || !seen.Add(at) ||
                !bytes.AsSpan(at, 4).SequenceEqual("2FIP"u8))
                throw new InvalidDataException("UYA gadget PIF texture offset/magic is invalid.");
            int w = I32(bytes, at + 8), h = I32(bytes, at + 12);
            int format = I32(bytes, at + 16), clutFormat = I32(bytes, at + 20), clutOrder = I32(bytes, at + 24), mipLevels = I32(bytes, at + 28);
            if (format != 0x13 || clutFormat != 0 || clutOrder != 0 || mipLevels != 1)
                throw new NotSupportedException("UYA gadget PIF is outside the recovered single-mip PSMT8/RGBA32 slice.");
            if (w is < 1 or > 2048 || h is < 1 or > 2048 || (long)at + 0x420 + (long)w * h > bytes.Length)
                throw new InvalidDataException("UYA gadget PIF pixels exceed the texture bundle.");
            uint[] palette = Enumerable.Range(0, 256).Select(p => U32(bytes, at + 0x20 + p * 4)).ToArray();
            result.Add(Ps2Texture.DecodePaletted8(new(w, h, bytes.AsMemory(at + 0x420, w * h), palette)));
        }
        return result.AsReadOnly();
    }

    private static (IReadOnlyList<Entry>, IRandomAccessReader) Open(IRandomAccessReader disc)
    {
        ArgumentNullException.ThrowIfNull(disc);
        byte[] header = disc.Read((long)UyaDiscIndex.TocLba * UyaDiscIndex.SectorBytes + TocHeaderOffset, HeaderSize);
        if (I32(header, 0) != HeaderSize)
            throw new InvalidDataException("UYA global gadget resident header size is unsupported.");
        long baseAt = checked((long)U32(header, 4) * UyaDiscIndex.SectorBytes);
        var entries = new List<Entry>();
        long end = UyaDiscIndex.SectorBytes;
        for (int i = 0; i < EntryCount; i++)
        {
            int at = 8 + i * EntrySize;
            int cls = I32(header, at);
            var model = new Range(I32(header, at + 4), I32(header, at + 8));
            var sound = new Range(I32(header, at + 12), I32(header, at + 16));
            if (cls < 0) throw new InvalidDataException("UYA gadget class identity is negative.");
            foreach (Range range in new[] { model, sound })
            {
                if (range.Sector < 0 || range.SectorCount < 0 || (range.Sector == 0) != (range.SectorCount == 0) ||
                    (range.Present && (range.Offset < UyaDiscIndex.SectorBytes || range.Size > MaxDecodedBytes)))
                    throw new InvalidDataException("UYA gadget directory has an invalid sector range.");
                end = Math.Max(end, checked(range.Offset + range.Size));
            }
            entries.Add(new(i, cls, model, sound));
        }
        if (baseAt < 0 || baseAt > disc.Length || end > disc.Length - baseAt)
            throw new InvalidDataException("UYA global gadget payload exceeds the source disc.");
        return (entries.AsReadOnly(), new SubRangeReader(disc, baseAt, end));
    }

    private static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static float F32(byte[] b, int at) => BitConverter.Int32BitsToSingle(I32(b, at));
}

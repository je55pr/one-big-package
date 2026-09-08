using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC2.Level;

/// <summary>
/// The Going Commando level "core": lump 0 holds a <c>GcUyaLevelDataHeader</c>
/// pointing at an uncompressed index block (<c>LevelCoreHeader</c> + class /
/// texture tables) and a WAD-LZ compressed asset blob. Section offsets in the
/// core header are byte offsets into the decompressed asset blob.
/// Translated from <c>reference-ts/packages/gc-level-core</c>.
/// </summary>
public static class GcLevelCore
{
    public const int CoreHeaderSize = 0xbc;
    private const long DefaultMaxDecompressedBytes = 128L * 1024 * 1024;

    public readonly record struct ByteRange(int Offset, int Size);

    public readonly record struct ArrayRange(int Count, int Offset);

    public sealed record DataHeader(
        ByteRange Overlay,
        ByteRange CoreIndex,
        ByteRange GsRam,
        ByteRange HudHeader,
        IReadOnlyList<ByteRange> HudBanks,
        ByteRange CoreData,
        ByteRange TransitionTextures);

    public sealed record CoreHeader(
        ArrayRange GsRam,
        int Tfrags,
        int Occlusion,
        int Sky,
        int Collision,
        ArrayRange MobyClasses,
        ArrayRange TieClasses,
        ArrayRange ShrubClasses,
        ArrayRange TfragTextures,
        ArrayRange MobyTextures,
        ArrayRange TieTextures,
        ArrayRange ShrubTextures,
        ArrayRange PartTextures,
        ArrayRange FxTextures,
        int TexturesBaseOffset,
        int SoundRemapOffset,
        int RatchetSeqsOffset,
        int AssetsCompressedSize,
        int AssetsDecompressedSize,
        int MobySoundRemapOffset,
        IReadOnlyList<uint> Raw);

    public sealed record Core(
        DataHeader DataHeader,
        CoreHeader Header,
        byte[] Index,
        byte[] Assets,
        byte[] GsRam,
        IReadOnlyList<int> SectionBoundaries);

    public static Core Open(IRandomAccessReader dataLump, long maxDecompressedBytes = DefaultMaxDecompressedBytes)
    {
        if (dataLump.Length < 0x58)
        {
            throw new InvalidDataException($"GC level data lump '{dataLump.Name}' is too small for a GcUyaLevelDataHeader.");
        }

        var hb = dataLump.Read(0, 0x58);
        ByteRange Range(int at) => new(
            BinaryPrimitives.ReadInt32LittleEndian(hb.AsSpan(at)),
            BinaryPrimitives.ReadInt32LittleEndian(hb.AsSpan(at + 4)));

        var dataHeader = new DataHeader(
            Range(0x00), Range(0x08), Range(0x10), Range(0x18),
            [Range(0x20), Range(0x28), Range(0x30), Range(0x38), Range(0x40)],
            Range(0x48), Range(0x50));

        AssertWithin(dataHeader.CoreIndex, dataLump.Length, "coreIndex", dataLump.Name);
        AssertWithin(dataHeader.CoreData, dataLump.Length, "coreData", dataLump.Name);
        AssertWithin(dataHeader.GsRam, dataLump.Length, "gsRam", dataLump.Name);
        if (dataHeader.CoreIndex.Size < CoreHeaderSize)
        {
            throw new InvalidDataException($"GC level coreIndex is smaller than the {CoreHeaderSize}-byte LevelCoreHeader.");
        }

        var index = dataLump.Read(dataHeader.CoreIndex.Offset, dataHeader.CoreIndex.Size);
        int S32(int at) => BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
        ArrayRange Arr(int at) => new(S32(at), S32(at + 4));

        var raw = new uint[CoreHeaderSize / 4];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 4));
        }

        var core = new CoreHeader(
            GsRam: Arr(0x00),
            Tfrags: S32(0x08), Occlusion: S32(0x0c), Sky: S32(0x10), Collision: S32(0x14),
            MobyClasses: Arr(0x18), TieClasses: Arr(0x20), ShrubClasses: Arr(0x28),
            TfragTextures: Arr(0x30), MobyTextures: Arr(0x38), TieTextures: Arr(0x40),
            ShrubTextures: Arr(0x48), PartTextures: Arr(0x50), FxTextures: Arr(0x58),
            TexturesBaseOffset: S32(0x60), SoundRemapOffset: S32(0x70), RatchetSeqsOffset: S32(0x78),
            AssetsCompressedSize: S32(0x88), AssetsDecompressedSize: S32(0x8c),
            MobySoundRemapOffset: S32(0xb4), Raw: raw);

        var coreDataReader = new SubRangeReader(dataLump, dataHeader.CoreData.Offset, dataHeader.CoreData.Size, $"{dataLump.Name}#coreData");
        var assets = WadLz.ReadBlock(coreDataReader, 0, maxDecompressedBytes).Data;

        if (core.AssetsDecompressedSize > 0 && assets.Length != core.AssetsDecompressedSize)
        {
            throw new InvalidDataException(
                $"GC level core: decompressed asset blob is {assets.Length} bytes but the header declares {core.AssetsDecompressedSize}.");
        }

        var gsRam = dataHeader.GsRam.Size > 0 ? dataLump.Read(dataHeader.GsRam.Offset, dataHeader.GsRam.Size) : [];

        var boundaries = EnumerateSectionBoundaries(core, index, assets.Length);
        return new Core(dataHeader, core, index, assets, gsRam, boundaries);
    }

    /// <summary>Byte range of the section starting at <paramref name="offset"/> — up to the next larger boundary.</summary>
    public static ByteRange? SectionRange(Core core, int offset)
    {
        if (offset <= 0)
        {
            return null;
        }

        int next = -1;
        foreach (int bound in core.SectionBoundaries)
        {
            if (bound > offset && (next == -1 || bound < next))
            {
                next = bound;
            }
        }

        return next == -1 ? null : new ByteRange(offset, next - offset);
    }

    public static ReadOnlySpan<byte> CollisionSection(Core core)
    {
        var range = SectionRange(core, core.Header.Collision);
        return range is { } r ? core.Assets.AsSpan(r.Offset, r.Size) : ReadOnlySpan<byte>.Empty;
    }

    private static IReadOnlyList<int> EnumerateSectionBoundaries(CoreHeader header, byte[] index, int assetsLength)
    {
        var bounds = new SortedSet<int>();
        void Add(long value)
        {
            if (value > 0 && value <= assetsLength)
            {
                bounds.Add((int)value);
            }
        }

        Add(header.Tfrags);
        Add(header.Occlusion);
        Add(header.Sky);
        Add(header.Collision);
        Add(header.TexturesBaseOffset);
        Add(header.AssetsDecompressedSize != 0 ? header.AssetsDecompressedSize : assetsLength);
        Add(assetsLength);

        void AddClassOffsets(ArrayRange table, int entrySize)
        {
            for (int i = 0; i < table.Count; i++)
            {
                int at = table.Offset + i * entrySize;
                if (at < 0 || at + 4 > index.Length)
                {
                    break;
                }

                Add(BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at)));
            }
        }

        AddClassOffsets(header.MobyClasses, 0x20);
        AddClassOffsets(header.TieClasses, 0x20);
        AddClassOffsets(header.ShrubClasses, 0x30);

        Add(header.MobySoundRemapOffset);
        if (header.RatchetSeqsOffset > 0)
        {
            for (int i = 0; i < 256; i++)
            {
                int at = header.RatchetSeqsOffset + i * 4;
                if (at + 4 > index.Length)
                {
                    break;
                }

                Add(BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at)));
            }
        }

        return bounds.ToList();
    }

    private static void AssertWithin(ByteRange range, long size, string label, string name)
    {
        if (range.Offset < 0 || range.Size < 0 || range.Offset > size || range.Size > size - range.Offset)
        {
            throw new InvalidDataException($"GC level data {label} range {range.Offset}+{range.Size} lies outside '{name}' ({size} bytes).");
        }
    }
}

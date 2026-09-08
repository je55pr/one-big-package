using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC3.Level;

/// <summary>
/// UYA projection of the retail-corroborated GC/UYA 0x58 data header and 0xbc
/// level-core family. Field names retain compatibility provenance; raw words are
/// preserved so no public-derived semantic label destroys native evidence.
/// </summary>
public static class UyaLevelCore
{
    public const int OuterHeaderSize = 0x60;
    public const int DataHeaderSize = 0x58;
    public const int CoreHeaderSize = 0xbc;
    private const long MaxAssetsBytes = 128L * 1024 * 1024;

    public readonly record struct ByteRange(int Offset, int Size)
    {
        public bool Present => Size != 0;
    }
    public readonly record struct ArrayRange(int Count, int Offset);
    public sealed record OuterHeader(int PublicLevelIdHint, IReadOnlyList<ByteRange> Ranges, IReadOnlyList<uint> RawWords);
    public sealed record DataHeader(ByteRange Overlay, ByteRange CoreIndex, ByteRange GsRam, ByteRange CoreData);
    public sealed record CoreHeader(
        ArrayRange GsRam, int Tfrags, int Occlusion, int Sky, int Collision,
        ArrayRange MobyClasses, ArrayRange TieClasses, ArrayRange ShrubClasses,
        ArrayRange TfragTextures, ArrayRange MobyTextures, ArrayRange TieTextures, ArrayRange ShrubTextures,
        ArrayRange PartTextures, ArrayRange FxTextures, int TexturesBaseOffset,
        int RatchetSeqsOffset, int AssetsCompressedSize, int AssetsDecompressedSize,
        int MobySoundRemapOffset, IReadOnlyList<uint> RawWords);
    public sealed record Core(OuterHeader Outer, DataHeader Data, CoreHeader Header, byte[] Index, byte[] Assets, byte[] GsRam, IReadOnlyList<int> SectionBoundaries);
    public sealed record OpenedLevel(UyaDiscIndex.LevelRow Row, IRandomAccessReader LevelReader, IRandomAccessReader PrimaryReader, IRandomAccessReader GameplayReader, Core Core);

    public static OpenedLevel Open(IRandomAccessReader disc, int tableIndex)
    {
        var payload = UyaDiscIndex.OpenMainLevel(disc, tableIndex);
        IRandomAccessReader level = payload.Reader;
        OuterHeader outer = ReadOuterHeader(level);
        if (outer.PublicLevelIdHint != tableIndex)
            throw new InvalidDataException($"UYA table {tableIndex} main-header +0x08 value is {outer.PublicLevelIdHint}; refusing to collapse distinct identities.");
        if (outer.Ranges.Count < 3 || !outer.Ranges[0].Present || !outer.Ranges[2].Present)
            throw new InvalidDataException($"UYA table {tableIndex} requires both retail-corroborated primary and gameplay ranges.");

        var primary = Bounded(level, outer.Ranges[0], $"{level.Name}#primary");
        var gameplay = Bounded(level, outer.Ranges[2], $"{level.Name}#gameplay");
        Core core = OpenCore(primary, outer);
        return new OpenedLevel(payload.Row, level, primary, gameplay, core);
    }

    public static ByteRange? SectionRange(Core core, int offset, bool allowZero = false)
    {
        if (offset < 0 || (!allowZero && offset == 0)) return null;
        int next = core.SectionBoundaries.FirstOrDefault(b => b > offset, -1);
        return next > offset ? new ByteRange(offset, next - offset) : null;
    }

    private static OuterHeader ReadOuterHeader(IRandomAccessReader level)
    {
        if (level.Length < OuterHeaderSize)
            throw new InvalidDataException($"UYA main payload '{level.Name}' is shorter than 0x60 bytes.");
        byte[] bytes = level.Read(0, OuterHeaderSize);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (headerSize != OuterHeaderSize)
            throw new InvalidDataException($"UYA main payload header is 0x{headerSize:x}, expected retail-observed 0x60 family.");

        var raw = new uint[OuterHeaderSize / 4];
        for (int i = 0; i < raw.Length; i++) raw[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
        var ranges = new List<ByteRange>(10);
        for (int slot = 0; slot < 10; slot++)
        {
            int at = 0x10 + slot * 8;
            int offsetSectors = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
            int sizeSectors = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
            bool present = offsetSectors != 0 || sizeSectors != 0;
            if (!present) { ranges.Add(new ByteRange(0, 0)); continue; }
            if (offsetSectors <= 0 || sizeSectors <= 0)
                throw new InvalidDataException($"UYA main range {slot} has invalid sectors {offsetSectors}+{sizeSectors}.");
            long offset = checked((long)offsetSectors * UyaDiscIndex.SectorBytes);
            long size = checked((long)sizeSectors * UyaDiscIndex.SectorBytes);
            if (offset > level.Length || size > level.Length - offset || offset > int.MaxValue || size > int.MaxValue)
                throw new InvalidDataException($"UYA main range {slot} lies outside '{level.Name}'.");
            ranges.Add(new ByteRange((int)offset, (int)size));
        }
        return new OuterHeader(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x08)), ranges, raw);
    }

    private static Core OpenCore(IRandomAccessReader primary, OuterHeader outer)
    {
        if (primary.Length < DataHeaderSize)
            throw new InvalidDataException($"UYA primary range '{primary.Name}' is shorter than 0x58 bytes.");
        byte[] hb = primary.Read(0, DataHeaderSize);
        ByteRange R(int at) => new(BinaryPrimitives.ReadInt32LittleEndian(hb.AsSpan(at)), BinaryPrimitives.ReadInt32LittleEndian(hb.AsSpan(at + 4)));
        var data = new DataHeader(R(0x00), R(0x08), R(0x10), R(0x48));
        Validate(data.CoreIndex, primary.Length, "core index");
        Validate(data.CoreData, primary.Length, "core data");
        Validate(data.GsRam, primary.Length, "GS RAM");
        if (data.CoreIndex.Size < CoreHeaderSize)
            throw new InvalidDataException("UYA core index is smaller than the retail-corroborated 0xbc header.");

        byte[] index = primary.Read(data.CoreIndex.Offset, data.CoreIndex.Size);
        int S(int at) => BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
        ArrayRange A(int at) => new(S(at), S(at + 4));
        var raw = new uint[CoreHeaderSize / 4];
        for (int i = 0; i < raw.Length; i++) raw[i] = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 4));
        var h = new CoreHeader(
            A(0x00), S(0x08), S(0x0c), S(0x10), S(0x14), A(0x18), A(0x20), A(0x28),
            A(0x30), A(0x38), A(0x40), A(0x48), A(0x50), A(0x58), S(0x60), S(0x78),
            S(0x88), S(0x8c), S(0xb4), raw);

        var coreData = new SubRangeReader(primary, data.CoreData.Offset, data.CoreData.Size, $"{primary.Name}#coreData");
        var decoded = WadLz.ReadBlock(coreData, 0, MaxAssetsBytes);
        if (h.AssetsCompressedSize > 0 && decoded.CompressedSize != h.AssetsCompressedSize)
            throw new InvalidDataException($"UYA core compressed size {decoded.CompressedSize} disagrees with header {h.AssetsCompressedSize}.");
        if (h.AssetsDecompressedSize > 0 && decoded.Data.Length != h.AssetsDecompressedSize)
            throw new InvalidDataException($"UYA core decompressed size {decoded.Data.Length} disagrees with header {h.AssetsDecompressedSize}.");
        byte[] gsRam = data.GsRam.Size > 0 ? primary.Read(data.GsRam.Offset, data.GsRam.Size) : [];
        return new Core(outer, data, h, index, decoded.Data, gsRam, EnumerateBoundaries(h, index, decoded.Data.Length));
    }

    private static IReadOnlyList<int> EnumerateBoundaries(CoreHeader h, byte[] index, int assetsLength)
    {
        var set = new SortedSet<int>();
        void Add(long v) { if (v >= 0 && v <= assetsLength) set.Add((int)v); }
        Add(h.Tfrags); Add(h.Occlusion); Add(h.Sky); Add(h.Collision); Add(h.TexturesBaseOffset); Add(assetsLength);
        void AddClasses(ArrayRange r, int stride)
        {
            if (r.Count < 0 || r.Offset < 0 || (long)r.Offset + (long)r.Count * stride > index.Length)
                throw new InvalidDataException($"UYA class table {r.Offset}+{r.Count}x{stride} lies outside the core index.");
            for (int i = 0; i < r.Count; i++) Add(BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(r.Offset + i * stride)));
        }
        AddClasses(h.MobyClasses, 0x20); AddClasses(h.TieClasses, 0x20); AddClasses(h.ShrubClasses, 0x30);
        Add(h.MobySoundRemapOffset);
        if (h.RatchetSeqsOffset > 0 && h.RatchetSeqsOffset < index.Length)
            for (int i = 0; i < 256 && h.RatchetSeqsOffset + i * 4 + 4 <= index.Length; i++) Add(BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(h.RatchetSeqsOffset + i * 4)));
        return set.ToArray();
    }

    private static IRandomAccessReader Bounded(IRandomAccessReader source, ByteRange range, string name)
        => new SubRangeReader(source, range.Offset, range.Size, name);

    private static void Validate(ByteRange range, long length, string label)
    {
        if (!range.Present || range.Offset < 0 || range.Size < 0 || range.Offset > length || range.Size > length - range.Offset)
            throw new InvalidDataException($"UYA {label} range {range.Offset}+{range.Size} lies outside its primary section.");
    }
}

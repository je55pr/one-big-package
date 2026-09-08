using System.Buffers.Binary;
using OBP.IO;

namespace OBP.RAC1.Level;

/// <summary>
/// Bounded reader for the supported R&C1 NTSC-U raw-disc level index. The index
/// is not an ISO-9660 file; retail evidence places the fixed 0x2960-byte table at
/// LBA 1500. Only directly established fields are named here.
/// </summary>
public static class Rac1DiscIndex
{
    public const int SectorBytes = 0x800;
    public const int IndexLba = 1500;
    public const int IndexSize = 0x2960;
    public const int LevelTableOffset = 0x28c8;
    public const int LevelTableCount = 19;
    public const int NativeLevelHeaderSize = 0x2434;
    public const int NativeLevelCorePrefixSize = 0x28;
    public const int NativeLevelCoreRangeCount = 4;

    public sealed record SectorRange(int Slot, int FieldOffset, uint OffsetSectors, uint SizeSectors)
    {
        public long OffsetBytes => checked((long)OffsetSectors * SectorBytes);
        public long SizeBytes => checked((long)SizeSectors * SectorBytes);
        public bool Present => SizeSectors != 0;
    }

    public sealed record TableEntry(int TableSlot, uint HeaderLba, uint RawSecondWord, bool Present);

    public sealed record NativeLevel(
        int TableSlot,
        uint TableRawSecondWord,
        uint HeaderLba,
        int LevelId,
        int HeaderSize,
        IReadOnlyList<SectorRange> CoreRanges);

    public sealed record Catalogue(int Version, int DeclaredSize, IReadOnlyList<TableEntry> Entries, IReadOnlyList<NativeLevel> Levels);

    public static Catalogue Read(IRandomAccessReader reader)
    {
        long indexOffset = checked((long)IndexLba * SectorBytes);
        RandomAccessReaderExtensions.ValidateRange(reader.Name, reader.Length, indexOffset, IndexSize);
        var bytes = reader.Read(indexOffset, IndexSize);
        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int declaredSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        if (version != 1)
        {
            throw new InvalidDataException($"R&C1 disc index version {version} is not supported version 1.");
        }
        if (declaredSize != IndexSize)
        {
            throw new InvalidDataException($"R&C1 disc index declares 0x{declaredSize:x}, expected 0x{IndexSize:x}.");
        }

        var entries = new List<TableEntry>(LevelTableCount);
        var levels = new List<NativeLevel>(LevelTableCount);
        for (int slot = 0; slot < LevelTableCount; slot++)
        {
            int at = LevelTableOffset + slot * 8;
            uint headerLba = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
            uint rawSecond = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
            bool present = headerLba != 0 || rawSecond != 0;
            if (present && headerLba == 0)
            {
                throw new InvalidDataException($"R&C1 level-table slot {slot} is present with zero header LBA.");
            }
            var entry = new TableEntry(slot, headerLba, rawSecond, present);
            entries.Add(entry);
            if (!present)
            {
                continue;
            }

            long headerOffset = checked((long)headerLba * SectorBytes);
            RandomAccessReaderExtensions.ValidateRange(reader.Name, reader.Length, headerOffset, NativeLevelCorePrefixSize);
            var prefix = reader.Read(headerOffset, NativeLevelCorePrefixSize);
            int levelId = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(4));
            if (headerSize != NativeLevelHeaderSize)
            {
                throw new InvalidDataException($"R&C1 slot {slot} header size 0x{headerSize:x} != 0x{NativeLevelHeaderSize:x}.");
            }

            var ranges = new List<SectorRange>(NativeLevelCoreRangeCount);
            for (int r = 0; r < NativeLevelCoreRangeCount; r++)
            {
                int field = 0x08 + r * 8;
                uint offsetSectors = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(field));
                uint sizeSectors = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(field + 4));
                var range = new SectorRange(r, field, offsetSectors, sizeSectors);
                if (range.Present)
                {
                    if (offsetSectors == 0)
                    {
                        throw new InvalidDataException($"R&C1 level {levelId} range {r} has data at zero LBA.");
                    }
                    RandomAccessReaderExtensions.ValidateRange(reader.Name, reader.Length, range.OffsetBytes, range.SizeBytes);
                }
                ranges.Add(range);
            }
            levels.Add(new NativeLevel(slot, rawSecond, headerLba, levelId, headerSize, ranges));
        }

        return new Catalogue(version, declaredSize, entries, levels);
    }

    public static IRandomAccessReader OpenRange(IRandomAccessReader reader, NativeLevel level, int slot)
    {
        if ((uint)slot >= NativeLevelCoreRangeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
        var range = level.CoreRanges[slot];
        if (!range.Present)
        {
            throw new InvalidDataException($"R&C1 level {level.LevelId} core range {slot} is absent.");
        }
        return new SubRangeReader(reader, range.OffsetBytes, range.SizeBytes,
            $"{reader.Name}#rac1-level{level.LevelId}-range{slot}");
    }
}

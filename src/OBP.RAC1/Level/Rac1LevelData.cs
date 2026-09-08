using System.Buffers.Binary;
using OBP.IO;

namespace OBP.RAC1.Level;

/// <summary>R&C1 native level-data directory inside outer positional range 0.</summary>
public static class Rac1LevelData
{
    public const int DirectorySize = 0x58;
    public const int RangeCount = 11;
    public const int CoreIndexSlot = 2;
    public const int GsRamSlot = 3;
    public const int CoreDataSlot = 10;

    public sealed record ByteRange(int Slot, int FieldOffset, int Offset, int Size)
    {
        public bool Present => Size > 0;
    }

    public sealed record Directory(IReadOnlyList<ByteRange> Ranges, long SourceSize);

    public static Directory ReadDirectory(IRandomAccessReader reader)
    {
        if (reader.Length < DirectorySize)
        {
            throw new InvalidDataException($"R&C1 level-data source '{reader.Name}' is smaller than 0x58 bytes.");
        }
        var bytes = reader.Read(0, DirectorySize);
        var ranges = new List<ByteRange>(RangeCount);
        for (int slot = 0; slot < RangeCount; slot++)
        {
            int field = slot * 8;
            int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field));
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field + 4));
            if (size < 0)
            {
                throw new InvalidDataException($"R&C1 level-data range {slot} has negative size {size}.");
            }
            if (size > 0)
            {
                if (offset < 0)
                {
                    throw new InvalidDataException($"R&C1 level-data range {slot} has negative offset {offset}.");
                }
                RandomAccessReaderExtensions.ValidateRange(reader.Name, reader.Length, offset, size);
            }
            else if (offset != 0 && offset != -1)
            {
                throw new InvalidDataException($"R&C1 level-data range {slot} has zero size but unexpected offset {offset}.");
            }
            ranges.Add(new ByteRange(slot, field, offset, size));
        }
        return new Directory(ranges, reader.Length);
    }

    public static IRandomAccessReader OpenRange(IRandomAccessReader reader, Directory directory, int slot)
    {
        if ((uint)slot >= RangeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
        var range = directory.Ranges[slot];
        if (!range.Present)
        {
            throw new InvalidDataException($"R&C1 level-data range {slot} is absent.");
        }
        return new SubRangeReader(reader, range.Offset, range.Size, $"{reader.Name}#range{slot}");
    }
}

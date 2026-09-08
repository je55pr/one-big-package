using System.Buffers.Binary;
using OBP.IO;

namespace OBP.RAC2.Level;

/// <summary>
/// Bounded reader for the loadable sections embedded in a GC level data lump.
/// Section destinations are EE virtual addresses from the retail overlay headers.
/// </summary>
public static class GcLevelOverlay
{
    public const int SectionHeaderBytes = 0x10;
    private const int MaxSectionCount = 64;
    private const int MaxOverlayBytes = 16 * 1024 * 1024;

    public sealed record Section(
        int HeaderOffset,
        int DataOffset,
        uint DestinationAddress,
        int CopySize,
        uint SectionType,
        uint EntryPoint);

    public sealed record Image(byte[] Raw, IReadOnlyList<Section> Sections)
    {
        public byte[] ReadVirtual(uint address, int size)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(size);
            ulong end = (ulong)address + (uint)size;
            if (end > 0x1_0000_0000UL)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Requested virtual range crosses the 32-bit EE address space.");
            }

            foreach (var section in Sections)
            {
                ulong sectionEnd = (ulong)section.DestinationAddress + (uint)section.CopySize;
                if (address < section.DestinationAddress || end > sectionEnd)
                {
                    continue;
                }

                int relative = checked((int)(address - section.DestinationAddress));
                return Raw.AsSpan(section.DataOffset + relative, size).ToArray();
            }

            throw new InvalidDataException(
                $"GC level overlay virtual range 0x{address:X8}..0x{end:X8} is not contained in a loadable section.");
        }
    }

    public static Image Open(IRandomAccessReader dataLump)
    {
        if (dataLump.Length < 8)
        {
            throw new InvalidDataException($"GC level data lump '{dataLump.Name}' is too small for an overlay range.");
        }

        var header = dataLump.Read(0, 8);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (offset < 0 || size <= 0 || size > MaxOverlayBytes)
        {
            throw new InvalidDataException($"GC level data lump '{dataLump.Name}' has invalid overlay range {offset}+{size}.");
        }

        if (offset > dataLump.Length || size > dataLump.Length - offset)
        {
            throw new InvalidDataException($"GC level overlay {offset}+{size} exceeds '{dataLump.Name}' ({dataLump.Length} bytes).");
        }

        return Parse(dataLump.Read(offset, size), dataLump.Name);
    }

    public static Image Parse(byte[] raw, string sourceName = "overlay")
    {
        var sections = new List<Section>();
        int cursor = 0;
        uint? expectedEntryPoint = null;
        while (cursor + SectionHeaderBytes <= raw.Length && sections.Count < MaxSectionCount)
        {
            var span = raw.AsSpan(cursor, SectionHeaderBytes);
            uint destination = BinaryPrimitives.ReadUInt32LittleEndian(span[0..4]);
            uint copySizeRaw = BinaryPrimitives.ReadUInt32LittleEndian(span[4..8]);
            uint sectionType = BinaryPrimitives.ReadUInt32LittleEndian(span[8..12]);
            uint entryPoint = BinaryPrimitives.ReadUInt32LittleEndian(span[12..16]);

            if (expectedEntryPoint is null)
            {
                expectedEntryPoint = entryPoint;
            }
            else if (entryPoint != expectedEntryPoint.Value)
            {
                break;
            }

            if (copySizeRaw > int.MaxValue)
            {
                throw new InvalidDataException($"GC overlay '{sourceName}' section at 0x{cursor:X} is too large.");
            }

            int dataOffset = cursor + SectionHeaderBytes;
            int copySize = (int)copySizeRaw;
            if (copySize > raw.Length - dataOffset)
            {
                throw new InvalidDataException(
                    $"GC overlay '{sourceName}' section at 0x{cursor:X} overruns the {raw.Length}-byte image.");
            }

            ulong endAddress = (ulong)destination + copySizeRaw;
            if (endAddress > 0x1_0000_0000UL)
            {
                throw new InvalidDataException($"GC overlay '{sourceName}' section destination overflows 32-bit EE address space.");
            }

            sections.Add(new Section(cursor, dataOffset, destination, copySize, sectionType, entryPoint));
            cursor = dataOffset + copySize;
        }

        if (sections.Count == 0)
        {
            throw new InvalidDataException($"GC overlay '{sourceName}' contained no loadable sections.");
        }

        if (sections.Count == MaxSectionCount && cursor + SectionHeaderBytes <= raw.Length)
        {
            throw new InvalidDataException($"GC overlay '{sourceName}' exceeded the {MaxSectionCount}-section safety cap.");
        }

        return new Image(raw, sections);
    }
}

using System.Buffers.Binary;
using OBP.IO;

namespace OBP.PS2.Elf;

public sealed record Elf32Header(
    string Endian,
    int OsAbi,
    int AbiVersion,
    int Type,
    int Machine,
    long Version,
    long Entry,
    long ProgramHeaderOffset,
    long SectionHeaderOffset,
    long Flags,
    int HeaderSize,
    int ProgramHeaderEntrySize,
    int ProgramHeaderCount,
    int SectionHeaderEntrySize,
    int SectionHeaderCount,
    int SectionNameStringTableIndex)
{
    public bool LittleEndian => Endian == "little";
}

public sealed record Elf32ProgramHeader(
    int Index,
    long Type,
    long Offset,
    long VirtualAddress,
    long PhysicalAddress,
    long FileSize,
    long MemorySize,
    long Flags,
    long Alignment);

public sealed record Elf32VirtualRangeMapping(int SegmentIndex, long VirtualAddress, long FileOffset, long Length);

/// <summary>
/// Bounded ELF32 header / program-header archaeology + file-backed
/// virtual-address translation. Mirrors <c>reference-ts/packages/elf32</c>.
/// </summary>
public static class Elf32Reader
{
    public const int MachineMips = 8;
    public const int TypeExecutable = 2;
    public const int ProgramTypeLoad = 1;

    private const int HeaderSize = 52;
    private const int ProgramHeaderSize = 32;
    private const long AddressSpaceSize = 0x1_0000_0000;
    private static readonly byte[] Magic = [0x7f, 0x45, 0x4c, 0x46];

    public static Elf32Header ReadHeader(IRandomAccessReader reader)
    {
        if (reader.Length < HeaderSize)
        {
            throw new InvalidDataException($"ELF32 source '{reader.Name}' is smaller than the {HeaderSize}-byte header.");
        }

        var b = new byte[HeaderSize];
        reader.Read(0, b);

        for (int i = 0; i < Magic.Length; i++)
        {
            if (b[i] != Magic[i])
            {
                throw new InvalidDataException($"Invalid ELF magic in '{reader.Name}'.");
            }
        }

        if (b[4] != 1)
        {
            throw new InvalidDataException($"Unsupported ELF class {b[4]}; expected ELF32.");
        }

        int dataEncoding = b[5];
        if (dataEncoding is not (1 or 2))
        {
            throw new InvalidDataException($"Unsupported ELF data encoding {dataEncoding}.");
        }

        if (b[6] != 1)
        {
            throw new InvalidDataException($"Unsupported ELF identification version {b[6]}.");
        }

        bool le = dataEncoding == 1;
        ushort U16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
        uint U32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));

        var header = new Elf32Header(
            Endian: le ? "little" : "big",
            OsAbi: b[7],
            AbiVersion: b[8],
            Type: U16(16),
            Machine: U16(18),
            Version: U32(20),
            Entry: U32(24),
            ProgramHeaderOffset: U32(28),
            SectionHeaderOffset: U32(32),
            Flags: U32(36),
            HeaderSize: U16(40),
            ProgramHeaderEntrySize: U16(42),
            ProgramHeaderCount: U16(44),
            SectionHeaderEntrySize: U16(46),
            SectionHeaderCount: U16(48),
            SectionNameStringTableIndex: U16(50));

        if (header.Version != 1)
        {
            throw new InvalidDataException($"Unsupported ELF32 version {header.Version}.");
        }

        if (header.HeaderSize < HeaderSize)
        {
            throw new InvalidDataException($"Invalid ELF32 header size {header.HeaderSize}; expected at least {HeaderSize}.");
        }

        if (header.ProgramHeaderCount > 0 && header.ProgramHeaderEntrySize < ProgramHeaderSize)
        {
            throw new InvalidDataException($"Invalid ELF32 program-header entry size {header.ProgramHeaderEntrySize}.");
        }

        ValidateTable(reader, "program-header", header.ProgramHeaderOffset, header.ProgramHeaderEntrySize, header.ProgramHeaderCount);
        if (header.SectionHeaderCount > 0)
        {
            if (header.SectionHeaderEntrySize == 0)
            {
                throw new InvalidDataException("ELF32 section-header count is nonzero but entry size is zero.");
            }

            ValidateTable(reader, "section-header", header.SectionHeaderOffset, header.SectionHeaderEntrySize, header.SectionHeaderCount);
        }

        return header;
    }

    public static IReadOnlyList<Elf32ProgramHeader> ReadProgramHeaders(IRandomAccessReader reader, Elf32Header? header = null, int maxProgramHeaders = 256)
    {
        var h = header ?? ReadHeader(reader);
        if (h.ProgramHeaderCount > maxProgramHeaders)
        {
            throw new InvalidDataException($"ELF32 program-header count {h.ProgramHeaderCount} exceeds safety cap {maxProgramHeaders}.");
        }

        bool le = h.LittleEndian;
        var entries = new List<Elf32ProgramHeader>(h.ProgramHeaderCount);
        var buf = new byte[h.ProgramHeaderEntrySize];
        for (int i = 0; i < h.ProgramHeaderCount; i++)
        {
            reader.Read(h.ProgramHeaderOffset + (long)i * h.ProgramHeaderEntrySize, buf);
            uint U32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(o));
            var entry = new Elf32ProgramHeader(i, U32(0), U32(4), U32(8), U32(12), U32(16), U32(20), U32(24), U32(28));
            if (entry.FileSize > 0 && (entry.Offset > reader.Length || entry.FileSize > reader.Length - entry.Offset))
            {
                throw new ArgumentOutOfRangeException(nameof(reader), $"ELF32 program segment {i} lies outside '{reader.Name}'.");
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Resolve one fully file-backed ELF32 virtual range through PT_LOAD metadata.</summary>
    public static Elf32VirtualRangeMapping MapVirtualRange(IReadOnlyList<Elf32ProgramHeader> programHeaders, long virtualAddress, long length)
    {
        if (virtualAddress < 0 || virtualAddress >= AddressSpaceSize || length < 0 || virtualAddress + length > AddressSpaceSize)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualAddress), $"Invalid ELF32 virtual range 0x{virtualAddress:x}+{length}.");
        }

        long requestEnd = virtualAddress + length;
        Elf32ProgramHeader? memoryOnly = null;
        foreach (var seg in programHeaders)
        {
            if (seg.Type != ProgramTypeLoad)
            {
                continue;
            }

            long memoryEnd = seg.VirtualAddress + seg.MemorySize;
            if (virtualAddress < seg.VirtualAddress || requestEnd > memoryEnd)
            {
                continue;
            }

            long relative = virtualAddress - seg.VirtualAddress;
            if (relative <= seg.FileSize && length <= seg.FileSize - relative)
            {
                return new Elf32VirtualRangeMapping(seg.Index, virtualAddress, seg.Offset + relative, length);
            }

            memoryOnly = seg;
        }

        string range = $"0x{virtualAddress:x}+{length}";
        throw memoryOnly is not null
            ? new ArgumentOutOfRangeException(nameof(virtualAddress), $"ELF32 virtual range {range} lies in PT_LOAD segment {memoryOnly.Index} but is not fully file-backed (BSS/zero-fill).")
            : new ArgumentOutOfRangeException(nameof(virtualAddress), $"ELF32 virtual range {range} is not mapped by any PT_LOAD segment.");
    }

    public static byte[] ReadVirtualRange(IRandomAccessReader reader, IReadOnlyList<Elf32ProgramHeader> programHeaders, long virtualAddress, long length)
    {
        if (length == 0)
        {
            _ = MapVirtualRange(programHeaders, virtualAddress, 0);
            return [];
        }

        var mapping = MapVirtualRange(programHeaders, virtualAddress, length);
        if (mapping.FileOffset > reader.Length || mapping.Length > reader.Length - mapping.FileOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualAddress), $"Mapped ELF32 virtual range lies outside '{reader.Name}'.");
        }

        return reader.Read(mapping.FileOffset, (int)mapping.Length);
    }

    private static void ValidateTable(IRandomAccessReader reader, string label, long offset, int entrySize, int count)
    {
        if (count == 0)
        {
            return;
        }

        long size = (long)entrySize * count;
        if (offset > reader.Length || size > reader.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(reader), $"ELF32 {label} table lies outside '{reader.Name}'.");
        }
    }
}

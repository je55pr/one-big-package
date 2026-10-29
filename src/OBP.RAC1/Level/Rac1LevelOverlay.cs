using System.Buffers.Binary;
using OBP.IO;

namespace OBP.RAC1.Level;

/// <summary>
/// Read-only view of the source game's level executable overlay, loaded from
/// the outer level-data directory (not from the gameplay WAD). Section addresses
/// are native EE virtual addresses; bytes remain private to the provided disc.
/// </summary>
public sealed class Rac1LevelOverlay
{
    public sealed record Section(uint Address, int Type, byte[] Data);

    public IReadOnlyList<Section> Sections { get; }
    public uint EntryPoint { get; }

    private Rac1LevelOverlay(IReadOnlyList<Section> sections, uint entryPoint)
    {
        Sections = sections;
        EntryPoint = entryPoint;
    }

    public static Rac1LevelOverlay Open(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level)
    {
        ArgumentNullException.ThrowIfNull(disc);
        ArgumentNullException.ThrowIfNull(level);
        var data = Rac1DiscIndex.OpenRange(disc, level, 0);
        var directory = Rac1LevelData.ReadDirectory(data);
        var overlay = Rac1LevelData.OpenRange(data, directory, 0);
        return Decode(overlay.Read(0, checked((int)overlay.Length)));
    }

    /// <summary>Decode destination/size/type/entry sections with strict bounds.</summary>
    public static Rac1LevelOverlay Decode(ReadOnlySpan<byte> bytes)
    {
        var sections = new List<Section>();
        uint entry = 0;
        int at = 0;
        while (at <= bytes.Length - 16)
        {
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
            int type = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 8)..]);
            uint candidateEntry = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at + 12)..]);

            // An executable contains a run of sections sharing the entry.
            // Once the next record diverges it is no longer this executable.
            if (sections.Count > 0 && candidateEntry != entry)
                break;
            if (size < 0 || size > bytes.Length - at - 16 ||
                (ulong)address + (uint)size > uint.MaxValue + 1UL)
                throw new InvalidDataException("R&C1 overlay section exceeds its source bounds.");
            if (type is not (1 or 8))
                throw new InvalidDataException($"Unsupported R&C1 overlay section type {type}.");
            if (sections.Any(s => (ulong)address < (ulong)s.Address + (uint)s.Data.Length &&
                                  (ulong)s.Address < (ulong)address + (uint)size))
                throw new InvalidDataException("R&C1 overlay sections overlap.");
            entry = candidateEntry;
            sections.Add(new Section(address, type, bytes.Slice(at + 16, size).ToArray()));
            at += 16 + size;
        }
        if (sections.Count == 0)
            throw new InvalidDataException("R&C1 level overlay has no executable sections.");
        return new Rac1LevelOverlay(sections, entry);
    }

    /// <summary>Read initialized overlay data by native EE address, never BSS.</summary>
    public bool TryRead(uint address, int length, out ReadOnlyMemory<byte> data)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        foreach (var section in Sections)
        {
            if (section.Type == 1 && address >= section.Address &&
                (ulong)address + (uint)length <= (ulong)section.Address + (uint)section.Data.Length)
            {
                data = section.Data.AsMemory(checked((int)(address - section.Address)), length);
                return true;
            }
        }
        data = default;
        return false;
    }

    public ReadOnlyMemory<byte> Read(uint address, int length) =>
        TryRead(address, length, out var data) ? data :
        throw new InvalidDataException($"R&C1 overlay address 0x{address:x8} is not initialized source data.");
}
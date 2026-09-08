using System.Buffers.Binary;
using OBP.IO;

namespace OBP.RAC2.Level;

/// <summary>
/// Outer container header of a Going Commando <c>/G/LEVEL&lt;n&gt;.WAD</c>.
/// Translated from <c>reference-ts/packages/gc-level-wad</c>. NTSC-U v1.01 is
/// always <c>headerSize == 0x60</c>; the UYA / <c>0x68</c> variants are not
/// handled here. See <c>research/GC_LEVEL_WAD.md</c>.
/// </summary>
public static class GcLevelWad
{
    public const int HeaderSize = 0x60;
    public const int LumpCount = 10;
    public const int SectorBytes = 2048;

    public sealed record Lump(
        int Slot,
        long OffsetSectors,
        long SizeSectors,
        long OffsetBytes,
        long SizeBytes,
        long AvailableBytes,
        bool Present);

    public sealed record Header(
        long HeaderSizeField,
        long Unknown0x04,
        long LevelId,
        long Unknown0x0c,
        IReadOnlyList<Lump> Lumps,
        long SourceSize);

    public static Header ReadHeader(IRandomAccessReader reader)
    {
        if (reader.Length < HeaderSize)
        {
            throw new InvalidDataException($"GC level WAD '{reader.Name}' is smaller than the {HeaderSize}-byte header.");
        }

        var b = reader.Read(0, HeaderSize);
        uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));

        long headerSize = U32(0x00);
        if (headerSize != HeaderSize)
        {
            throw new InvalidDataException($"GC level WAD '{reader.Name}' header size 0x{headerSize:x} is not 0x{HeaderSize:x}.");
        }

        var lumps = new List<Lump>(LumpCount);
        for (int slot = 0; slot < LumpCount; slot++)
        {
            int baseOff = 0x10 + slot * 8;
            long offsetSectors = U32(baseOff);
            long sizeSectors = U32(baseOff + 4);
            bool present = offsetSectors != 0 || sizeSectors != 0;
            long offsetBytes = offsetSectors * SectorBytes;
            long sizeBytes = sizeSectors * SectorBytes;

            if (present)
            {
                if (offsetSectors == 0)
                {
                    throw new InvalidDataException($"GC level WAD '{reader.Name}' lump {slot} has size but no offset.");
                }

                if (offsetBytes < HeaderSize)
                {
                    throw new InvalidDataException($"GC level WAD '{reader.Name}' lump {slot} offset {offsetBytes} overlaps the header.");
                }

                if (offsetBytes > reader.Length)
                {
                    throw new InvalidDataException($"GC level WAD '{reader.Name}' lump {slot} offset {offsetBytes} lies past the {reader.Length}-byte source.");
                }
            }

            long available = present ? System.Math.Min(sizeBytes, reader.Length - offsetBytes) : 0;
            lumps.Add(new Lump(slot, offsetSectors, sizeSectors, offsetBytes, sizeBytes, available, present));
        }

        return new Header(headerSize, U32(0x04), U32(0x08), U32(0x0c), lumps, reader.Length);
    }

    /// <summary>Expose one lump as its own bounded source, or <c>null</c> for an absent lump.</summary>
    public static IRandomAccessReader? OpenLump(IRandomAccessReader reader, Header header, int slot)
    {
        if (slot < 0 || slot >= LumpCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"GC level WAD lump slot {slot} is out of range 0..{LumpCount - 1}.");
        }

        var lump = header.Lumps[slot];
        return lump.Present ? new SubRangeReader(reader, lump.OffsetBytes, lump.AvailableBytes, $"{reader.Name}#lump{slot}") : null;
    }

    public static IRandomAccessReader RequireLump(IRandomAccessReader reader, Header header, int slot) =>
        OpenLump(reader, header, slot) ?? throw new InvalidDataException($"GC level WAD '{reader.Name}' lump {slot} is not present.");
}

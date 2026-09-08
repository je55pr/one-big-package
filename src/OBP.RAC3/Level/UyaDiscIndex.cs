using System.Buffers.Binary;
using OBP.IO;

namespace OBP.RAC3.Level;

/// <summary>
/// Bounded reader for the retail-observed UYA resident table at the LBA-1001
/// public lead. Address and slot meanings remain provenance-marked leads; this
/// parser only accepts the exact structural shape observed on SCUS-97353.
/// </summary>
public static class UyaDiscIndex
{
    public const int SectorBytes = 0x800;
    public const int TocLba = 1001;
    public const int TocWindowBytes = 0x200000;
    public const int ObservedLevelTableOffset = 0x7400;
    public const int LevelRowBytes = 0x18;
    public const int MaxRows = 100;

    public sealed record Part(int Slot, uint HeaderLba, uint SizeSectors, int ResidentHeaderSize, uint PayloadLba);
    public sealed record LevelRow(int TableIndex, IReadOnlyList<Part> Parts, Part MainPart);
    public sealed record LevelPayload(LevelRow Row, IRandomAccessReader Reader);

    public static LevelPayload OpenMainLevel(IRandomAccessReader disc, int tableIndex)
    {
        if (tableIndex < 0 || tableIndex >= MaxRows)
            throw new ArgumentOutOfRangeException(nameof(tableIndex));

        long tocOffset = checked((long)TocLba * SectorBytes);
        if (tocOffset + TocWindowBytes > disc.Length)
            throw new InvalidDataException($"UYA candidate ToC window lies outside '{disc.Name}'.");

        byte[] window = disc.Read(tocOffset, TocWindowBytes);
        ValidateResidentHeaderChain(window);
        int rowAt = checked(ObservedLevelTableOffset + tableIndex * LevelRowBytes);
        if (rowAt + LevelRowBytes > window.Length)
            throw new InvalidDataException($"UYA table row {tableIndex} lies outside the bounded ToC window.");

        var parts = new List<Part>();
        for (int slot = 0; slot < 3; slot++)
        {
            int at = rowAt + slot * 8;
            uint headerLba = BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(at));
            uint sizeSectors = BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(at + 4));
            if (headerLba == 0 && sizeSectors == 0) continue;
            if (headerLba == 0 || sizeSectors == 0 || headerLba <= TocLba)
                throw new InvalidDataException($"UYA table {tableIndex} slot {slot} has invalid sparse range {headerLba}+{sizeSectors} sectors.");

            long residentAt64 = checked(((long)headerLba - TocLba) * SectorBytes);
            if (residentAt64 < 0 || residentAt64 + 8 > window.Length)
                throw new InvalidDataException($"UYA table {tableIndex} slot {slot} resident header lies outside the bounded ToC window.");
            int residentAt = (int)residentAt64;
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(residentAt));
            uint payloadLba = BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(residentAt + 4));
            if (headerSize < 8 || residentAt64 + headerSize > window.Length || payloadLba <= TocLba)
                throw new InvalidDataException($"UYA table {tableIndex} slot {slot} has an invalid resident header.");
            parts.Add(new Part(slot, headerLba, sizeSectors, headerSize, payloadLba));
        }

        Part[] main = parts.Where(p => p.ResidentHeaderSize == 0x60).ToArray();
        if (main.Length != 1)
            throw new InvalidDataException($"UYA table {tableIndex} has {main.Length} parts matching the retail-observed 0x60 main-level header family; expected exactly one.");

        Part selected = main[0];
        long payloadOffset = checked((long)selected.PayloadLba * SectorBytes);
        long payloadBytes = checked((long)selected.SizeSectors * SectorBytes);
        if (payloadBytes <= 0 || payloadOffset > disc.Length || payloadBytes > disc.Length - payloadOffset)
            throw new InvalidDataException($"UYA table {tableIndex} main payload range lies outside '{disc.Name}'.");

        var row = new LevelRow(tableIndex, parts, selected);
        return new LevelPayload(row, new SubRangeReader(disc, payloadOffset, payloadBytes, $"{disc.Name}#uya-table-{tableIndex}"));
    }

    private static void ValidateResidentHeaderChain(byte[] window)
    {
        int at = 0, headers = 0;
        while (at < ObservedLevelTableOffset)
        {
            if (at + 8 > ObservedLevelTableOffset)
                throw new InvalidDataException("UYA resident header chain does not tile to the observed level-table boundary.");
            int size = BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(at));
            uint payloadLba = BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(at + 4));
            if (size < 8 || size > 0xffff || at + size > ObservedLevelTableOffset || payloadLba <= TocLba)
                throw new InvalidDataException($"UYA resident header {headers} at 0x{at:x} is not structurally valid.");
            at += size;
            headers++;
        }
        if (at != ObservedLevelTableOffset || headers != 8)
            throw new InvalidDataException($"UYA resident header chain ended at 0x{at:x} with {headers} headers; authority observation is 8 headers ending at 0x{ObservedLevelTableOffset:x}.");
    }
}

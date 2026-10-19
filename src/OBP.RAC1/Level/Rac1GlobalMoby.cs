using System.Buffers.Binary;
using OBP.PS2.Compression;

namespace OBP.RAC1.Level;

/// <summary>Bounded compressed gadget-class directory at core index +80/+84.</summary>
public static class Rac1GlobalMoby
{
    public sealed record Source(int Offset, byte[] Compressed, byte[] Payload, byte[] TextureMap);

    public static Source Read(Rac1LevelCore.Core core, int nativeClass)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x80));
        int table = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x84));
        if (count is < 0 or > 256 || table < Rac1LevelCore.HeaderSize ||
            (long)table + count * 16L > core.Index.Length)
            throw new InvalidDataException("R&C1 global Moby directory exceeds the core index.");
        int found = -1, length = 0;
        for (int i = 0; i < count; i++)
        {
            var row = core.Index.AsSpan(table + i * 16, 16);
            if (BinaryPrimitives.ReadInt32LittleEndian(row[4..]) != nativeClass) continue;
            if (found >= 0) throw new InvalidDataException("Duplicate global Moby class.");
            found = BinaryPrimitives.ReadInt32LittleEndian(row);
            length = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            if (found < 0 || length < 16 || found > core.Assets.Length - length)
                throw new InvalidDataException("Global Moby compressed payload exceeds core assets.");
        }
        if (found < 0) throw new InvalidDataException($"Global Moby class {nativeClass} is absent.");
        byte[] compressed = core.Assets.AsSpan(found, length).ToArray();
        if (WadLz.ReadHeader(compressed).CompressedSize != length)
            throw new InvalidDataException("Global Moby WAD length differs from its directory.");
        int mobyCount = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x18));
        int mobyTable = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x1c));
        if (mobyCount < 0 || mobyTable < Rac1LevelCore.HeaderSize ||
            (long)mobyTable + mobyCount * 32L > core.Index.Length)
            throw new InvalidDataException("Moby texture-map directory exceeds the core index.");
        byte[]? map = null;
        for (int i = 0; i < mobyCount; i++)
        {
            var row = core.Index.AsSpan(mobyTable + i * 32, 32);
            if (BinaryPrimitives.ReadInt32LittleEndian(row[4..]) != nativeClass) continue;
            if (map is not null) throw new InvalidDataException("Duplicate global Moby texture map.");
            map = row[16..].ToArray();
        }
        return new(found, compressed, WadLz.Decompress(compressed).Data,
            map ?? throw new InvalidDataException("Global Moby texture map is absent."));
    }
}

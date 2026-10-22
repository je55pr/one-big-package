using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>Core +0x70's level SoundDefs and bank remap, preceding its class directories.</summary>
public static class Rac1LevelSoundRemap
{
    public readonly record struct Entry(int Index, int DefinitionOffset, int BankSoundId);

    public static IReadOnlyList<Entry> Read(byte[] index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (index.Length < Rac1LevelCore.HeaderSize)
            throw new InvalidDataException("R&C1 core sound header is truncated.");
        int start = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(0x70));
        if (start < Rac1LevelCore.HeaderSize || start > index.Length - 8)
            throw new InvalidDataException("R&C1 level sound directory is outside core index.");
        int first = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(start));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(start + 2));
        int remap = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(start + 4));
        int remapCount = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(start + 6));
        if (first < 8 || remap < 8 || (long)start + first + count * 32L > index.Length ||
            (long)start + remap + remapCount * 4L > index.Length)
            throw new InvalidDataException("R&C1 level SoundDefs/remap are outside core index.");
        var entries = new Entry[count];
        for (int i = 0; i < count; i++)
        {
            int at = start + first + i * 32;
            int slot = BinaryPrimitives.ReadInt16LittleEndian(index.AsSpan(at + 0x1a));
            if (slot < -1 || slot >= remapCount)
                throw new InvalidDataException("R&C1 level SoundDef bank slot is outside its remap.");
            int sound = slot == -1 ? -1 : BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(start + remap + slot * 4));
            if (sound < -1) throw new InvalidDataException("R&C1 level sound remap has an invalid negative sound.");
            entries[i] = new(i, at, sound);
        }
        return entries;
    }
}

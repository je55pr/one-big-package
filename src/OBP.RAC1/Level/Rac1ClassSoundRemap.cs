using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>
/// Core-index +0x70 maps class-directory row / local SoundDef to an SBlk sound.
/// The eight-byte header is followed by one (u16 relative offset, u16 count)
/// per Moby row. Negative sound ids are absent slots, not playable sounds.
/// </summary>
public static class Rac1ClassSoundRemap
{
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> Read(Rac1LevelCore.Core core)
    {
        byte[] index = core.Index;
        int start = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(0x70, 4));
        var rows = Rac1StaticClasses.ReadMobyTable(core);
        if (start <= 0 || start > index.Length - 8L - rows.Count * 4L)
            throw new InvalidDataException("R&C1 class sound-remap directory is outside core index.");
        var result = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var row in rows)
        {
            int at = start + 8 + row.Index * 4;
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(at, 2));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(at + 2, 2));
            if (count == 0) continue;
            long first = (long)start + offset;
            if (offset < 8 + rows.Count * 4 || first + count * 4L > index.Length)
                throw new InvalidDataException($"R&C1 class {row.OClass} sound-remap entries are outside core index.");
            int[] sounds = new int[count];
            for (int i = 0; i < count; i++)
            {
                sounds[i] = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan((int)first + i * 4, 4));
                if (sounds[i] < -1) throw new InvalidDataException("R&C1 class sound remap has an invalid negative id.");
            }
            if (!result.TryAdd(row.OClass, sounds))
                throw new InvalidDataException($"R&C1 class {row.OClass} has duplicate sound-remap rows.");
        }
        return result;
    }
}

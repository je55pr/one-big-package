using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>
/// R&amp;C1 native level Moby-index group directory at gameplay header +0x48.
/// D_L06_001ABFC0 indexes these original sign-terminated ushort Moby lists.
/// Header: count, total group-data byte size, two reserved words; count
/// 32-bit relative offsets, followed immediately by the packed group data.
/// The second header word is a LENGTH, not a data offset.
/// </summary>
public static class Rac1MobyIndexGroups
{
    public const int HeaderOffset = 0x48;
    public const int MaxGroups = 4096;

    public static IReadOnlyList<int[]> Read(byte[] gameplay, int authoredMobyCount)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        if (authoredMobyCount is < 0 or > 32768 ||
            gameplay.Length < HeaderOffset + sizeof(int))
            throw new InvalidDataException("Invalid native Moby group context.");
        int block = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(HeaderOffset));
        if (block <= 0 || block > gameplay.Length - 16)
            throw new InvalidDataException("Invalid native Moby group block offset.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(block));
        int length = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(block + 4));
        if (count is < 0 or > MaxGroups || length < 0 ||
            (long)block + 16L + count * 4L + length > gameplay.Length)
            throw new InvalidDataException("Invalid native Moby group directory range.");
        int table = block + 16;
        int data = table + count * sizeof(int);
        var groups = new int[count][];
        for (int i = 0; i < count; i++)
        {
            int relative = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(table + i * 4));
            if (relative == -1) { groups[i] = []; continue; }
            if (relative < 0 || relative % 2 != 0 || relative >= length)
                throw new InvalidDataException($"Native Moby group {i} has invalid relative offset {relative}.");
            int next = length;
            for (int j = i + 1; j < count; j++)
            {
                int offset = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(table + j * 4));
                if (offset < 0) continue;
                next = offset;
                break;
            }
            if (next <= relative || next > length || (next - relative) % 2 != 0)
                throw new InvalidDataException($"Native Moby group {i} has invalid group boundary.");
            int capacity = (next - relative) / 2;
            var members = new List<int>(capacity);
            bool terminated = false;
            for (int j = 0; j < capacity; j++)
            {
                ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(gameplay.AsSpan(data + relative + j * 2));
                int index = raw & 0x7fff;
                if (index >= authoredMobyCount)
                    throw new InvalidDataException($"Native Moby group {i} references out-of-range Moby {index}.");
                members.Add(index);
                if ((raw & 0x8000) == 0) continue;
                terminated = true;
                // Only the final group can have alignment padding after its
                // signed last entry. Other groups must end at the next offset.
                if (next < length && j != capacity - 1)
                    throw new InvalidDataException($"Native Moby group {i} overlaps its successor.");
                break;
            }
            if (!terminated)
                throw new InvalidDataException($"Native Moby group {i} has no terminal entry.");
            groups[i] = members.ToArray();
        }
        return groups;
    }
}

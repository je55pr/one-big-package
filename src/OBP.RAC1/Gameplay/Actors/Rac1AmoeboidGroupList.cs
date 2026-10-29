using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original RAC1 Moby.group runtime list word format. Native Novalis
/// FUN_L01_002ef350 walks unsigned halfwords in stored order, masks the
/// top bit for the Moby-array index and ends after the top-bit-set word.
/// The original loader's construction ORDER remains unrecovered.
/// </summary>
public static class Rac1AmoeboidGroupList
{
    public static IReadOnlyList<int> Decode(ReadOnlySpan<byte> nativeWords)
    {
        if (nativeWords.IsEmpty) return [];
        if (nativeWords.Length % sizeof(ushort) != 0)
            throw new InvalidDataException("Native Moby group list has a partial halfword.");

        var members = new List<int>();
        for (int offset = 0; offset < nativeWords.Length; offset += sizeof(ushort))
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(nativeWords[offset..]);
            members.Add(word & 0x7fff);
            if ((word & 0x8000) != 0) return members;
        }

        throw new InvalidDataException("Native Moby group list has no high-bit terminal member.");
    }
}

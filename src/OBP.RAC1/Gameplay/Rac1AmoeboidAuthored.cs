using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original three-class Amoeboid spawn admission. The shared source updater
/// initializes active parents and authored child-pool reserves differently.
/// This describes source inputs only: movement, group-based split selection,
/// damage and host activation are NOT implemented here.
/// </summary>
public static class Rac1AmoeboidAuthored
{
    public const int Large = 572;
    public const int Medium = 865;
    public const int Small = 866;
    public const int PVarSize = 0x2b0;
    public const int ActiveState = 1;
    public const int DormantPoolState = 12;
    public const int WaitingForVolumeState = 13;

    public static bool IsFamily(int nativeClass) => nativeClass is Large or Medium or Small;

    // The authored 0x78-byte record stores the native group index at +0x48.
    public static int ReadGroup(ReadOnlySpan<byte> record)
    {
        if (record.Length != 0x78) throw new InvalidDataException("Native Moby record must contain 0x78 bytes.");
        int group = BinaryPrimitives.ReadInt32LittleEndian(record[0x48..]);
        if (group == -1) return 255; // original native Moby.group = 0xff: no group list
        if (group is < 0 or > 255) throw new InvalidDataException($"Invalid native Amoeboid group index {group}.");
        return group;
    }

    public static Rac1AmoeboidSpawnFacts Read(int nativeClass, ReadOnlySpan<byte> pvar)
    {
        if (!IsFamily(nativeClass)) throw new ArgumentOutOfRangeException(nameof(nativeClass));
        if (pvar.Length != PVarSize)
            throw new InvalidDataException($"Amoeboid class {nativeClass} requires 0x{PVarSize:x} native bytes.");
        int direct = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x230..]);
        int forced = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x258..]);
        int volume = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x25c..]);
        int path = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x240..]);
        int nextPath = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x244..]);
        if (volume < -1 || path < -1 || nextPath < -1)
            throw new InvalidDataException("Invalid Amoeboid authored volume/path source index.");
        int state = forced != 0 || direct != 0 ? ActiveState
            : volume == -1 ? DormantPoolState : WaitingForVolumeState;
        return new(nativeClass, state, volume, path, nextPath,
            nativeClass == Large ? Medium : nativeClass == Medium ? Small : null, forced != 0);
    }
}

public sealed record Rac1AmoeboidSpawnFacts(int ClassId, int InitialNativeState,
    int VolumeIndex, int PathIndex, int NextPathIndex, int? SplitChildClassId, bool ForcedActive = false)
{
    public bool IsActiveOnLoad => InitialNativeState == Rac1AmoeboidAuthored.ActiveState;
    public bool IsSpawnPoolReserve => InitialNativeState == Rac1AmoeboidAuthored.DormantPoolState;
}

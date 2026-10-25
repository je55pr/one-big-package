using OBP.Core;
using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native authored inputs for the widespread invisible metal-detector targets
/// (class 605) and checkpoint markers (class 805). These are parsers and
/// admission rules, not a new generic Moby script interpreter.
/// </summary>
public static class Rac1CommonMobyAuthored
{
    public const int ExplosiveCrate = 505;
    public const int BuriedMetalDetectorTarget = 605;
    public const int CheckpointMarker = 805;
    public const int PulsingColourObject = 1060;

    private const string NativePVarFormat = "rac1-pvar";

    /// <summary>Class 605: the 0x20-byte authored buried-resource target.</summary>
    public static Rac1MetalDetectorTargetAuthored? ReadMetalDetectorTarget(RuntimeDynamicObject source)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != BuriedMetalDetectorTarget)
            return null;
        var bytes = RequirePVar(source, 0x20);
        int path = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10));
        int volume = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x14));
        int resources = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x18));
        if (path < -1 || volume < -1 || resources < 0)
            throw new InvalidDataException("Native buried-resource target has invalid authored path/volume/count.");
        return new(path, volume, resources);
    }

    /// <summary>Class 805: the 0x30-byte authored checkpoint volume and gates.</summary>
    public static Rac1CheckpointMarkerAuthored? ReadCheckpointMarker(RuntimeDynamicObject source)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != CheckpointMarker)
            return null;
        var bytes = RequirePVar(source, 0x30);
        int volume = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x08));
        int rearm = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10));
        int spawnSlot = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x18));
        if (volume < -1 || spawnSlot < -1)
            throw new InvalidDataException("Native checkpoint volume/slot index is invalid.");
        return new(volume, flags, rearm != 0, spawnSlot);
    }

    private static byte[] RequirePVar(RuntimeDynamicObject source, int length)
    {
        var payloads = source.NativePayloads?.Where(p => p.Format == NativePVarFormat).ToArray() ?? [];
        if (payloads.Length != 1 || payloads[0].Data.Length != length)
            throw new InvalidDataException(
                $"R&C1 class {source.NativeClassId} instance {source.InstanceIndex} requires one " +
                $"0x{length:x}-byte authored PVar.");
        return payloads[0].Data;
    }
}

/// <summary>
/// Authored metal-detector target. Resource counts are initial spawn values;
/// native initialization can reduce the count, and collection drains it.
/// </summary>
public sealed record Rac1MetalDetectorTargetAuthored(int PathIndex, int VolumeIndex, int InitialResources)
{
    /// <summary>Caller must resolve the native authored path/clip volume.</summary>
    public bool Eligible(bool insideAuthoredPath, bool insideAuthoredVolume) =>
        (PathIndex == -1 && VolumeIndex == -1) ||
        (PathIndex >= 0 && insideAuthoredPath) ||
        (VolumeIndex >= 0 && insideAuthoredVolume);
}

/// <summary>
/// Native checkpoint marker activation gates, separate from the 3D clip-volume
/// query, persistent UID maps, first-update delay, and checkpoint writer.
/// </summary>
public sealed record Rac1CheckpointMarkerAuthored(
    int VolumeIndex, uint Flags, bool CanRearm, int SpawnSlot)
{
    /// <summary>
    /// Native state gating before testing volume entry. The caller supplies
    /// the actual native global mode and state plus whether the conditional
    /// per-level spawn slot remains available (0xff in the source table).
    /// </summary>
    public bool IsEligible(int mode, uint state, bool spawnSlotAvailable)
    {
        if ((Flags & 8) != 0 || (SpawnSlot != -1 && !spawnSlotAvailable))
            return false;
        if ((Flags & 2) != 0 && mode != 1)
            return false;
        if ((Flags & 4) != 0 && mode is not (0 or 3))
            return false;
        if ((Flags & 0x10) != 0 && state > 1 && state != 9)
            return false;
        return true;
    }
}

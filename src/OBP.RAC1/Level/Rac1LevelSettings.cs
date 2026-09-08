using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC1.Level;

/// <summary>
/// R&amp;C1's retail 0x50-byte first-part level-settings generation. The later GC
/// structure is 0x5c bytes and is intentionally not reused here.
/// </summary>
public static class Rac1LevelSettings
{
    public const int GameplayRangeSlot = 1;
    public const int PointerOffset = 0x00;
    public const int FirstPartSize = 0x50;
    public const long MaxGameplayBytes = 64L * 1024 * 1024;

    public sealed record Settings(
        (double R, double G, double B)? BackgroundColour,
        (double R, double G, double B)? FogColour,
        float FogNearDistance,
        float FogFarDistance,
        float FogNearIntensity,
        float FogFarIntensity,
        float DeathHeight,
        (float X, float Y, float Z) ShipPosition,
        float ShipRotationZ,
        int ShipPath,
        int ShipCameraCuboidStart,
        int ShipCameraCuboidEnd,
        uint RawPad0,
        uint RawPad1,
        int BlockOffset);

    /// <summary>Decode the selected level's outer native gameplay WAD exactly once.</summary>
    public static byte[] ReadGameplay(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level)
    {
        var gameplay = Rac1DiscIndex.OpenRange(disc, level, GameplayRangeSlot);
        return WadLz.ReadBlock(gameplay, 0, MaxGameplayBytes).Data;
    }

    /// <summary>Parse the fixed first-part settings record from decompressed gameplay bytes.</summary>
    public static Settings Parse(byte[] gameplay)
    {
        if (gameplay.Length < 4)
        {
            throw new InvalidDataException($"R&C1 gameplay stream is only {gameplay.Length} bytes.");
        }
        int block = BinaryPrimitives.ReadInt32LittleEndian(gameplay);
        if (block <= 0 || block > gameplay.Length || FirstPartSize > gameplay.Length - block)
        {
            throw new InvalidDataException($"R&C1 level-settings pointer 0x{unchecked((uint)block):x} is out of range.");
        }

        int I32(int relative) => BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(block + relative));
        uint U32(int relative) => BinaryPrimitives.ReadUInt32LittleEndian(gameplay.AsSpan(block + relative));
        float F32(int relative, string label)
        {
            float value = BitConverter.Int32BitsToSingle(I32(relative));
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException($"R&C1 level settings {label} is non-finite.");
            }
            return value;
        }
        (double R, double G, double B)? Rgb(int relative, string label)
        {
            int r = I32(relative), g = I32(relative + 4), b = I32(relative + 8);
            if (r == -1)
            {
                return null;
            }
            if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255)
            {
                throw new InvalidDataException($"R&C1 level settings {label} colour [{r}, {g}, {b}] is invalid.");
            }
            return (r / 255.0, g / 255.0, b / 255.0);
        }

        return new Settings(
            BackgroundColour: Rgb(0x00, "background"),
            FogColour: Rgb(0x0c, "fog"),
            FogNearDistance: F32(0x18, "fogNearDistance"),
            FogFarDistance: F32(0x1c, "fogFarDistance"),
            FogNearIntensity: F32(0x20, "fogNearIntensity"),
            FogFarIntensity: F32(0x24, "fogFarIntensity"),
            DeathHeight: F32(0x28, "deathHeight"),
            ShipPosition: (F32(0x2c, "shipPosition.x"), F32(0x30, "shipPosition.y"), F32(0x34, "shipPosition.z")),
            ShipRotationZ: F32(0x38, "shipRotationZ"),
            ShipPath: I32(0x3c),
            ShipCameraCuboidStart: I32(0x40),
            ShipCameraCuboidEnd: I32(0x44),
            RawPad0: U32(0x48),
            RawPad1: U32(0x4c),
            BlockOffset: block);
    }
}

using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC2.Level;

/// <summary>
/// Going Commando level-settings first part — the head of the gameplay lump
/// (level WAD slot 2, WAD-LZ). Death height, background / fog colour, spherical
/// world, sphere centre and ship position. Translated from
/// <c>reference-ts/packages/gc-level-settings</c>. Colours / heights stay in
/// native (Z-up) units; conversion happens at the world-assembly boundary.
/// </summary>
public static class GcLevelSettings
{
    public const int FirstPartSize = 0x5c;

    public sealed record Settings(
        (double R, double G, double B)? BackgroundColour,
        (double R, double G, double B)? FogColour,
        float FogNearDistance,
        float FogFarDistance,
        float FogNearIntensity,
        float FogFarIntensity,
        float DeathHeight,
        bool IsSphericalWorld,
        (float X, float Y, float Z) SphereCentre,
        (float X, float Y, float Z) ShipPosition,
        float ShipRotationZ,
        int BlockOffset);

    public static Settings Parse(byte[] gameplayData)
    {
        if (gameplayData.Length < 4)
        {
            throw new InvalidDataException($"GC level settings: gameplay lump is only {gameplayData.Length} bytes.");
        }

        int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(gameplayData.AsSpan());
        if (blockOffset <= 0 || blockOffset + FirstPartSize > gameplayData.Length)
        {
            throw new InvalidDataException($"GC level settings: block pointer 0x{blockOffset:x} is out of range.");
        }

        float F32(int rel) => BinaryPrimitives.ReadSingleLittleEndian(gameplayData.AsSpan(blockOffset + rel));
        int S32(int rel) => BinaryPrimitives.ReadInt32LittleEndian(gameplayData.AsSpan(blockOffset + rel));
        (float, float, float) Vec3(int rel) => (F32(rel), F32(rel + 4), F32(rel + 8));

        (double, double, double)? Rgb(int rel)
        {
            int r = S32(rel);
            return r == -1 ? null : (r / 255.0, S32(rel + 4) / 255.0, S32(rel + 8) / 255.0);
        }

        return new Settings(
            BackgroundColour: Rgb(0x00),
            FogColour: Rgb(0x0c),
            FogNearDistance: F32(0x18),
            FogFarDistance: F32(0x1c),
            FogNearIntensity: F32(0x20),
            FogFarIntensity: F32(0x24),
            DeathHeight: F32(0x28),
            IsSphericalWorld: S32(0x2c) != 0,
            SphereCentre: Vec3(0x30),
            ShipPosition: Vec3(0x3c),
            ShipRotationZ: F32(0x48),
            BlockOffset: blockOffset);
    }

    public static Settings Read(IRandomAccessReader gameplayLump, long maxDecompressedBytes = 64L * 1024 * 1024) =>
        Parse(WadLz.ReadBlock(gameplayLump, 0, maxDecompressedBytes).Data);
}

using System.Buffers.Binary;

namespace OBP.RAC3.Level;

/// <summary>Retail-corroborated first 0x5c bytes of the shared GC/UYA/DL settings block.</summary>
public static class UyaLevelSettings
{
    public const int FirstPartSize = 0x5c;
    public sealed record Settings((double R, double G, double B)? BackgroundColour, (double R, double G, double B)? FogColour,
        float FogNearDistance, float FogFarDistance, float FogNearIntensity, float FogFarIntensity,
        float DeathHeight, bool IsSphericalWorld, (float X, float Y, float Z) SphereCentre, int BlockOffset);

    public static Settings Parse(byte[] gameplay)
    {
        if (gameplay.Length < 4) throw new InvalidDataException("UYA gameplay is too short for the settings pointer.");
        int block = BinaryPrimitives.ReadInt32LittleEndian(gameplay);
        if (block <= 0 || block + FirstPartSize > gameplay.Length)
            throw new InvalidDataException($"UYA settings block 0x{block:x} is outside gameplay data.");
        float F(int r) => BinaryPrimitives.ReadSingleLittleEndian(gameplay.AsSpan(block + r));
        int S(int r) => BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(block + r));
        (double, double, double)? Rgb(int r) => S(r) == -1 ? null : (S(r) / 255.0, S(r + 4) / 255.0, S(r + 8) / 255.0);
        float[] finite = [F(0x18), F(0x1c), F(0x20), F(0x24), F(0x28), F(0x30), F(0x34), F(0x38)];
        if (finite.Any(v => !float.IsFinite(v))) throw new InvalidDataException("UYA settings contains a non-finite value.");
        return new Settings(Rgb(0x00), Rgb(0x0c), F(0x18), F(0x1c), F(0x20), F(0x24), F(0x28), S(0x2c) != 0,
            (F(0x30), F(0x34), F(0x38)), block);
    }
}

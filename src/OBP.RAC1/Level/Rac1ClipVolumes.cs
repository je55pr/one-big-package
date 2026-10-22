using System.Buffers.Binary;
using System.Numerics;

namespace OBP.RAC1.Level;

/// <summary>
/// Native authored 0x80-byte 3D clip volumes from gameplay header +0x60.
/// The first 0x40 bytes represent three scaled box axes and a translation.
/// Values remain source Z-up, not Godot Y-up.
/// </summary>
public sealed record Rac1ClipVolume(int Index, Vector3 Centre,
    Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ)
{
    public bool Contains(Vector3 point)
    {
        Vector3 relative = point - Centre;
        float determinant = Vector3.Dot(AxisX, Vector3.Cross(AxisY, AxisZ));
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-7f)
            throw new InvalidDataException($"Native clip volume {Index} has a singular transform.");
        float x = Vector3.Dot(relative, Vector3.Cross(AxisY, AxisZ)) / determinant;
        float y = Vector3.Dot(AxisX, Vector3.Cross(relative, AxisZ)) / determinant;
        float z = Vector3.Dot(AxisX, Vector3.Cross(AxisY, relative)) / determinant;
        return MathF.Abs(x) <= 1 && MathF.Abs(y) <= 1 && MathF.Abs(z) <= 1;
    }
}

public static class Rac1ClipVolumes
{
    public const int HeaderOffset = 0x60;
    public const int RecordSize = 0x80;

    public static IReadOnlyList<Rac1ClipVolume> Read(byte[] gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        if (gameplay.Length < HeaderOffset + 4)
            throw new InvalidDataException("Missing native clip-volume directory.");
        int at = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(HeaderOffset));
        if (at < 0 || at > gameplay.Length - 16)
            throw new InvalidDataException("Invalid native clip-volume directory pointer.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(at));
        if (count < 0 || count > 65_536 || (long)at + 16 + (long)count * RecordSize > gameplay.Length)
            throw new InvalidDataException("Invalid native clip-volume count.");
        var output = new Rac1ClipVolume[count];
        for (int i = 0; i < count; i++)
        {
            int offset = at + 16 + i * RecordSize;
            float F(int start) => BinaryPrimitives.ReadSingleLittleEndian(gameplay.AsSpan(offset + start, 4));
            var volume = new Rac1ClipVolume(i, new(F(48), F(52), F(56)),
                new(F(0), F(4), F(8)), new(F(16), F(20), F(24)),
                new(F(32), F(36), F(40)));
            if (!float.IsFinite(volume.Centre.X) || !float.IsFinite(volume.Centre.Y) ||
                !float.IsFinite(volume.Centre.Z) ||
                !float.IsFinite(volume.AxisX.X) || !float.IsFinite(volume.AxisX.Y) || !float.IsFinite(volume.AxisX.Z) ||
                !float.IsFinite(volume.AxisY.X) || !float.IsFinite(volume.AxisY.Y) || !float.IsFinite(volume.AxisY.Z) ||
                !float.IsFinite(volume.AxisZ.X) || !float.IsFinite(volume.AxisZ.Y) || !float.IsFinite(volume.AxisZ.Z))
                throw new InvalidDataException($"Native clip volume {i} contains non-finite coordinates.");
            output[i] = volume;
        }
        return output;
    }
}

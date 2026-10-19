using System.Buffers.Binary;
using System.Numerics;

namespace OBP.RAC1.Level;

/// <summary>The native path/polygon block addressed by gameplay+0x70 and live 0x1b04b0.</summary>
public static class Rac1Paths
{
    public static IReadOnlyList<Vector3[]> Read(byte[] gameplay)
    {
        int Int(int at)
        {
            if (at < 0 || at > gameplay.Length - 4) throw new InvalidDataException("R&C1 path field exceeds gameplay.");
            return BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(at));
        }
        int block = Int(0x70), count = Int(block), dataOffset = Int(block + 4), dataSize = Int(block + 8);
        if (block <= 0 || count is < 0 or > 4096 || block > gameplay.Length - 0x10 - count * 4)
            throw new InvalidDataException("R&C1 path table is invalid.");
        if (dataOffset < 0x10 + count * 4 || dataSize < 0 || block > gameplay.Length - dataOffset ||
            dataSize > gameplay.Length - block - dataOffset) throw new InvalidDataException("R&C1 path data exceeds gameplay.");
        var paths = new Vector3[count][];
        int previousEnd = 0;
        for (int i = 0; i < count; i++)
        {
            int relative = Int(block + 0x10 + i * 4);
            if (relative < previousEnd || relative > dataSize - 0x10) throw new InvalidDataException("R&C1 path offset overlaps or exceeds its block.");
            int at = block + dataOffset + relative, points = Int(at);
            if (points is < 1 or > 65536 || relative > dataSize - 0x10 - points * 0x10)
                throw new InvalidDataException("R&C1 path points exceed gameplay.");
            previousEnd = relative + 0x10 + points * 0x10;
            paths[i] = new Vector3[points];
            for (int point = 0; point < points; point++)
            {
                int offset = at + 0x10 + point * 0x10;
                float F(int axis) => BitConverter.Int32BitsToSingle(Int(offset + axis * 4));
                var value = new Vector3(F(0), F(1), F(2));
                if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                    throw new InvalidDataException("R&C1 path has a non-finite point.");
                paths[i][point] = value;
            }
        }
        return paths;
    }

    public static bool ContainsPlanar(IReadOnlyList<Vector3> polygon, Vector3 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}

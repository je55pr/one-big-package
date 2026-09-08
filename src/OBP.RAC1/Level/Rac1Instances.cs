using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>Retail R&amp;C1 gameplay Tie/Shrub placement blocks.</summary>
public static class Rac1Instances
{
    public const int TiePointerOffset = 0x34;
    public const int ShrubPointerOffset = 0x3c;
    public const int MobyPointerOffset = 0x44;
    public const int TieRecordSize = 0xe0;
    public const int ShrubRecordSize = 0x70;
    public const int MobyRecordSize = 0x78;

    public sealed record MatrixInstance(
        int Index,
        int OClass,
        float[] Matrix,
        int Raw0x04,
        int Raw0x08,
        int Raw0x0c);

    public sealed record TieInstance(
        int Index,
        int OClass,
        float[] Matrix,
        int Raw0x04,
        int Raw0x08,
        int Raw0x0c,
        int Uid);

    public sealed record MobyInstance(
        int Index,
        int OClass,
        float Scale,
        (float X, float Y, float Z) Position,
        (float X, float Y, float Z) Rotation);

    public sealed record Gameplay(
        IReadOnlyList<TieInstance> TieInstances,
        IReadOnlyList<MatrixInstance> ShrubInstances,
        IReadOnlyList<MobyInstance> MobyInstances,
        int SpawnableMobyCount);

    public static Gameplay Parse(byte[] gameplay)
    {
        var ties = ReadMatrixBlock(gameplay, TiePointerOffset, TieRecordSize, "tie", true)
            .Select(x => new TieInstance(x.Base.Index, x.Base.OClass, x.Base.Matrix,
                x.Base.Raw0x04, x.Base.Raw0x08, x.Base.Raw0x0c, x.Uid))
            .ToList();
        var shrubs = ReadMatrixBlock(gameplay, ShrubPointerOffset, ShrubRecordSize, "shrub", false)
            .Select(x => x.Base)
            .ToList();
        var mobies = ReadMobyBlock(gameplay, out int spawnableMobyCount);
        return new Gameplay(ties, shrubs, mobies, spawnableMobyCount);
    }

    private static List<MobyInstance> ReadMobyBlock(byte[] data, out int spawnableMobyCount)
    {
        int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(MobyPointerOffset));
        if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
            throw new InvalidDataException($"R&C1 gameplay moby block offset {blockOffset} is invalid.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
        spawnableMobyCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset + 4));
        if (count < 0 || count > 200_000 || spawnableMobyCount < 0 || spawnableMobyCount > 200_000 ||
            (long)blockOffset + 0x10L + (long)count * MobyRecordSize > data.Length)
            throw new InvalidDataException($"R&C1 gameplay moby count/range {count} is invalid.");

        var outp = new List<MobyInstance>(count);
        for (int i = 0; i < count; i++)
        {
            int at = blockOffset + 0x10 + i * MobyRecordSize;
            int declaredSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            if (declaredSize != MobyRecordSize)
                throw new InvalidDataException($"R&C1 gameplay moby {i} declares size 0x{declaredSize:x}, expected 0x78.");
            float scale = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x1c));
            var pos = (BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x30)), BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x34)), BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x38)));
            var rot = (BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x3c)), BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x40)), BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x44)));
            if (!float.IsFinite(scale) || !float.IsFinite(pos.Item1) || !float.IsFinite(pos.Item2) || !float.IsFinite(pos.Item3) || !float.IsFinite(rot.Item1) || !float.IsFinite(rot.Item2) || !float.IsFinite(rot.Item3))
                throw new InvalidDataException($"R&C1 gameplay moby {i} has a non-finite transform.");
            outp.Add(new MobyInstance(i, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 0x18)), scale, pos, rot));
        }
        return outp;
    }

    private sealed record Parsed(MatrixInstance Base, int Uid);
    private static List<Parsed> ReadMatrixBlock(
        byte[] data,
        int pointerOffset,
        int recordSize,
        string label,
        bool readUid)
    {
        if (pointerOffset < 0 || pointerOffset + 4 > data.Length)
        {
            throw new InvalidDataException($"R&C1 gameplay {label} pointer is out of range.");
        }
        int blockOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pointerOffset));
        if (blockOffset <= 0 || blockOffset + 0x10 > data.Length)
        {
            throw new InvalidDataException($"R&C1 gameplay {label} block offset {blockOffset} is invalid.");
        }
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(blockOffset));
        if (count < 0 || count > 200_000 ||
            (long)blockOffset + 0x10L + (long)count * recordSize > data.Length)
        {
            throw new InvalidDataException($"R&C1 gameplay {label} count/range {count} is invalid.");
        }

        var outp = new List<Parsed>(count);
        for (int i = 0; i < count; i++)
        {
            int at = blockOffset + 0x10 + i * recordSize;
            var matrix = new float[16];
            for (int m = 0; m < 16; m++)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x10 + m * 4));
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException($"R&C1 gameplay {label} {i} matrix[{m}] is non-finite.");
                }
                matrix[m] = value;
            }
            var instance = new MatrixInstance(
                i,
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)),
                matrix,
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 8)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 12)));
            int uid = readUid ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 0x54)) : 0;
            outp.Add(new Parsed(instance, uid));
        }
        return outp;
    }

    public static (double X, double Y, double Z) TransformPoint(float[] matrix, double x, double y, double z)
    {
        if (matrix.Length != 16)
        {
            throw new ArgumentException("R&C1 instance matrix must contain 16 floats.", nameof(matrix));
        }
        return (
            matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12],
            matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13],
            matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14]);
    }


    public static (double X, double Y, double Z) TransformMobyPoint(MobyInstance instance, double x, double y, double z)
    {
        double sx = System.Math.Sin(instance.Rotation.X), cx = System.Math.Cos(instance.Rotation.X);
        double sy = System.Math.Sin(instance.Rotation.Y), cy = System.Math.Cos(instance.Rotation.Y);
        double sz = System.Math.Sin(instance.Rotation.Z), cz = System.Math.Cos(instance.Rotation.Z);
        double x1 = x, y1 = cx * y - sx * z, z1 = sx * y + cx * z;
        double x2 = cy * x1 + sy * z1, y2 = y1, z2 = -sy * x1 + cy * z1;
        double x3 = cz * x2 - sz * y2, y3 = sz * x2 + cz * y2;
        return (
            instance.Position.X + instance.Scale * x3,
            instance.Position.Y + instance.Scale * y3,
            instance.Position.Z + instance.Scale * z2);
    }
}

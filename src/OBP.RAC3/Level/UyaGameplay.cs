using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC3.Level;

/// <summary>
/// Strict UYA compatibility reader for only the gameplay blocks with direct
/// retail support today: TIE/shrub matrices and authored Moby/PVar identity.
/// It deliberately stops before GC-derived +0x80-and-later gameplay semantics.
/// </summary>
public static class UyaGameplay
{
    private const int TiePtr = 0x34, ShrubPtr = 0x40, MobyPtr = 0x4c, PvarTablePtr = 0x5c, PvarDataPtr = 0x60;
    private const int TieBytes = 0x60, ShrubBytes = 0x70, MobyBytes = 0x88;

    public sealed record MatrixInstance(int Index, int OClass, float[] Matrix);
    public sealed record MobyInstance(int Index, int OClass, float Scale,
        (float X, float Y, float Z) Position, (float X, float Y, float Z) Rotation,
        int UidCompatibility, int Raw0x14, int PvarIndex, int ModeBits,
        byte[] RawInstance, byte[]? PvarData);
    public sealed record Gameplay(IReadOnlyList<MatrixInstance> TieInstances, IReadOnlyList<MatrixInstance> ShrubInstances,
        IReadOnlyList<MobyInstance> MobyInstances, byte[] RawDecoded);

    public static Gameplay Read(IRandomAccessReader compressed, long maxBytes = 64L * 1024 * 1024)
        => Parse(WadLz.ReadBlock(compressed, 0, maxBytes).Data);

    public static Gameplay Parse(byte[] data)
    {
        if (data.Length < 0x68) throw new InvalidDataException($"UYA gameplay is only {data.Length} bytes.");
        int Pointer(int at, string label)
        {
            int p = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            if (p <= 0 || p >= data.Length) throw new InvalidDataException($"UYA {label} pointer 0x{p:x} lies outside gameplay data.");
            return p;
        }
        int pvarTable = Pointer(PvarTablePtr, "PVar table");
        int pvarData = Pointer(PvarDataPtr, "PVar data");

        List<MatrixInstance> Matrices(int ptrAt, int stride, string label)
        {
            int block = Pointer(ptrAt, label);
            if (block + 0x10 > data.Length) throw new InvalidDataException($"UYA {label} header is truncated.");
            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(block));
            if (count < 0 || count > 200_000 || (long)block + 0x10L + (long)count * stride > data.Length)
                throw new InvalidDataException($"UYA {label} count/extent is invalid ({count}).");
            var result = new List<MatrixInstance>(count);
            for (int i = 0; i < count; i++)
            {
                int at = block + 0x10 + i * stride;
                int oClass = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
                var matrix = new float[16];
                for (int m = 0; m < 16; m++)
                {
                    float v = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 0x10 + m * 4));
                    if (!float.IsFinite(v)) throw new InvalidDataException($"UYA {label} {i} contains a non-finite matrix component.");
                    matrix[m] = v;
                }
                result.Add(new MatrixInstance(i, oClass, matrix));
            }
            return result;
        }

        byte[]? ResolvePvar(int index)
        {
            if (index < 0) return null;
            long entry64 = (long)pvarTable + index * 8L;
            if (entry64 < 0 || entry64 + 8 > data.Length) throw new InvalidDataException($"UYA PVar index {index} lies outside the table.");
            int entry = (int)entry64;
            int rel = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entry));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entry + 4));
            long at64 = (long)pvarData + rel;
            if (rel < 0 || size < 0 || at64 < 0 || at64 + size > data.Length)
                throw new InvalidDataException($"UYA PVar {index} range {rel}+{size} lies outside gameplay data.");
            return data.AsSpan((int)at64, size).ToArray();
        }

        int mobyBlock = Pointer(MobyPtr, "Moby instances");
        if (mobyBlock + 0x10 > data.Length) throw new InvalidDataException("UYA Moby block header is truncated.");
        int mobyCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(mobyBlock));
        if (mobyCount < 0 || mobyCount > 200_000 || (long)mobyBlock + 0x10L + (long)mobyCount * MobyBytes > data.Length)
            throw new InvalidDataException($"UYA Moby count/extent is invalid ({mobyCount}).");
        var mobies = new List<MobyInstance>(mobyCount);
        for (int i = 0; i < mobyCount; i++)
        {
            int at = mobyBlock + 0x10 + i * MobyBytes;
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            if (size != MobyBytes) throw new InvalidDataException($"UYA Moby {i} size is 0x{size:x}, expected 0x88.");
            int S(int rel) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + rel));
            float F(int rel)
            {
                float v = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + rel));
                if (!float.IsFinite(v)) throw new InvalidDataException($"UYA Moby {i} contains a non-finite float at +0x{rel:x}.");
                return v;
            }
            int pvarIndex = S(0x68);
            mobies.Add(new MobyInstance(i, S(0x28), F(0x2c), (F(0x40), F(0x44), F(0x48)),
                (F(0x4c), F(0x50), F(0x54)), S(0x10), S(0x14), pvarIndex, S(0x70),
                data.AsSpan(at, MobyBytes).ToArray(), ResolvePvar(pvarIndex)));
        }
        return new Gameplay(Matrices(TiePtr, TieBytes, "TIE instances"), Matrices(ShrubPtr, ShrubBytes, "shrub instances"), mobies, data);
    }

    /// <summary>Native Rz*Ry*Rx transform conjugated by the Y/Z basis swap into OBP Y-up.</summary>
    public static double[] MobyTransform(MobyInstance instance)
    {
        double rx = instance.Rotation.X, ry = instance.Rotation.Y, rz = instance.Rotation.Z, s = instance.Scale;
        double cx = Math.Cos(rx), sx = Math.Sin(rx), cy = Math.Cos(ry), sy = Math.Sin(ry), cz = Math.Cos(rz), sz = Math.Sin(rz);
        double[,] r = {
            { cy * cz, cy * sz, -sy },
            { sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy },
            { cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy }
        };
        int[] q = [0, 2, 1];
        var m = new double[16];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++) m[col * 4 + row] = r[q[row], q[col]] * s;
        m[12] = instance.Position.X; m[13] = instance.Position.Z; m[14] = instance.Position.Y; m[15] = 1;
        return m;
    }
}

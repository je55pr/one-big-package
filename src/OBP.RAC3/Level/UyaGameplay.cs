using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC3.Level;

/// <summary>
/// Strict UYA compatibility reader for gameplay blocks with direct retail
/// support: TIE/shrub matrices, authored Moby/PVar identity, and the native
/// target-region tables recovered from the UYA level loader.
/// </summary>
public static class UyaGameplay
{
    private const int TiePtr = 0x34, ShrubPtr = 0x40, MobyPtr = 0x4c, PvarTablePtr = 0x5c, PvarDataPtr = 0x60;
    private const int TargetVolumePtr = 0x68, TargetGroupPtr = 0x98;
    private const int TieBytes = 0x60, ShrubBytes = 0x70, MobyBytes = 0x88;
    private const int TargetVolumeBytes = 0x80, TargetGroupBytes = 0x30;

    public sealed record MatrixInstance(int Index, int OClass, float[] Matrix);
    public sealed record MobyInstance(int Index, int OClass, float Scale,
        (float X, float Y, float Z) Position, (float X, float Y, float Z) Rotation,
        int UidCompatibility, int Raw0x14, int PvarIndex, int ModeBits,
        byte[] RawInstance, byte[]? PvarData);
    public sealed record TargetVolume(int Index, byte[] RawRecord);
    public sealed record TargetGroup(
        int Index,
        (float X, float Y, float Z) Center,
        float Radius,
        IReadOnlyList<int> PolygonRegionIndices,
        IReadOnlyList<int> OrientedVolumeIndices,
        IReadOnlyList<int> UnknownList2,
        IReadOnlyList<int> UnknownList3,
        IReadOnlyList<int> UnknownList4);

    public sealed record Gameplay(IReadOnlyList<MatrixInstance> TieInstances, IReadOnlyList<MatrixInstance> ShrubInstances,
        IReadOnlyList<MobyInstance> MobyInstances, IReadOnlyList<TargetVolume> TargetVolumes,
        IReadOnlyList<TargetGroup> TargetGroups, byte[] RawDecoded);

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

        int OptionalPointer(int at, string label)
        {
            if (at < 0 || at + sizeof(int) > data.Length) return 0;
            int p = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            if (p == 0) return 0;
            if (p < 0 || p >= data.Length)
                throw new InvalidDataException($"UYA {label} pointer 0x{p:x} lies outside gameplay data.");
            return p;
        }

        List<TargetVolume> TargetVolumes()
        {
            int block = OptionalPointer(TargetVolumePtr, "target volume");
            if (block == 0) return [];
            if (block + 0x10 > data.Length)
                throw new InvalidDataException("UYA target-volume header is truncated.");
            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(block));
            long end = (long)block + 0x10L + (long)count * TargetVolumeBytes;
            if (count < 0 || count > 200_000 || end > data.Length)
                throw new InvalidDataException($"UYA target-volume count/extent is invalid ({count}).");

            var result = new List<TargetVolume>(count);
            for (int i = 0; i < count; i++)
            {
                int at = block + 0x10 + i * TargetVolumeBytes;
                result.Add(new TargetVolume(
                    i,
                    data.AsSpan(at, TargetVolumeBytes).ToArray()));
            }
            return result;
        }

        List<TargetGroup> TargetGroups(IReadOnlyList<TargetVolume> volumes)
        {
            int outer = OptionalPointer(TargetGroupPtr, "target-group");
            if (outer == 0) return [];
            if (outer + sizeof(int) > data.Length)
                throw new InvalidDataException("UYA target-group outer header is truncated.");

            int payloadBytes = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(outer));
            int payload = outer + sizeof(int);
            if (payloadBytes < 0x20 || (long)payload + payloadBytes > data.Length)
                throw new InvalidDataException(
                    $"UYA target-group payload size 0x{payloadBytes:x} is invalid.");

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(payload));
            if (count < 0 || count > 200_000 ||
                0x20L + (long)count * TargetGroupBytes > payloadBytes)
            {
                throw new InvalidDataException(
                    $"UYA target-group count/record extent is invalid ({count}).");
            }

            int[] listBases = new int[5];
            for (int i = 0; i < listBases.Length; i++)
            {
                listBases[i] = BinaryPrimitives.ReadInt32LittleEndian(
                    data.AsSpan(payload + 4 + i * sizeof(int)));
                if (listBases[i] < 0 || listBases[i] > payloadBytes)
                {
                    throw new InvalidDataException(
                        $"UYA target-group list {i} base 0x{listBases[i]:x} lies outside its payload.");
                }
            }

            var result = new List<TargetGroup>(count);
            for (int i = 0; i < count; i++)
            {
                int at = payload + 0x20 + i * TargetGroupBytes;
                float F(int rel)
                {
                    float value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + rel));
                    if (!float.IsFinite(value))
                        throw new InvalidDataException(
                            $"UYA target group {i} has a non-finite float at +0x{rel:x}.");
                    return value;
                }

                int[] ReadList(int list)
                {
                    int itemCount = BinaryPrimitives.ReadInt16LittleEndian(
                        data.AsSpan(at + 0x10 + list * sizeof(short)));
                    if (itemCount < 0)
                        throw new InvalidDataException(
                            $"UYA target group {i} list {list} has negative count {itemCount}.");
                    if (itemCount == 0) return [];

                    int relative = BinaryPrimitives.ReadInt32LittleEndian(
                        data.AsSpan(at + 0x1c + list * sizeof(int)));
                    long start = (long)listBases[list] + relative;
                    long end = start + (long)itemCount * sizeof(int);
                    if (relative < 0 || start < 0 || end > payloadBytes)
                    {
                        throw new InvalidDataException(
                            $"UYA target group {i} list {list} range 0x{relative:x}+{itemCount} lies outside its payload.");
                    }

                    var values = new int[itemCount];
                    int source = checked(payload + (int)start);
                    for (int item = 0; item < itemCount; item++)
                    {
                        values[item] = BinaryPrimitives.ReadInt32LittleEndian(
                            data.AsSpan(source + item * sizeof(int)));
                    }
                    return values;
                }

                float radius = F(0x0c);
                if (radius < 0f)
                    throw new InvalidDataException(
                        $"UYA target group {i} has negative radius {radius}.");

                int[] polygons = ReadList(0);
                int[] orientedVolumes = ReadList(1);
                if (orientedVolumes.Any(index => index < 0 || index >= volumes.Count))
                {
                    throw new InvalidDataException(
                        $"UYA target group {i} references an out-of-range target volume.");
                }

                result.Add(new TargetGroup(
                    i,
                    (F(0x00), F(0x04), F(0x08)),
                    radius,
                    polygons,
                    orientedVolumes,
                    ReadList(2),
                    ReadList(3),
                    ReadList(4)));
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
        List<TargetVolume> targetVolumes = TargetVolumes();
        List<TargetGroup> targetGroups = TargetGroups(targetVolumes);
        return new Gameplay(
            Matrices(TiePtr, TieBytes, "TIE instances"),
            Matrices(ShrubPtr, ShrubBytes, "shrub instances"),
            mobies,
            targetVolumes,
            targetGroups,
            data);
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

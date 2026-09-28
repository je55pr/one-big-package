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
    private const int TargetVolumePtr = 0x68, TargetPolygonPtr = 0x78, TargetGroupPtr = 0x98;
    private const int TieBytes = 0x60, ShrubBytes = 0x70, MobyBytes = 0x88;
    private const int TargetVolumeBytes = 0x80, TargetGroupBytes = 0x30;

    public sealed record MatrixInstance(int Index, int OClass, float[] Matrix);
    public sealed record MobyInstance(int Index, int OClass, float Scale,
        (float X, float Y, float Z) Position, (float X, float Y, float Z) Rotation,
        int UidCompatibility, int Raw0x14, int PvarIndex, int ModeBits,
        byte[] RawInstance, byte[]? PvarData);
    public sealed record TargetVolume(
        int Index,
        (float X, float Y, float Z) Center,
        (float X, float Y, float Z) InverseColumnX,
        (float X, float Y, float Z) InverseColumnY,
        (float X, float Y, float Z) InverseColumnZ,
        byte[] RawRecord)
    {
        /// <summary>
        /// Exact scalar form of retail UYA helper 0x00440760 -> 0x0040C2D0:
        /// center XYZ, multiply by the stored inverse 3x3 basis, then require
        /// each normalized component to lie inclusively in [-1,+1].
        /// </summary>
        public bool Contains(float x, float y, float z)
        {
            float dx = x - Center.X;
            float dy = y - Center.Y;
            float dz = z - Center.Z;
            float nx = InverseColumnX.X * dx + InverseColumnY.X * dy + InverseColumnZ.X * dz;
            float ny = InverseColumnX.Y * dx + InverseColumnY.Y * dy + InverseColumnZ.Y * dz;
            float nz = InverseColumnX.Z * dx + InverseColumnY.Z * dy + InverseColumnZ.Z * dz;
            return nx >= -1f && nx <= 1f
                && ny >= -1f && ny <= 1f
                && nz >= -1f && nz <= 1f;
        }
    }
    public readonly record struct TargetPolygonVertex(float X, float Y, float Z, float W);

    public sealed record TargetPolygon(
        int Index,
        IReadOnlyList<TargetPolygonVertex> Vertices,
        byte[] RawRecord)
    {
        /// <summary>
        /// Exact scalar form of retail UYA helper 0x004432C0. The native test
        /// is an XY half-open crossing count; vertex Z/W are retained but do
        /// not participate.
        /// </summary>
        public bool Contains(float x, float y)
        {
            bool inside = false;
            int count = Vertices.Count;
            if (count <= 0) return false;

            for (int i = 0; i < count; i++)
            {
                TargetPolygonVertex current = Vertices[i];
                TargetPolygonVertex next = Vertices[i + 1 == count ? 0 : i + 1];
                bool crosses = (current.Y < y && y <= next.Y)
                    || (next.Y < y && y <= current.Y);
                if (!crosses) continue;

                float vertical = next.Y - current.Y;
                float ratio = (y - current.Y) / vertical;
                float horizontal = next.X - current.X;
                float scaled = ratio * horizontal;
                float intersectionX = current.X + scaled;
                if (intersectionX < x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }

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
        IReadOnlyList<TargetPolygon> TargetPolygons, IReadOnlyList<TargetGroup> TargetGroups,
        byte[] RawDecoded)
    {
        public bool TryContainsTargetGroup(int groupIndex, float x, float y, float z, out bool contains)
        {
            contains = false;
            if ((uint)groupIndex >= (uint)TargetGroups.Count)
            {
                return false;
            }

            TargetGroup group = TargetGroups[groupIndex];
            float dx = x - group.Center.X;
            float dy = y - group.Center.Y;
            float dz = z - group.Center.Z;
            float distanceSquared = dx * dx + dy * dy + dz * dz;
            float radiusSquared = group.Radius * group.Radius;
            if (distanceSquared > radiusSquared)
            {
                return true;
            }

            if (group.UnknownList2.Count > 0 || group.UnknownList3.Count > 0 || group.UnknownList4.Count > 0)
            {
                return false;
            }

            foreach (int polygonIndex in group.PolygonRegionIndices)
            {
                if (TargetPolygons[polygonIndex].Contains(x, y))
                {
                    contains = true;
                    return true;
                }
            }

            foreach (int volumeIndex in group.OrientedVolumeIndices)
            {
                if (TargetVolumes[volumeIndex].Contains(x, y, z))
                {
                    contains = true;
                    return true;
                }
            }

            return true;
        }
    }

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
                float F(int relative)
                {
                    float value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + relative));
                    if (!float.IsFinite(value))
                    {
                        throw new InvalidDataException(
                            $"UYA target volume {i} has a non-finite float at +0x{relative:x}.");
                    }
                    return value;
                }

                result.Add(new TargetVolume(
                    i,
                    (F(0x30), F(0x34), F(0x38)),
                    (F(0x40), F(0x44), F(0x48)),
                    (F(0x50), F(0x54), F(0x58)),
                    (F(0x60), F(0x64), F(0x68)),
                    data.AsSpan(at, TargetVolumeBytes).ToArray()));
            }
            return result;
        }

        List<TargetPolygon> TargetPolygons()
        {
            int block = OptionalPointer(TargetPolygonPtr, "target polygon");
            if (block == 0) return [];
            if (block + 0x10 > data.Length)
                throw new InvalidDataException("UYA target-polygon header is truncated.");

            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(block));
            int dataRelative = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(block + 4));
            int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(block + 8));
            long tableEnd = (long)block + 0x10L + (long)count * sizeof(int);
            long polygonData = (long)block + dataRelative;
            long polygonEnd = polygonData + dataBytes;
            if (count < 0 || count > 200_000 || dataRelative < 0 || dataBytes < 0
                || tableEnd > data.Length || polygonData < tableEnd || polygonEnd > data.Length)
            {
                throw new InvalidDataException(
                    $"UYA target-polygon count/extent is invalid ({count}, rel=0x{dataRelative:x}, bytes=0x{dataBytes:x}).");
            }

            var offsets = new int[count];
            for (int i = 0; i < count; i++)
            {
                offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(
                    data.AsSpan(block + 0x10 + i * sizeof(int)));
                if (offsets[i] < 0 || offsets[i] >= dataBytes)
                {
                    throw new InvalidDataException(
                        $"UYA target polygon {i} offset 0x{offsets[i]:x} lies outside its data blob.");
                }
                if (i > 0 && offsets[i] < offsets[i - 1])
                {
                    throw new InvalidDataException(
                        $"UYA target polygon offsets are not monotonic at {i}.");
                }
            }

            var result = new List<TargetPolygon>(count);
            for (int i = 0; i < count; i++)
            {
                int at = checked((int)polygonData + offsets[i]);
                int next = checked((int)polygonData + (i + 1 < count ? offsets[i + 1] : dataBytes));
                if (at + 0x10 > next)
                    throw new InvalidDataException($"UYA target polygon {i} header overlaps the next record.");

                int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
                long vertexEnd = (long)at + 0x10L + (long)vertexCount * 0x10L;
                if (vertexCount < 0 || vertexCount > 200_000 || vertexEnd > next)
                {
                    throw new InvalidDataException(
                        $"UYA target polygon {i} vertex count/extent is invalid ({vertexCount}).");
                }

                var vertices = new TargetPolygonVertex[vertexCount];
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    int v = at + 0x10 + vertex * 0x10;
                    float F(int relative)
                    {
                        float value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(v + relative));
                        if (!float.IsFinite(value))
                        {
                            throw new InvalidDataException(
                                $"UYA target polygon {i} vertex {vertex} has a non-finite component.");
                        }
                        return value;
                    }
                    vertices[vertex] = new TargetPolygonVertex(F(0), F(4), F(8), F(12));
                }

                result.Add(new TargetPolygon(
                    i,
                    vertices,
                    data.AsSpan(at, next - at).ToArray()));
            }

            return result;
        }

        List<TargetGroup> TargetGroups(
            IReadOnlyList<TargetVolume> volumes,
            IReadOnlyList<TargetPolygon> polygons)
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

                int[] polygonIndices = ReadList(0);
                int[] orientedVolumes = ReadList(1);
                if (polygonIndices.Any(index => index < 0 || index >= polygons.Count))
                {
                    throw new InvalidDataException(
                        $"UYA target group {i} references an out-of-range target polygon.");
                }
                if (orientedVolumes.Any(index => index < 0 || index >= volumes.Count))
                {
                    throw new InvalidDataException(
                        $"UYA target group {i} references an out-of-range target volume.");
                }

                result.Add(new TargetGroup(
                    i,
                    (F(0x00), F(0x04), F(0x08)),
                    radius,
                    polygonIndices,
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
        List<TargetPolygon> targetPolygons = TargetPolygons();
        List<TargetGroup> targetGroups = TargetGroups(targetVolumes, targetPolygons);
        return new Gameplay(
            Matrices(TiePtr, TieBytes, "TIE instances"),
            Matrices(ShrubPtr, ShrubBytes, "shrub instances"),
            mobies,
            targetVolumes,
            targetPolygons,
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

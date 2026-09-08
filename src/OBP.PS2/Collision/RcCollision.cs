using System.Buffers.Binary;
using OBP.Core.Math;

namespace OBP.PS2.Collision;

/// <summary>
/// The octree collision mesh format shared by the PS2 Ratchet &amp; Clank games
/// (one <c>read_collision</c> entry point for RC1/2/3/Deadlocked). Translated
/// from <c>reference-ts/packages/rc-collision</c>. Positions are native world
/// space (Z-up); quads are triangulated and the native face <c>type</c> byte is
/// kept per triangle. See <c>research/GC_COLLISION.md</c>.
/// </summary>
public static class RcCollision
{
    public const int HeaderSize = 8;
    public const int OctantUnits = 4;

    public sealed record Octant(
        int Index, int ByteOffset, int GridX, int GridY, int GridZ,
        int OriginX, int OriginY, int OriginZ,
        int VertexCount, int FaceCount, int QuadCount, int FirstVertex);

    public readonly record struct Triangle(int A, int B, int C, int MaterialId, int OctantIndex, bool FromQuad);

    public sealed record Mesh(
        int MeshOffset,
        int HeroGroupsOffset,
        int HeroGroupCount,
        IReadOnlyList<Octant> Octants,
        double[] Positions,
        IReadOnlyList<Triangle> Triangles,
        ObpBounds Bounds,
        IReadOnlyList<int> MaterialIds);

    public sealed record Options(int MaxOctants = 200_000, int MaxVertices = 8_000_000, int MaxTriangles = 8_000_000);

    public static Mesh Read(byte[] bytes, Options? options = null)
    {
        options ??= new Options();
        if (bytes.Length < HeaderSize)
        {
            throw new InvalidDataException("RC collision source shorter than the 8-byte header.");
        }

        int meshOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan());
        int heroGroupsOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        if (meshOffset < HeaderSize || meshOffset >= bytes.Length)
        {
            throw new InvalidDataException($"RC collision meshOffset 0x{meshOffset:x} is out of range.");
        }

        if (heroGroupsOffset != 0 && (heroGroupsOffset < meshOffset || heroGroupsOffset > bytes.Length))
        {
            throw new InvalidDataException($"RC collision heroGroupsOffset 0x{heroGroupsOffset:x} is out of range.");
        }

        int meshEnd = heroGroupsOffset != 0 ? heroGroupsOffset : bytes.Length;

        byte U8(int at) => at >= 0 && at < bytes.Length ? bytes[at] : throw new InvalidDataException($"RC collision read past end at {at}.");
        short S16(int at) => at >= 0 && at + 2 <= bytes.Length ? BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at)) : throw new InvalidDataException($"RC collision read past end at {at}.");
        ushort U16(int at) => at >= 0 && at + 2 <= bytes.Length ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)) : throw new InvalidDataException($"RC collision read past end at {at}.");
        uint U32(int at) => at >= 0 && at + 4 <= bytes.Length ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : throw new InvalidDataException($"RC collision read past end at {at}.");

        var octants = new List<Octant>();
        var positionList = new List<double>();
        var triangles = new List<Triangle>();
        var materialIds = new SortedSet<int>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        int zCoord = S16(meshOffset);
        int zCount = U16(meshOffset + 2);
        int zOffsets = meshOffset + 4;

        for (int z = 0; z < zCount; z++)
        {
            int zByte = U16(zOffsets + z * 2) * 4;
            if (zByte == 0)
            {
                continue;
            }

            int zNode = meshOffset + zByte;
            int yCoord = S16(zNode);
            int yCount = U16(zNode + 2);
            int yOffsets = zNode + 4;

            for (int y = 0; y < yCount; y++)
            {
                uint yByte = U32(yOffsets + y * 4);
                if (yByte == 0)
                {
                    continue;
                }

                int yNode = meshOffset + (int)yByte;
                int xCoord = S16(yNode);
                int xCount = U16(yNode + 2);
                int xOffsets = yNode + 4;

                for (int x = 0; x < xCount; x++)
                {
                    uint octByte = U32(xOffsets + x * 4) >> 8;
                    if (octByte == 0)
                    {
                        continue;
                    }

                    int octOffset = meshOffset + (int)octByte;
                    if (octOffset < meshOffset || octOffset + 4 > meshEnd)
                    {
                        throw new InvalidDataException($"RC collision octant offset 0x{octByte:x} out of mesh range.");
                    }

                    int faceCount = U16(octOffset);
                    int vertexCount = U8(octOffset + 2);
                    int quadCount = U8(octOffset + 3);
                    if (quadCount > faceCount)
                    {
                        throw new InvalidDataException("RC collision octant quad count exceeds face count.");
                    }

                    if (octants.Count >= options.MaxOctants ||
                        positionList.Count / 3 + vertexCount > options.MaxVertices ||
                        triangles.Count + faceCount + quadCount > options.MaxTriangles)
                    {
                        throw new InvalidDataException("RC collision exceeds a safety cap.");
                    }

                    int gridX = xCoord + x;
                    int gridY = yCoord + y;
                    int gridZ = zCoord + z;
                    int originX = gridX * OctantUnits + 2;
                    int originY = gridY * OctantUnits + 2;
                    int originZ = gridZ * OctantUnits + 2;

                    int firstVertex = positionList.Count / 3;
                    int cursor = octOffset + 4;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        int packed = (int)U32(cursor);
                        cursor += 4;
                        double lx = ((packed << 22) >> 22) / 16.0;
                        double ly = ((packed << 12) >> 22) / 16.0;
                        double lz = ((packed << 0) >> 20) / 64.0;
                        double wx = originX + lx, wy = originY + ly, wz = originZ + lz;
                        positionList.Add(wx);
                        positionList.Add(wy);
                        positionList.Add(wz);
                        if (wx < minX) minX = wx;
                        if (wy < minY) minY = wy;
                        if (wz < minZ) minZ = wz;
                        if (wx > maxX) maxX = wx;
                        if (wy > maxY) maxY = wy;
                        if (wz > maxZ) maxZ = wz;
                    }

                    int faceBase = cursor;
                    int quadBase = faceBase + faceCount * 4;
                    if (quadBase + quadCount > meshEnd)
                    {
                        throw new InvalidDataException("RC collision octant face table runs past the mesh.");
                    }

                    for (int f = 0; f < faceCount; f++)
                    {
                        int fo = faceBase + f * 4;
                        int v0 = U8(fo), v1 = U8(fo + 1), v2 = U8(fo + 2), type = U8(fo + 3);
                        bool isQuad = f < quadCount;
                        int v3 = isQuad ? U8(quadBase + f) : 0;
                        foreach (int vi in isQuad ? new[] { v0, v1, v2, v3 } : new[] { v0, v1, v2 })
                        {
                            if (vi >= vertexCount)
                            {
                                throw new InvalidDataException($"RC collision face vertex index {vi} >= octant vertex count {vertexCount}.");
                            }
                        }

                        materialIds.Add(type);
                        triangles.Add(new Triangle(firstVertex + v0, firstVertex + v1, firstVertex + v2, type, octants.Count, isQuad));
                        if (isQuad)
                        {
                            triangles.Add(new Triangle(firstVertex + v0, firstVertex + v2, firstVertex + v3, type, octants.Count, true));
                        }
                    }

                    octants.Add(new Octant(octants.Count, octOffset, gridX, gridY, gridZ, originX, originY, originZ, vertexCount, faceCount, quadCount, firstVertex));
                }
            }
        }

        int heroGroupCount = 0;
        if (heroGroupsOffset != 0 && heroGroupsOffset + 4 <= bytes.Length)
        {
            heroGroupCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(heroGroupsOffset));
            if (heroGroupCount is < 0 or > 4096)
            {
                heroGroupCount = 0;
            }
        }

        var bounds = positionList.Count == 0
            ? new ObpBounds(Vec3.Zero, Vec3.Zero)
            : new ObpBounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));

        return new Mesh(meshOffset, heroGroupsOffset, heroGroupCount, octants, positionList.ToArray(), triangles, bounds, materialIds.ToList());
    }
}

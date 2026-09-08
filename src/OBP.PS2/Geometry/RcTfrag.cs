using System.Buffers.Binary;
using OBP.PS2.Vif;

namespace OBP.PS2.Geometry;

/// <summary>
/// Static terrain-fragment geometry shared by the PS2 Ratchet &amp; Clank line.
/// R&amp;C1 retail archaeology independently validates the same inner packet grammar
/// as Going Commando across all 19 authority levels. The enclosing level/core
/// containers remain game-specific; only this inner codec is shared.
///
/// Positions are <c>base + s16 delta</c> / 1024, UVs are s16 / 4096, baked
/// vertex colours are preserved, and each recovered triangle retains its native
/// texture-table id. See the game-specific research documents for the evidence
/// establishing compatibility.
/// </summary>
public static class RcTfrag
{
    public const int TfragHeaderSize = 0x40;
    private const int Sector = 0x10;

    public sealed record Mesh(
        double[] Positions,
        float[] Uvs,
        float[] Colors,
        int[] Indices,
        int[] TriangleTextureIds,
        int TfragCount,
        (double X, double Y, double Z) BoundsMin,
        (double X, double Y, double Z) BoundsMax,
        IReadOnlyList<int> TextureIds);

    public sealed record Options(int MaxVertices = 12_000_000, int MaxTriangles = 12_000_000, bool ToYUp = true);

    private readonly record struct VertexInfo(int S, int T, int Parent, int Vertex);

    public static Mesh Read(byte[] blob, Options? options = null)
    {
        options ??= new Options();
        if (blob.Length < 0x10)
        {
            throw new InvalidDataException("RC tfrags blob shorter than the 16-byte header.");
        }

        int tableOffset = BinaryPrimitives.ReadInt32LittleEndian(blob);
        int tfragCount = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
        if (tableOffset < 0x10 || tableOffset > blob.Length)
        {
            throw new InvalidDataException($"RC tfrags tableOffset 0x{tableOffset:x} out of range.");
        }

        if (tfragCount < 0 || (long)tableOffset + (long)tfragCount * TfragHeaderSize > blob.Length)
        {
            throw new InvalidDataException($"RC tfrags count {tfragCount} out of range.");
        }

        var positions = new List<double>();
        var uvs = new List<float>();
        var colors = new List<float>();
        var indices = new List<int>();
        var triangleTextureIds = new List<int>();
        var textureIdSet = new SortedSet<int>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (int t = 0; t < tfragCount; t++)
        {
            int ho = tableOffset + t * TfragHeaderSize;
            int data = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(ho + 0x10));
            int sharedOfs = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(ho + 0x16));
            int lod1Ofs = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(ho + 0x18));
            int lod0Ofs = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(ho + 0x1a));
            int rgbaOfs = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(ho + 0x1e));
            int commonSize = blob[ho + 0x20];
            int lod2Size = blob[ho + 0x21];
            int lod1Size = blob[ho + 0x22];
            int rgbaSize = blob[ho + 0x29];

            int dataStart = tableOffset + data;
            if (dataStart < 0 || dataStart > blob.Length)
            {
                continue;
            }

            ArraySegment<byte> Sub(int start, int end)
            {
                int from = System.Math.Clamp(dataStart + start, 0, blob.Length);
                int to = System.Math.Clamp(dataStart + end, from, blob.Length);
                return new ArraySegment<byte>(blob, from, to - from);
            }

            var commonList = Vif.ReadCommandList(Sub(sharedOfs, lod1Ofs));
            var commonUnpacks = Vif.FilterUnpacks(commonList);
            if (commonUnpacks.Count < 4)
            {
                continue;
            }

            if (commonList.Count < 6 || commonList[5].Data.Count < 12)
            {
                continue;
            }

            var strow = commonList[5].Data;
            int baseX = BinaryPrimitives.ReadInt32LittleEndian(strow.AsSpan(0));
            int baseY = BinaryPrimitives.ReadInt32LittleEndian(strow.AsSpan(4));
            int baseZ = BinaryPrimitives.ReadInt32LittleEndian(strow.AsSpan(8));

            var textures = ReadTexturePrimitives(commonUnpacks[1].Data);
            var commonVertexInfo = ReadVertexInfo(commonUnpacks[2].Data);
            var commonPositions = ReadPositions(commonUnpacks[3].Data);

            var lod01List = Vif.ReadCommandList(Sub(lod0Ofs, sharedOfs + lod1Size * Sector));
            var lod01Unpacks = Vif.FilterUnpacks(lod01List);
            var lod01Positions = new List<int[]>();
            var lod01VertexInfo = new List<VertexInfo>();
            {
                int i = 0;
                while (i < lod01Unpacks.Count && lod01Unpacks[i].Code.Vnvl == Vif.UnpackV4_8)
                {
                    i++;
                }

                if (i < lod01Unpacks.Count && lod01Unpacks[i].Code.Vnvl == Vif.UnpackV4_16)
                {
                    lod01VertexInfo = ReadVertexInfo(lod01Unpacks[i++].Data);
                }

                if (i < lod01Unpacks.Count && lod01Unpacks[i].Code.Vnvl == Vif.UnpackV3_16)
                {
                    lod01Positions = ReadPositions(lod01Unpacks[i++].Data);
                }
            }

            int lod0Start = sharedOfs + lod1Size * Sector;
            int lod0Size = rgbaOfs - (lod1Size + lod2Size - commonSize) * Sector;
            var lod0List = Vif.ReadCommandList(Sub(lod0Start, lod0Start + System.Math.Max(0, lod0Size)));
            var lod0Unpacks = Vif.FilterUnpacks(lod0List);
            var lod0Positions = new List<int[]>();
            var lod0VertexInfo = new List<VertexInfo>();
            byte[] strips = [];
            byte[] stripIndices = [];
            {
                int i = 0;
                if (i < lod0Unpacks.Count && lod0Unpacks[i].Code.Vnvl == Vif.UnpackV3_16)
                {
                    lod0Positions = ReadPositions(lod0Unpacks[i++].Data);
                }

                if (i < lod0Unpacks.Count)
                {
                    strips = lod0Unpacks[i++].Data.ToArray();
                }

                if (i < lod0Unpacks.Count)
                {
                    stripIndices = lod0Unpacks[i++].Data.ToArray();
                }

                while (i < lod0Unpacks.Count && lod0Unpacks[i].Code.Vnvl == Vif.UnpackV4_8)
                {
                    i++;
                }

                if (i < lod0Unpacks.Count && lod0Unpacks[i].Code.Vnvl == Vif.UnpackV4_16)
                {
                    lod0VertexInfo = ReadVertexInfo(lod0Unpacks[i++].Data);
                }
            }

            if (strips.Length == 0 || stripIndices.Length == 0)
            {
                continue;
            }

            var allPositions = new List<int[]>(commonPositions);
            allPositions.AddRange(lod01Positions);
            allPositions.AddRange(lod0Positions);
            var allVertexInfo = new List<VertexInfo>(commonVertexInfo);
            allVertexInfo.AddRange(lod01VertexInfo);
            allVertexInfo.AddRange(lod0VertexInfo);
            var rgbas = ReadRgbas(blob, dataStart + rgbaOfs, rgbaSize * 4);

            int vertexBase = positions.Count / 3;
            foreach (var info in allVertexInfo)
            {
                int pi = info.Vertex >> 1;
                int[] p = pi >= 0 && pi < allPositions.Count ? allPositions[pi] : [0, 0, 0];
                double x = (baseX + p[0]) / 1024.0;
                double y = (baseY + p[1]) / 1024.0;
                double z = (baseZ + p[2]) / 1024.0;
                if (options.ToYUp)
                {
                    (y, z) = (z, y);
                }

                positions.Add(x);
                positions.Add(y);
                positions.Add(z);
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;

                double s = info.S / 4096.0;
                double tc = info.T / 4096.0;
                if (s < 0) s *= 0.5;
                if (tc < 0) tc *= 0.5;
                uvs.Add((float)s);
                uvs.Add((float)tc);

                if (pi >= 0 && pi < rgbas.Count)
                {
                    colors.Add(rgbas[pi][0] / 255f);
                    colors.Add(rgbas[pi][1] / 255f);
                    colors.Add(rgbas[pi][2] / 255f);
                }
                else
                {
                    colors.Add(0.7f);
                    colors.Add(0.7f);
                    colors.Add(0.7f);
                }
            }

            if (positions.Count / 3 > options.MaxVertices)
            {
                throw new InvalidDataException($"RC tfrags exceeds maxVertices {options.MaxVertices}.");
            }

            foreach (var (tri, texId) in RecoverFaces(strips, stripIndices, textures))
            {
                if (tri.A >= allVertexInfo.Count || tri.B >= allVertexInfo.Count || tri.C >= allVertexInfo.Count)
                {
                    continue;
                }

                indices.Add(vertexBase + tri.A);
                indices.Add(vertexBase + tri.B);
                indices.Add(vertexBase + tri.C);
                triangleTextureIds.Add(texId);
                if (texId >= 0)
                {
                    textureIdSet.Add(texId);
                }

                if (indices.Count / 3 > options.MaxTriangles)
                {
                    throw new InvalidDataException($"RC tfrags exceeds maxTriangles {options.MaxTriangles}.");
                }
            }
        }

        bool empty = positions.Count == 0;
        return new Mesh(
            positions.ToArray(), uvs.ToArray(), colors.ToArray(), indices.ToArray(), triangleTextureIds.ToArray(),
            tfragCount,
            empty ? (0, 0, 0) : (minX, minY, minZ),
            empty ? (0, 0, 0) : (maxX, maxY, maxZ),
            textureIdSet.ToList());
    }

    private static List<int[]> ReadPositions(ArraySegment<byte> data)
    {
        var span = data.AsSpan();
        var outp = new List<int[]>();
        for (int o = 0; o + 6 <= span.Length; o += 6)
        {
            outp.Add([
                BinaryPrimitives.ReadInt16LittleEndian(span[o..]),
                BinaryPrimitives.ReadInt16LittleEndian(span[(o + 2)..]),
                BinaryPrimitives.ReadInt16LittleEndian(span[(o + 4)..]),
            ]);
        }

        return outp;
    }

    private static List<VertexInfo> ReadVertexInfo(ArraySegment<byte> data)
    {
        var span = data.AsSpan();
        var outp = new List<VertexInfo>();
        for (int o = 0; o + 8 <= span.Length; o += 8)
        {
            outp.Add(new VertexInfo(
                BinaryPrimitives.ReadInt16LittleEndian(span[o..]),
                BinaryPrimitives.ReadInt16LittleEndian(span[(o + 2)..]),
                BinaryPrimitives.ReadInt16LittleEndian(span[(o + 4)..]),
                BinaryPrimitives.ReadInt16LittleEndian(span[(o + 6)..])));
        }

        return outp;
    }

    private static List<int> ReadTexturePrimitives(ArraySegment<byte> data)
    {
        var span = data.AsSpan();
        var outp = new List<int>();
        for (int o = 0; o + 0x50 <= span.Length; o += 0x50)
        {
            outp.Add(BinaryPrimitives.ReadInt32LittleEndian(span[o..]));
        }

        return outp;
    }

    private static List<byte[]> ReadRgbas(byte[] blob, int offset, int count)
    {
        var outp = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            int o = offset + i * 4;
            if (o + 3 > blob.Length)
            {
                break;
            }

            outp.Add([blob[o], blob[o + 1], blob[o + 2]]);
        }

        return outp;
    }

    private static IEnumerable<((int A, int B, int C) Tri, int TexId)> RecoverFaces(byte[] strips, byte[] indices, List<int> textures)
    {
        int activeAdGif = -1;
        int next = 0;
        int Idx(int i) => i >= 0 && i < indices.Length ? indices[i] : 0;
        int TexFor() => activeAdGif >= 0 && activeAdGif < textures.Count ? textures[activeAdGif] : -1;

        for (int s = 0; s + 4 <= strips.Length; s += 4)
        {
            int vc = (sbyte)strips[s];
            int adGifOffset = (sbyte)strips[s + 2];
            if (vc <= 0)
            {
                if (vc == 0)
                {
                    break;
                }

                if (adGifOffset >= 0)
                {
                    activeAdGif = adGifOffset / 5;
                }

                vc += 128;
            }

            if (next + vc > indices.Length)
            {
                break;
            }

            if (vc % 2 == 0)
            {
                for (int i = 0; i + 4 <= vc; i += 2)
                {
                    int q0 = Idx(next + i), q1 = Idx(next + i + 1), q2 = Idx(next + i + 2), q3 = Idx(next + i + 3);
                    yield return ((q2, q3, q1), TexFor());
                    yield return ((q2, q1, q0), TexFor());
                }
            }
            else
            {
                for (int i = 0; i + 3 <= vc; i++)
                {
                    int t0 = Idx(next + i), t1 = Idx(next + i + 1), t2 = Idx(next + i + 2);
                    yield return ((i & 1) != 0 ? (t1, t0, t2) : (t0, t1, t2), TexFor());
                }
            }

            next += vc;
        }
    }
}

using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC1.Level;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;

namespace OBP.Tests;

/// <summary>
/// Packet-level strip winding and retail foliage regression for shared RAC1/GC decoding.
/// No retail bytes are stored with these tests.
/// </summary>
public sealed class RcShrubStripTests
{
    [Fact]
    public void GifStrip_ReversesEveryOtherSharedEdgeAndRestartsWithEachTag()
    {
        var mesh = RcShrub.ReadClass(SyntheticClass(lastIsStrip: true, lastVertexCount: 2));

        // A four-vertex strip, then a fresh six-vertex strip. The final two-vertex
        // primitive must not produce a triangle or carry parity into another tag.
        Assert.Equal(new[]
        {
            0, 1, 2, 2, 1, 3,
            4, 5, 6, 6, 5, 7, 6, 7, 8, 8, 7, 9,
        }, mesh.Indices);
        Assert.Equal(10, mesh.Positions.Length / 3);
        Assert.Equal(6, mesh.Indices.Length / 3);
        Assert.Equal(new[] { 5, 5, 9, 9, 9, 9 }, mesh.TriangleMaterialSlots);
        Assert.Equal(new[] { 0, 0, 1, 1, 1, 1 }, mesh.TriangleMaterialStateIndices);
        Assert.Equal(new bool?[] { true, true, false, false, false, false }, mesh.TriangleAlphaBlendEnabled);
        Assert.Equal(new[] { 5, 9, 13 }, mesh.Materials.Select(m => m.TextureId));

        Assert.Equal(4, DirectedSharedEdges(mesh.Indices));
        Assert.Equal(0, ConflictingDirectedEdges(mesh.Indices));
        for (int triangle = 0; triangle < mesh.Indices.Length / 3; triangle++)
            Assert.True(SignedAreaXY(mesh.Positions, mesh.Indices, triangle) > 0,
                $"Triangle {triangle} should face the same way in native coordinates");
    }

    [Fact]
    public void GifTriangleList_KeepsOriginalOrderAndIgnoresIncompleteTail()
    {
        var mesh = RcShrub.ReadClass(SyntheticClass(lastIsStrip: false, lastVertexCount: 4));
        Assert.Equal(new[] { 10, 11, 12 }, mesh.Indices[^3..]);
        Assert.Equal(7, mesh.Indices.Length / 3);
        Assert.Equal(13, mesh.TriangleMaterialSlots[^1]);
        Assert.Equal(2, mesh.TriangleMaterialStateIndices[^1]);
        Assert.True(mesh.TriangleAlphaBlendEnabled[^1]);
    }

    [SkippableTheory]
    [InlineData(0, 33, 12_244)]
    [InlineData(1, 51, 0)]
    public void Rac1RetailFoliage_StripsHaveOppositeSharedEdgesAndUnchangedTriangleCount(
        int levelId, int expectedClasses, int expectedTriangles)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == levelId);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level)).Shrubs;
        Assert.Equal(expectedClasses, classes.Count);
        if (expectedTriangles > 0)
            Assert.Equal(expectedTriangles, classes.Values.Sum(c => c.Mesh.Indices.Length / 3));

        int observed = 0;
        foreach (var cls in classes.Values)
        {
            var mesh = cls.Mesh;
            Assert.Equal(mesh.Indices.Length / 3, mesh.TriangleMaterialSlots.Length);
            Assert.Equal(mesh.Indices.Length / 3, mesh.TriangleMaterialStateIndices.Length);
            Assert.Equal(mesh.Indices.Length / 3, mesh.TriangleAlphaBlendEnabled.Length);
            Assert.Equal(mesh.Indices.Length / 3, cls.TriangleTextureIds.Length);
            Assert.Equal(0, ConflictingDirectedEdges(mesh.Indices));
            if (DirectedSharedEdges(mesh.Indices) > 0)
                observed++;
        }

        Assert.True(observed >= (levelId == 0 ? 33 : 49),
            $"Level {levelId}: only {observed} shrub classes had adjacent strip triangles");
    }

    [SkippableFact]
    public void GoingCommandoRetailFoliage_SharedDecoderPreservesMaterialAndWinding()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD") ?? throw new FileNotFoundException("/G/LEVEL1.WAD");
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2Shrub.ReadClasses(core);
        Assert.Equal(24, classes.Count);
        Assert.True(classes.Values.Sum(c => DirectedSharedEdges(c.Mesh.Indices)) > 0);
        foreach (var cls in classes.Values)
        {
            Assert.Equal(0, ConflictingDirectedEdges(cls.Mesh.Indices));
            Assert.Equal(cls.Mesh.Indices.Length / 3, cls.Mesh.TriangleMaterialSlots.Length);
            Assert.Equal(cls.Mesh.Indices.Length / 3, cls.Mesh.TriangleMaterialStateIndices.Length);
            Assert.Equal(cls.Mesh.Indices.Length / 3, cls.TriangleTextureIds.Length);
        }
    }

    private static double SignedAreaXY(double[] positions, int[] indices, int triangle)
    {
        int ia = indices[triangle * 3] * 3;
        int ib = indices[triangle * 3 + 1] * 3;
        int ic = indices[triangle * 3 + 2] * 3;
        return (positions[ib] - positions[ia]) * (positions[ic + 1] - positions[ia + 1]) -
               (positions[ib + 1] - positions[ia + 1]) * (positions[ic] - positions[ia]);
    }

    private static (int Edges, int Conflicts) EdgeCensus(int[] indices)
    {
        var edges = new Dictionary<(int, int), (int Forward, int Backward)>();
        for (int t = 0; t < indices.Length; t += 3)
        {
            for (int edge = 0; edge < 3; edge++)
            {
                int a = indices[t + edge], b = indices[t + (edge + 1) % 3];
                if (a == b) continue;
                var key = (System.Math.Min(a, b), System.Math.Max(a, b));
                edges.TryGetValue(key, out var counts);
                edges[key] = a < b ? (counts.Forward + 1, counts.Backward) :
                    (counts.Forward, counts.Backward + 1);
            }
        }

        int shared = 0, conflicts = 0;
        foreach (var pair in edges.Values)
        {
            if (pair.Forward + pair.Backward < 2) continue;
            shared++;
            if (pair.Forward == 0 || pair.Backward == 0) conflicts++;
        }

        return (shared, conflicts);
    }

    private static int DirectedSharedEdges(int[] indices) => EdgeCensus(indices).Edges;
    private static int ConflictingDirectedEdges(int[] indices) => EdgeCensus(indices).Conflicts;

    private static byte[] SyntheticClass(bool lastIsStrip, int lastVertexCount)
    {
        // Three VIF UNPACKs: 16-byte quadword header, and parallel 8-byte
        // position/UV records. Offset counters model native GIF+AD+vertex walk:
        // tag=1, material=5, vertex=3 qwords.
        int[] lengths = [4, 6, lastVertexCount];
        int[] tagOffsets = [0, 18, 42];
        int[] adOffsets = [1, 19, 43];
        int[][] coords =
        [
            [0, 0, 10, 0, 0, 10, 10, 10],
            [0, 20, 10, 20, 0, 30, 10, 30, 0, 40, 10, 40],
            [0, 50, 10, 50, 0, 60, 10, 60],
        ];
        var hd = new byte[0x100];
        BinaryPrimitives.WriteInt32LittleEndian(hd.AsSpan(0), 3);
        BinaryPrimitives.WriteInt32LittleEndian(hd.AsSpan(4), 3);
        BinaryPrimitives.WriteInt32LittleEndian(hd.AsSpan(8), lengths.Sum());
        for (int p = 0; p < 3; p++)
        {
            int tag = 0x10 + p * 0x10;
            int type = p == 2 && !lastIsStrip ? 0b011 : 0b100;
            int prim = type | (p != 1 ? 0x40 : 0);
            BinaryPrimitives.WriteUInt64LittleEndian(hd.AsSpan(tag),
                (1UL << 46) | ((ulong)prim << 47));
            BinaryPrimitives.WriteInt32LittleEndian(hd.AsSpan(tag + 0x0c), tagOffsets[p]);
            int mat = 0x10 + 3 * 0x10 + p * 0x40;
            BinaryPrimitives.WriteInt32LittleEndian(hd.AsSpan(mat + 0x0c), adOffsets[p]);
            BinaryPrimitives.WriteInt32LittleEndian(hd.AsSpan(mat + 0x30), p == 0 ? 5 : p == 1 ? 9 : 13);
        }

        int vertexCount = lengths.Sum();
        var d1 = new byte[vertexCount * 8];
        var d2 = new byte[vertexCount * 8];
        int vertex = 0;
        for (int p = 0; p < 3; p++)
        {
            int start = adOffsets[p] + 5;
            for (int k = 0; k < lengths[p]; k++, vertex++)
            {
                int at = vertex * 8;
                BinaryPrimitives.WriteInt16LittleEndian(d1.AsSpan(at), (short)coords[p][k * 2]);
                BinaryPrimitives.WriteInt16LittleEndian(d1.AsSpan(at + 2), (short)coords[p][k * 2 + 1]);
                BinaryPrimitives.WriteInt16LittleEndian(d1.AsSpan(at + 6), (short)(start + 3 * k));
                BinaryPrimitives.WriteInt16LittleEndian(d2.AsSpan(at), (short)(k * 256));
            }
        }

        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)((0x6c << 24) | (16 << 16)));
            writer.Write(hd);
            writer.Write((uint)((0x6d << 24) | (vertexCount << 16)));
            writer.Write(d1);
            writer.Write((uint)((0x6d << 24) | (vertexCount << 16)));
            writer.Write(d2);
        }

        var bytes = new byte[0x80 + packet.Length];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0x20), 1024f);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x28), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x40), 0x80);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x44), (int)packet.Length);
        packet.ToArray().CopyTo(bytes, 0x80);
        return bytes;
    }
}

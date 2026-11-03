using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.PS2.Vif;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Independent retail VIF index-push audit for the two Oozla Mobys saved in
/// Jess's view. Read-only: does not alter the renderer or any golden values.
/// </summary>
public sealed class Rac2OozlaTriangleSourceAuditTests(ITestOutputHelper output)
{
    private static readonly int[] TargetClasses = [2466, 2859];
    private readonly record struct Triangle(int A, int B, int C)
    {
        public static Triangle Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return new Triangle(a, b, c);
        }
    }

    [SkippableFact]
    public void RetailVifKickStreamVsImportedTriangles()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO is absent");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var hdr = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, hdr, 0));
        var classes = Rac2MobyClasses.Read(core, level: 1);
        var table = core.Header.MobyClasses;

        foreach (int id in TargetClasses)
        {
            var row = Enumerable.Range(0, table.Count)
                .Select(i => table.Offset + i * GcUyaMoby.ClassEntrySize)
                .Single(at => BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4)) == id);
            int begin = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(row));
            int end = core.SectionBoundaries.FirstOrDefault(b => b > begin, core.Assets.Length);
            byte[] data = core.Assets.AsSpan(begin, end - begin).ToArray();
            int packetTable = BinaryPrimitives.ReadInt32LittleEndian(data);
            int packets = data[4];

            var expected = new Dictionary<Triangle, int>();
            int globalVertexBase = 0, kicks = 0, suppressed = 0, degenerate = 0, packetSwitches = 0;
            for (int p = 0; p < packets; p++)
            {
                int entry = packetTable + p * 16;
                int vifOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entry));
                int vifBytes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry + 4)) * 16;
                int tableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entry + 8));
                int inFile = 0;
                for (int off = 2; off <= 6; off += 2)
                    inFile += BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(tableOffset + off));
                int duplicates = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(tableOffset + 8));
                int packetVertices = inFile + duplicates;
                var unpacks = Vif.FilterUnpacks(
                    Vif.ReadCommandList(new ArraySegment<byte>(data, vifOffset, vifBytes)));
                Assert.InRange(unpacks.Count, 2, 3);
                byte[] idx = unpacks[1].Data.ToArray();
                Assert.True(idx.Length >= 5, $"class {id} packet {p} has a short index stream");
                var secrets = new List<byte> { idx[2] };
                if (unpacks.Count == 3)
                {
                    var gs = unpacks[2].Data;
                    Assert.True(gs.Count % 64 == 0);
                    for (int i = 0; i < gs.Count / 64; i++)
                        secrets.Add(gs[i * 16 + 12]);
                }

                var pushes = new List<(int Vertex, bool Kick)>();
                int secret = 0, switches = 0;
                bool terminated = false;
                foreach (byte encoded in idx.AsSpan(4).ToArray())
                {
                    byte code = encoded;
                    if (code == 0)
                    {
                        Assert.True(secret < secrets.Count, $"class {id} packet {p} exhausted extra indices");
                        code = secrets[secret++];
                        if (code == 0) { terminated = true; break; }
                        switches++;
                        code |= 0x80; // Texture switch pushes, but never draws.
                    }
                    int vertex = (code & 0x7f) - 1;
                    Assert.InRange(vertex, 0, packetVertices - 1);
                    pushes.Add((vertex, (code & 0x80) == 0));
                }
                Assert.True(terminated, $"class {id} packet {p} is unterminated");
                Assert.True(pushes.Count >= 3);
                Assert.Equal(new[] { 0, 0, 0 }, pushes.TakeLast(3).Select(q => q.Vertex));
                pushes.RemoveRange(pushes.Count - 3, 3);
                packetSwitches += switches;
                for (int i = 2; i < pushes.Count; i++)
                {
                    if (!pushes[i].Kick) { suppressed++; continue; }
                    kicks++;
                    var tri = Triangle.Sorted(
                        globalVertexBase + pushes[i - 2].Vertex,
                        globalVertexBase + pushes[i - 1].Vertex,
                        globalVertexBase + pushes[i].Vertex);
                    if (tri.A == tri.B || tri.B == tri.C) { degenerate++; continue; }
                    expected[tri] = expected.GetValueOrDefault(tri) + 1;
                }
                globalVertexBase += packetVertices;
            }

            var mesh = classes[id].Mesh;
            Assert.Equal(globalVertexBase, mesh.Positions.Length / 3);
            var actual = new Dictionary<Triangle, int>();
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                var t = Triangle.Sorted(mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]);
                actual[t] = actual.GetValueOrDefault(t) + 1;
            }

            int missing = expected.Sum(x => Math.Max(0, x.Value - actual.GetValueOrDefault(x.Key)));
            int extra = actual.Sum(x => Math.Max(0, x.Value - expected.GetValueOrDefault(x.Key)));
            output.WriteLine($"class={id}: packets={packets}, vertices={globalVertexBase}, " +
                $"source_kicks={kicks}, suppressed={suppressed}, degenerates={degenerate}, " +
                $"texture_switches={packetSwitches}, source_faces={expected.Values.Sum()}, " +
                $"obp_faces={mesh.Indices.Length / 3}, missing={missing}, extra={extra}");
            foreach (var pair in expected.Where(x => x.Value > actual.GetValueOrDefault(x.Key)).Take(12))
                output.WriteLine($"  MISSING source=({pair.Key.A},{pair.Key.B},{pair.Key.C}) count={pair.Value} obp={actual.GetValueOrDefault(pair.Key)}");
            foreach (var pair in actual.Where(x => x.Value > expected.GetValueOrDefault(x.Key)).Take(12))
                output.WriteLine($"  EXTRA obp=({pair.Key.A},{pair.Key.B},{pair.Key.C}) count={pair.Value} source={expected.GetValueOrDefault(pair.Key)}");
            Assert.True(expected.Count > 0);
            Assert.Equal(0, missing);
            Assert.Equal(0, extra);
        }
    }
}

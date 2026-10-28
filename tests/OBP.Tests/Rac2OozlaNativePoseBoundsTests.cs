using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Read-only geometry/plausibility audit for the isolated native pose evaluator.
/// Bounding spheres are retail-authored sequence data, not a visual signoff.
/// </summary>
public sealed class Rac2OozlaNativePoseBoundsTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void NativePoseSequenceSphereCoverage()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO missing");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var hdr = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, hdr, 0));
        var table = core.Header.MobyClasses;
        var classes = Rac2MobyClasses.Read(core, level: 1);
        foreach (int id in new[] { 2466, 2859 })
        {
            int at = Enumerable.Range(0, table.Count)
                .Select(i => table.Offset + i * GcUyaMoby.ClassEntrySize)
                .Single(p => BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(p + 4)) == id);
            int start = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int end = core.SectionBoundaries.FirstOrDefault(b => b > start, core.Assets.Length);
            var source = core.Assets.AsSpan(start, end - start).ToArray();
            var moby = classes[id];
            int totalVertices = 0, totalOutside = 0, totalFrames = 0;
            double worst = 0;
            foreach (var seq in moby.Sequences)
            {
                if (seq.BoundingSphere is not { } bound || seq.Frames.Count == 0) continue;
                double k = moby.Mesh.Scale / 1024.0;
                double cx = bound.X * k, cy = bound.Y * k, cz = bound.Z * k, r = Math.Abs(bound.Radius * k);
                if (r <= 0) continue;
                int outside = 0, count = 0;
                double maxDistance = 0, maxSpan = 0;
                foreach (var frame in seq.Frames)
                {
                    var positions = Ps2MobyNativePose.Pose(source, moby.Mesh, frame);
                    double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity,
                           maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
                    for (int v = 0; v < positions.Length; v += 3)
                    {
                        double x = positions[v], y = positions[v + 1], z = positions[v + 2];
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                        double dx = x - cx, dy = y - cy, dz = z - cz;
                        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        maxDistance = Math.Max(maxDistance, distance);
                        if (distance > r * 1.001) outside++;
                        count++;
                    }
                    maxSpan = Math.Max(maxSpan, Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)));
                    totalFrames++;
                }
                totalVertices += count; totalOutside += outside;
                double escape = (double)outside / count;
                worst = Math.Max(worst, escape);
                output.WriteLine($"class={id} seq={seq.Index} frames={seq.Frames.Count} sphere_r={r:F3} max_dist={maxDistance:F3} max_span={maxSpan:F3} outside={outside}/{count} ({escape:P4})");
            }
            output.WriteLine($"SUMMARY class={id} frames={totalFrames} outside={totalOutside}/{totalVertices} fraction={(double)totalOutside / totalVertices:P4}, worst sequence={worst:P4}");
            Assert.True(totalFrames > 100);
            Assert.True((double)totalOutside / totalVertices < 0.001,
                $"Class {id} has too many authored-sphere outliers: {totalOutside}/{totalVertices}");
        }
    }
}

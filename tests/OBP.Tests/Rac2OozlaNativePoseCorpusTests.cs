using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Read-only native-frame pose census of other Oozla Moby classes. Keeps
/// source-scope admissions explicit; does not modify meshes or renderer.
/// </summary>
public sealed class Rac2OozlaNativePoseCorpusTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void GeneralizedPoseEvaluatorHandlesAdditionalRetailClasses()
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
        int eligible = 0, checkedFrames = 0, sphereEscapes = 0, checkedVerts = 0, unsupported = 0;
        double largestRadiusMultiple = 0;
        int worstClass = -1, worstSequence = -1;
        var failures = new List<string>();
        foreach (var cls in classes.Values)
        {
            if (cls.OClass is 2466 or 2859) continue;
            if (cls.Joints.Count < 2 || !cls.Mesh.SkinningApplied ||
                !cls.Mesh.SkinStateFullyResolved ||
                cls.Sequences.All(s => s.Frames.Count == 0)) continue;
            int at = Enumerable.Range(0, table.Count)
                .Select(i => table.Offset + i * GcUyaMoby.ClassEntrySize)
                .FirstOrDefault(p => BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(p + 4)) == cls.OClass, -1);
            if (at < 0) continue;
            int start = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int end = core.SectionBoundaries.FirstOrDefault(b => b > start, core.Assets.Length);
            byte[] source = core.Assets.AsSpan(start, end - start).ToArray();
            if (source[0x0b] != 0) continue; // R&C1-format variants are separate.
            // Apply the same initial-pose admission gate as production.
            var initial = cls.Sequences.FirstOrDefault(s => s.Index == 0)?.Frames.FirstOrDefault();
            if (initial is null || !initial.ExtraChannelsResolved) continue;
            int poses = 0, outside = 0, vertices = 0;
            try
            {
                foreach (var seq in cls.Sequences)
                {
                    if (seq.Frames.Count == 0) continue;
                    foreach (var frame in seq.Frames.Count > 1 ?
                        new[] { seq.Frames[0], seq.Frames[^1] } : new[] { seq.Frames[0] })
                    {
                        if (!frame.ExtraChannelsResolved) continue; // Unsupported source frame.
                        var points = Ps2MobyNativePose.Pose(source, cls.Mesh, frame);
                        poses++;
                        if (seq.BoundingSphere is not { } sphere || sphere.Radius <= 0) continue;
                        double k = cls.Mesh.Scale / 1024.0;
                        double cx = sphere.X * k, cy = sphere.Y * k, cz = sphere.Z * k, r = Math.Abs(sphere.Radius * k);
                        if (r <= 0) continue;
                        for (int v = 0; v < points.Length; v += 3)
                        {
                            double dx = points[v] - cx, dy = points[v + 1] - cy, dz = points[v + 2] - cz;
                            double radiusMultiple = Math.Sqrt(dx * dx + dy * dy + dz * dz) / r;
                            if (radiusMultiple > largestRadiusMultiple)
                            {
                                largestRadiusMultiple = radiusMultiple;
                                worstClass = cls.OClass;
                                worstSequence = seq.Index;
                            }
                            if (radiusMultiple > 1.001) outside++;
                            vertices++;
                        }
                    }
                }
                eligible++; checkedFrames += poses; checkedVerts += vertices; sphereEscapes += outside;
                if (outside > 0) failures.Add($"class={cls.OClass} poses={poses} outside={outside}/{vertices}");
            }
            catch (InvalidDataException ex)
            {
                unsupported++;
                if (failures.Count < 30) failures.Add($"class={cls.OClass} UNSUPPORTED: {ex.GetType().Name} {ex.Message}");
            }
        }
        output.WriteLine($"General Oozla eligible_classes={eligible}, sampled_frames={checkedFrames}, checked_vertices={checkedVerts}, escapes={sphereEscapes}, unsupported_classes={unsupported}, largest_radius_multiple={largestRadiusMultiple:F6}, worst_class={worstClass}, worst_sequence={worstSequence}");
        foreach (var f in failures.Take(40)) output.WriteLine(f);
        Assert.True(eligible >= 8);
        Assert.True(checkedVerts > 0);
        Assert.Equal(0, unsupported);
        // The retail corpus contains a handful of legitimate sphere outliers.
        Assert.True((double)sphereEscapes / checkedVerts < 0.001,
            $"Too many authored-sphere outliers: {sphereEscapes}/{checkedVerts}");
        // The retail witness peaks at 1.354028 radii in class 2818,
        // sequence 3. Do not silently admit arbitrarily exploded vertices
        // just because their fraction of the full corpus is small.
        Assert.True(largestRadiusMultiple < 1.36,
            $"Excessive authored-sphere displacement: {largestRadiusMultiple:F6} " +
            $"radii (class {worstClass}, sequence {worstSequence})");
    }
}

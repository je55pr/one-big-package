using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Cross-planet preview regression: format selection is per retail class
/// header, not a special list of level/class IDs. Never edits render data.
/// </summary>
public sealed class Rac2CrossLevelMobyPreviewSafetyTests(ITestOutputHelper log)
{
    [SkippableFact]
    public void AllRetailLevelsPreserveMeshesExceptSourceBasedInitialPositions()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO missing");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        int levels = 0, totalClasses = 0, posed = 0, variants = 0;
        for (int file = 0; file <= 26; file++)
        {
            var wad = fs.OpenFile($"/G/LEVEL{file}.WAD");
            if (wad is null) continue;
            var header = Rac2LevelWad.ReadHeader(wad);
            var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
            var table = core.Header.MobyClasses;
            var layout = new GcUyaMoby.ClassTable(table.Count, table.Offset);
            var before = GcUyaMoby.ReadClasses(core.Index, core.Assets, core.SectionBoundaries, layout,
                recoverPersistentVertexCache: _ => true);
            var after = GcUyaMoby.ReadClasses(core.Index, core.Assets, core.SectionBoundaries, layout,
                recoverPersistentVertexCache: _ => true, previewNativeInitialPose: _ => true);
            Assert.Equal(before.Keys.Order(), after.Keys.Order());
            int changed = 0, alternative = 0;
            foreach (var (id, a) in before)
            {
                var b = after[id];
                Assert.Equal(a.Mesh.Positions.Length, b.Mesh.Positions.Length);
                Assert.Equal(a.Mesh.Indices, b.Mesh.Indices);
                Assert.Equal(a.Mesh.Uvs, b.Mesh.Uvs);
                Assert.Equal(a.Mesh.Normals, b.Mesh.Normals);
                Assert.Equal(a.Mesh.TriangleMaterialSlots, b.Mesh.TriangleMaterialSlots);
                Assert.Equal(a.Mesh.TriangleMaterialStateIndices, b.Mesh.TriangleMaterialStateIndices);
                Assert.Equal(a.Mesh.VertexJoints, b.Mesh.VertexJoints);
                Assert.Equal(a.Mesh.VertexWeights, b.Mesh.VertexWeights);
                Assert.Equal(a.Mesh.SkinLocalPositions, b.Mesh.SkinLocalPositions);
                Assert.Equal(a.TriangleTextureIds, b.TriangleTextureIds);
                Assert.Equal(a.Mesh.Materials, b.Mesh.Materials);
                Assert.All(b.Mesh.Positions, x => Assert.True(double.IsFinite(x)));
                if (!a.Mesh.Positions.SequenceEqual(b.Mesh.Positions)) changed++;
            }
            for (int k = 0; k < table.Count; k++)
            {
                int at = table.Offset + k * GcUyaMoby.ClassEntrySize;
                int off = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
                if (off <= 0 || off >= core.Assets.Length - 0x48) continue;
                if (core.Assets[off + 0x0b] != 0)
                {
                    int id = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
                    if (before.ContainsKey(id))
                        Assert.Equal(before[id].Mesh.Positions, after[id].Mesh.Positions);
                    alternative++;
                }
            }
            log.WriteLine($"LEVEL{file}: classes={before.Count}, preview pose changes={changed}, alternative packet formats={alternative}");
            Assert.True(changed > 0, $"No eligible native poses on LEVEL{file}");
            totalClasses += before.Count; posed += changed; variants += alternative; levels++;
        }
        log.WriteLine($"ALL RAC2 LEVELS: levels={levels} loadedClasses={totalClasses} changedNativePoses={posed} alternativeFormatClasses={variants}");
        Assert.Equal(27, levels);
        Assert.Equal(2, variants);
        Assert.True(posed >= 700);
    }
}

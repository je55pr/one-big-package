using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2OozlaNativePosePreviewTests
{
    [SkippableFact]
    public void UniversalNativeInitialPosePreviewPreservesEveryMeshContract()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO missing");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var t = core.Header.MobyClasses;
        var table = new GcUyaMoby.ClassTable(t.Count, t.Offset);
        var control = GcUyaMoby.ReadClasses(core.Index, core.Assets, core.SectionBoundaries, table,
            recoverPersistentVertexCache: _ => true);
        var preview = GcUyaMoby.ReadClasses(core.Index, core.Assets, core.SectionBoundaries, table,
            recoverPersistentVertexCache: _ => true,
            previewNativeInitialPose: _ => true);
        Assert.Equal(control.Keys.Order(), preview.Keys.Order());
        int changed = 0;
        foreach (var (id, before) in control)
        {
            var after = preview[id];
            Assert.Equal(before.Mesh.Indices, after.Mesh.Indices);
            Assert.Equal(before.Mesh.Uvs, after.Mesh.Uvs);
            Assert.Equal(before.Mesh.Normals, after.Mesh.Normals);
            Assert.Equal(before.Mesh.TriangleMaterialSlots, after.Mesh.TriangleMaterialSlots);
            Assert.Equal(before.Mesh.TriangleMaterialStateIndices, after.Mesh.TriangleMaterialStateIndices);
            Assert.Equal(before.Mesh.SkinLocalPositions, after.Mesh.SkinLocalPositions);
            Assert.Equal(before.Mesh.VertexJoints, after.Mesh.VertexJoints);
            Assert.Equal(before.Mesh.VertexWeights, after.Mesh.VertexWeights);
            Assert.Equal(before.Mesh.Positions.Length, after.Mesh.Positions.Length);
            Assert.All(after.Mesh.Positions, v => Assert.True(double.IsFinite(v)));
            if (!before.Mesh.Positions.SequenceEqual(after.Mesh.Positions)) changed++;
            if (id is 2466 or 2859)
                Assert.False(before.Mesh.Positions.SequenceEqual(after.Mesh.Positions));
        }
        Assert.True(changed >= 30, $"Expected many eligible Oozla classes, got {changed}");
    }
}

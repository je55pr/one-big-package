using OBP.IO;
using OBP.RAC2;
using OBP.RAC2.Level;
using OBP.RAC3;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Portable source-qualified importer placement regressions. No retail bytes
/// are embedded: the same synthetic class triangles enter each actual importer.
/// </summary>
public sealed class TieImporterUvSeamTests
{
    private static readonly float[] Identity =
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static readonly double[] Positions =
        [0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0];

    // Source vertex zero is shared by a seam-crossing and an ordinary face.
    // It must have different presentation UVs without moving its position.
    private static readonly float[] Uvs =
        [0, 0, 0, 0.25f, 15.5f, 0.5f, 0.5f, 0.75f];

    private static readonly int[] Triangles = [0, 1, 2, 0, 1, 3];

    private static readonly RuntimeMaterialPresentation Repeat = new(
        WrapS: RuntimeTextureWrap.Repeat,
        WrapT: RuntimeTextureWrap.Repeat);

    private static readonly RuntimeMaterialPresentation Clamp = new(
        WrapS: RuntimeTextureWrap.Clamp,
        WrapT: RuntimeTextureWrap.Clamp);

    [Fact]
    public void Rac2_TiePlacementCorrectsRepeatingSeamButPreservesClampAndShrubs()
    {
        const int classId = 731;
        const int textureId = 6;
        var instance = new Rac2Instances.MatrixInstance(0, classId, Identity);
        var meshes = new List<RuntimeMesh>();

        Rac2WorldImport.PlaceMatrixInstances("tie", [instance], id =>
            id == classId
                ? (Positions, Uvs, [.. Triangles, 0, 1, 2],
                    new[] { textureId, textureId, textureId },
                    new RuntimeMaterialPresentation?[] { Repeat, Repeat, Clamp })
                : null,
            meshes, (_, _, _) => { }, growBounds: false);

        Assert.Equal(2, meshes.Count);
        Assert.Equal(3, meshes.Sum(m => m.TriangleCount));
        var repeating = Assert.Single(meshes, m => m.MaterialPresentation == Repeat);
        var clamped = Assert.Single(meshes, m => m.MaterialPresentation == Clamp);
        Assert.Equal(2, repeating.TriangleCount);
        Assert.Equal(1, clamped.TriangleCount);
        Assert.All(meshes, m =>
        {
            Assert.Equal("tie", m.AssetKind);
            Assert.Equal(textureId, m.TextureId);
        });

        Assert.Contains(16f, repeating.Uvs);
        Assert.Contains(15.5f, repeating.Uvs);
        Assert.Contains(0f, repeating.Uvs);
        Assert.Equal(0f, clamped.Uvs[0]);
        Assert.Contains(15.5f, clamped.Uvs);
        Assert.Equal(6, repeating.Indices.Length);
        Assert.Equal(3, clamped.Indices.Length);

        // Both variants of vertex zero share the same XYZ but have different S.
        var atOrigin = Enumerable.Range(0, repeating.Positions.Length / 3)
            .Where(v => repeating.Positions[v * 3] == 0
                && repeating.Positions[v * 3 + 1] == 0
                && repeating.Positions[v * 3 + 2] == 0)
            .Select(v => repeating.Uvs[v * 2]).Order().ToArray();
        Assert.Equal([0f, 16f], atOrigin);

        var shrubs = new List<RuntimeMesh>();
        Rac2WorldImport.PlaceMatrixInstances("shrub", [instance], id =>
            id == classId
                ? (Positions, Uvs, Triangles, new[] { textureId, textureId },
                    new RuntimeMaterialPresentation?[] { Repeat, Repeat })
                : null,
            shrubs, (_, _, _) => { }, growBounds: false);
        var shrub = Assert.Single(shrubs);
        Assert.Equal(2, shrub.TriangleCount);
        Assert.DoesNotContain(16f, shrub.Uvs);
        Assert.Contains(15.5f, shrub.Uvs);
    }

    [Fact]
    public void Rac3_TiePlacementKeepsDistinctSeamVariantsAndLeavesShrubAlone()
    {
        const int classId = 731;
        const int textureId = 6;
        var cls = new Rac3Assets.StaticClass(classId, Positions, Uvs, Triangles, [textureId, textureId])
        {
            TriangleMaterialStateIndices = [-1, -1],
            TriangleAlphaBlendEnabled = [null, null],
        };
        var classes = new Dictionary<int, Rac3Assets.StaticClass> { [classId] = cls };
        var instance = new Rac3Gameplay.MatrixInstance(0, classId, Identity);
        var ties = new List<RuntimeMesh>();

        Rac3WorldImport.PlaceStatics("tie", [instance], classes, 8, ties, (_, _, _) => { });
        var tie = Assert.Single(ties);
        Assert.Equal("tie", tie.AssetKind);
        Assert.Equal(textureId, tie.TextureId);
        Assert.Equal(2, tie.TriangleCount);
        Assert.Equal(6, tie.Indices.Length);
        var sameOrigin = Enumerable.Range(0, tie.Positions.Length / 3)
            .Where(v => tie.Positions[v * 3] == 0 && tie.Positions[v * 3 + 1] == 0
                && tie.Positions[v * 3 + 2] == 0)
            .Select(v => tie.Uvs[v * 2]).Order().ToArray();
        Assert.Equal([0f, 16f], sameOrigin);
        Assert.Contains(15.5f, tie.Uvs);

        var shrubs = new List<RuntimeMesh>();
        Rac3WorldImport.PlaceStatics("shrub", [instance], classes, 8, shrubs, (_, _, _) => { });
        var shrub = Assert.Single(shrubs);
        Assert.Equal(2, shrub.TriangleCount);
        Assert.Equal(textureId, shrub.TextureId);
        Assert.DoesNotContain(16f, shrub.Uvs);
        Assert.Equal(Uvs.Length, shrub.Uvs.Length);
    }

    [SkippableFact]
    public void Rac3RetailVeldin_TieSeamPreservesNativeFacesAndRecoversContinuousUv()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(path) || !File.Exists(path),
            "UYA retail ISO is required for source evidence.");

        using var disc = new FileRandomAccessReader(path!);
        var opened = Rac3LevelCore.Open(disc, 1);
        var classes = Rac3Assets.ReadTieClasses(opened.Core);
        var instanceData = Rac3Gameplay.Read(opened.GameplayReader);
        var world = Rac3WorldImport.Build(disc, 1);

        // UYA retail class 5863, face 8 has packed S=(0,0,15.5).
        const int classId = 5863;
        var cls = classes[classId];
        const int face = 8;
        var native = Enumerable.Range(0, 3).Select(k => cls.Indices[face * 3 + k]).ToArray();
        Assert.Equal([0f, 0f, 15.5f], native.Select(v => cls.Uvs[v * 2]).ToArray());
        var instance = instanceData.TieInstances.First(i => i.OClass == classId);

        (double X, double Y, double Z) Position(int v)
        {
            var m = instance.Matrix;
            double x = cls.Positions[v * 3], y = cls.Positions[v * 3 + 1], z = cls.Positions[v * 3 + 2];
            return (
                m[0] * x + m[4] * y + m[8] * z + m[12],
                m[2] * x + m[6] * y + m[10] * z + m[14],
                m[1] * x + m[5] * y + m[9] * z + m[13]);
        }

        var expectedPositions = native.Select(Position).ToArray();
        var surface = Assert.Single(world.Meshes, m =>
            m.AssetKind == "tie" && m.TextureId == cls.TriangleTextureIds[face]);
        int found = 0;
        for (int f = 0; f < surface.TriangleCount; f++)
        {
            var vs = Enumerable.Range(0, 3).Select(k => surface.Indices[f * 3 + k]).ToArray();
            bool Match(int v, (double X, double Y, double Z) p) =>
                Math.Abs(surface.Positions[v * 3] - p.X) < 0.001
                && Math.Abs(surface.Positions[v * 3 + 1] - p.Y) < 0.001
                && Math.Abs(surface.Positions[v * 3 + 2] - p.Z) < 0.001;
            if (!Enumerable.Range(0, 3).All(i => Match(vs[i], expectedPositions[i]))) continue;
            Assert.Equal([15.5f, 16f, 16f],
                vs.Select(v => surface.Uvs[v * 2]).Order().ToArray());
            found++;
        }
        Assert.Equal(1, found);
        Assert.Equal(instanceData.TieInstances.Sum(i =>
                classes[i.OClass].Indices.Length / 3),
            world.Meshes.Where(m => m.AssetKind == "tie").Sum(m => m.TriangleCount));
    }
}

using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1AridiaOceanPreviewTests
{
    [SkippableFact]
    public void AridiaNativeGridProducesSeparateRenderableSeaWithoutChangingCollision()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var nativeLevel = Rac1DiscIndex.Read(disc).Levels.Single(level => level.LevelId == 2);
        var core = Rac1LevelCore.Open(disc, nativeLevel);
        var preview = Rac1AridiaOceanPreview.Build(
            Rac1LevelOverlay.Open(disc, nativeLevel), core);
        Assert.Equal(7, preview.Meshes.Count);
        Assert.Equal(13_328, preview.Meshes.Sum(m => m.TriangleCount)); // 136 visible 7x7 cells
        Assert.All(preview.Meshes, mesh =>
        {
            Assert.Equal(Rac1AridiaOceanPreview.AssetKind, mesh.AssetKind);
            Assert.Equal(mesh.Positions.Length / 3 * 2, mesh.Uvs.Length);
            Assert.Equal(0, mesh.Indices.Length % 3);
            Assert.True(mesh.Indices.Max() < mesh.Positions.Length / 3);
        });
        Assert.Equal(64, preview.Texture.Width);
        Assert.Equal(64, preview.Texture.Height);

        var world = Rac1WorldImport.Build(disc, 2, includeAridiaOceanPreview: true);
        var ordinary = Rac1WorldImport.Build(disc, 2);
        Assert.DoesNotContain(ordinary.Meshes, m => m.AssetKind == Rac1AridiaOceanPreview.AssetKind);
        Assert.Equal(ordinary.TotalCollisionTriangles, world.TotalCollisionTriangles);
        Assert.Equal(7, world.Meshes.Count(m => m.AssetKind == Rac1AridiaOceanPreview.AssetKind));
        Assert.Single(world.Textures, texture => texture.AssetKind == Rac1AridiaOceanPreview.AssetKind);

        // No collision face is added or mutated to create a visible sea.
        Assert.DoesNotContain(world.CollisionMeshes, c => c.MaterialSource is null);
        Assert.DoesNotContain(world.Meshes, m => m.AssetKind == "collision-ocean");
    }

    [SkippableFact]
    public void OtherRac1LevelsHaveNoAridiaOnlySeaPreview()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        foreach (int level in new[] { 1, 9, 11 })
        {
            var world = Rac1WorldImport.Build(disc, level);
            Assert.DoesNotContain(world.Meshes, m => m.AssetKind == Rac1AridiaOceanPreview.AssetKind);
        }
    }
}
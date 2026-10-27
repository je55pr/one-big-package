using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Retail-gated evidence for source-authored R&C1 Novalis foliage materials and placement batching.</summary>
public sealed class Rac1FoliagePresentationTests
{
    [SkippableFact]
    public void Novalis_AlphaCanopiesKeepOneSortSurfacePerPlacementWhileOpaqueBarkStaysWelded()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(x => x.LevelId == 1);
        var core = Rac1LevelCore.Open(disc, level);
        var sourceClasses = Rac1StaticClasses.Read(core).Shrubs;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;
        var world = Rac1WorldImport.Build(disc, 1);
        var shrubs = world.Meshes.Where(m => m.AssetKind == "shrub").ToArray();
        var textures = world.Textures.Where(t => t.AssetKind == "shrub")
            .ToDictionary(t => t.TextureId, t => AlphaProfile.Analyse(t.Rgba));

        Assert.Equal(1_208, placements.Count);
        Assert.Equal(430_644, shrubs.Sum(m => m.TriangleCount));
        Assert.True(textures[14].HasTransparency);
        Assert.False(textures[15].HasTransparency);

        // Retail class 107 supplies Novalis leafy surfaces on alpha texture 14.
        // Every native placement with that surface must have its own sorting origin.
        int leafyPlacements = placements.Count(instance =>
            sourceClasses[instance.OClass].TriangleTextureIds.Contains(14));
        int leafyMeshes = shrubs.Count(mesh => mesh.TextureId == 14);
        Assert.True(leafyPlacements > 10);
        Assert.Equal(leafyPlacements, leafyMeshes);
        Assert.All(shrubs.Where(m => m.TextureId == 14), mesh =>
            Assert.True(mesh.MaterialPresentation?.AlphaBlendEnabled));

        // Fully opaque class-116 trunk surfaces may keep sharing a material mesh
        // despite native GIF ALPHA=1; splitting them increases draw calls without benefit.
        int trunkPlacements = placements.Count(instance =>
            sourceClasses[instance.OClass].TriangleTextureIds.Contains(15));
        int trunkMeshes = shrubs.Count(mesh => mesh.TextureId == 15);
        Assert.True(trunkPlacements > 10);
        Assert.InRange(trunkMeshes, 1, trunkPlacements - 1);
    }

    [SkippableFact]
    public void Novalis_SavedSwimmingViewAdmitsOnlyIdentifiedTreeAndRedLeafCutouts()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(disc, 1);
        var shrubs = world.Meshes.Where(mesh => mesh.AssetKind == "shrub").ToArray();

        // The saved view ray intersects class 121's layered canopy (textures
        // 10/11, 20-24). Class 116 supplies the red-leaf textures 16/17 nearby.
        foreach (int textureId in new[] { 10, 11, 16, 17, 20, 21, 22, 23, 24 })
            Assert.Contains(shrubs, mesh => mesh.TextureId == textureId &&
                mesh.MaterialPresentation?.PreferFoliageCutout is true);

        // Those specific textures contain meaningful intermediate alpha.
        // Cutout is an explicit visual-stability tradeoff at these authored
        // placements, not a rule silently applied to all translucent foliage.
        var foliageTextures = world.Textures.Where(texture => texture.AssetKind == "shrub")
            .ToDictionary(texture => texture.TextureId, texture => AlphaProfile.Analyse(texture.Rgba));
        Assert.True(foliageTextures[10].TranslucentFraction > 0.02);
        Assert.True(foliageTextures[16].TranslucentFraction > 0.02);

        // The unrelated class-107 shrub retains the preceding, independently
        // sorted alpha blend and must not silently gain this treatment.
        Assert.DoesNotContain(shrubs, mesh => mesh.TextureId == 14 &&
            mesh.MaterialPresentation?.PreferFoliageCutout is true);
        Assert.All(shrubs.Where(mesh => mesh.MaterialPresentation?.PreferFoliageCutout is true),
            mesh => Assert.True(mesh.MaterialPresentation?.AlphaBlendEnabled));
    }

    [SkippableFact]
    public void Veldin_NativeShrubTriangleCountRemainsStable()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(disc, 0);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(entry => entry.LevelId == 0);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(disc, level)).Shrubs;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;
        Assert.Equal(12_244, classes.Values.Sum(cls => cls.Mesh.Indices.Length / 3));

        // Each authored instance keeps every face despite alpha-dependent
        // material partitioning; no foliage triangles may be dropped.
        int expectedTriangles = placements.Sum(instance => classes[instance.OClass].Mesh.Indices.Length / 3);
        Assert.Equal(expectedTriangles, world.Meshes.Where(mesh => mesh.AssetKind == "shrub")
            .Sum(mesh => mesh.TriangleCount));
    }
}

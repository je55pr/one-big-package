using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Retail-gated colour/depth regression for Veldin's overlapping red and blue leaf cards.</summary>
public sealed class Rac1VeldinRedPlantPresentationTests
{
    [SkippableFact]
    public void Class466OverlappingRedAndBlueLeavesUseDepthWritingCutout()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(entry => entry.LevelId == 0);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(disc, level)).Shrubs;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;

        // Saved Veldin view-20261027-190012-704 looks toward three flowers
        // along the ledge and another above. Their native class includes
        // independent red and dark-blue cards that nearly coincide.
        var native = classes[466];
        Assert.Equal(474, native.TriangleTextureIds.Count(id => id == 3));
        Assert.Equal(456, native.TriangleTextureIds.Count(id => id == 4));
        Assert.Equal(5, placements.Count(item => item.OClass == 466));

        var world = Rac1WorldImport.Build(disc, 0);
        var redTexture = Assert.Single(world.Textures, t => t.AssetKind == "shrub" && t.TextureId == 3);
        var blueTexture = Assert.Single(world.Textures, t => t.AssetKind == "shrub" && t.TextureId == 4);
        var redProfile = AlphaProfile.Analyse(redTexture.Rgba);
        var blueProfile = AlphaProfile.Analyse(blueTexture.Rgba);
        Assert.InRange(redProfile.CutoutFraction, 0.3, 0.4);
        Assert.InRange(blueProfile.CutoutFraction, 0.3, 0.4);
        Assert.InRange(redProfile.TranslucentFraction, 0.04, 0.06);
        Assert.InRange(blueProfile.TranslucentFraction, 0.04, 0.06);

        var red = Assert.Single(world.Meshes, m => m.AssetKind == "shrub" && m.TextureId == 3);
        var blue = Assert.Single(world.Meshes, m => m.AssetKind == "shrub" && m.TextureId == 4);
        Assert.Equal(5 * 474, red.TriangleCount);
        Assert.Equal(5 * 456, blue.TriangleCount);

        foreach (var mesh in new[] { red, blue })
        {
            Assert.True(mesh.MaterialPresentation?.AlphaBlendEnabled);
            Assert.True(mesh.MaterialPresentation?.PreferFoliageCutout);
            Assert.True(mesh.MaterialPresentation?.PreferPerPlacementFoliageDepth);
        }

        // This change is limited to the observed class. Other Veldin shrub
        // materials keep their original sorting/alpha semantics.
        Assert.DoesNotContain(world.Meshes, m => m.AssetKind == "shrub" &&
            m.TextureId is not (3 or 4) &&
            m.MaterialPresentation?.PreferFoliageCutout == true);
    }
}

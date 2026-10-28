using OBP.PS2.Geometry;
using OBP.PS2.Graphics;
using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class Rac1CrossLevelFoliageTests
{
    [Theory]
    [InlineData(true, false, false, 160, new int[] { 10, 11 })]
    [InlineData(false, false, false, 160, new int[] { })]
    [InlineData(true, true, false, 160, new int[] { })]
    [InlineData(true, false, true, 160, new int[] { })]
    [InlineData(true, false, false, 80, new int[] { })]
    public void CutoutPolicyRequiresLargeCoincidentBinaryAlphaLayersWithSourceBlend(
        bool overlap, bool fixedAlpha, bool mostlyTranslucent, int faces, int[] expected)
    {
        var positions = new List<double>();
        var indices = new List<int>();
        var textureIds = new List<int>();
        foreach (int layer in new[] { 10, 11, 12 })
        for (int triangle = 0; triangle < faces; triangle++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                indices.Add(positions.Count / 3);
                positions.Add(triangle * 5 + corner);
                positions.Add(corner * 2);
                positions.Add(layer == 12 ? 40 : layer == 11 && !overlap ? 20 : 0);
            }
            textureIds.Add(layer);
        }

        var state = new RcMaterialState(0,
            RcTextureWrapHint.Unknown, RcTextureWrapHint.Unknown,
            RcTextureMinFilterHint.Unknown, 0, 0, 0, 0, "synthetic")
        {
            AlphaBlend = fixedAlpha ? new RcGsAlphaState(0, 1, 2, 1, 64) : null,
        };
        var mesh = new RcShrub.Mesh(
            positions.ToArray(), new float[indices.Count * 2],
            indices.ToArray(), new int[textureIds.Count], new int[textureIds.Count],
            Enumerable.Repeat<bool?>(true, textureIds.Count).ToArray(), 1, [state]);
        var shrub = new Rac1StaticClasses.ShrubClass(700, 0, mesh,
            textureIds.ToArray(), [10, 11, 12]);
        var alpha = new Dictionary<int, AlphaProfile>
        {
            [10] = new(.55, .35, .10, true),
            [11] = mostlyTranslucent ? new(.35, .35, .30, true) : new(.55, .35, .10, true),
            [12] = new(.55, .35, .10, true),
        };

        var actual = Rac1FoliageCutoutPolicy.FindLayeredCutoutTextures(shrub, alpha);
        Assert.Equal(expected, actual.OrderBy(x => x));
    }

    [SkippableTheory]
    [InlineData(0, 466, 3, 4, 5)]
    [InlineData(18, 466, 0, 1, 69)]
    [InlineData(18, 510, 29, 30, 82)]
    [InlineData(15, 399, 23, 24, 0)]
    [InlineData(1, 116, 16, 17, 0)]
    public void RetailInterleavedLeafMaterialsSelectConsistentCutoutAcrossLevels(
        int levelId, int classId, int textureA, int textureB, int expectedPlacements)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(entry => entry.LevelId == levelId);
        var core = Rac1LevelCore.Open(disc, level);
        var cls = Rac1StaticClasses.Read(core).Shrubs[classId];
        var textureTable = OBP.PS2.Textures.RcLevelTextureTable.Read(
            core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
            new OBP.PS2.Textures.RcLevelTextureTable.Range(core.Header.ShrubTextures.Count,
                core.Header.ShrubTextures.Offset));
        var alpha = textureTable.ToDictionary(t => t.Index, t => AlphaProfile.Analyse(t.Rgba));
        var cutouts = Rac1FoliageCutoutPolicy.FindLayeredCutoutTextures(cls, alpha);
        Assert.Equal(new[] { textureA, textureB }, cutouts.OrderBy(x => x));
        if (expectedPlacements > 0)
        {
            var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;
            Assert.Equal(expectedPlacements, placements.Count(p => p.OClass == classId));
        }

        var world = Rac1WorldImport.Build(disc, levelId);
        foreach (int textureId in cutouts)
        {
            Assert.Contains(world.Meshes, m => m.AssetKind == "shrub" &&
                m.TextureId == textureId && m.MaterialPresentation?.PreferFoliageCutout == true);
        }
        if (levelId == 18 && classId == 510)
            Assert.DoesNotContain(world.Meshes, m => m.AssetKind == "shrub" &&
                m.TextureId is 20 or 22 && m.MaterialPresentation?.PreferFoliageCutout == true);
    }

    [SkippableTheory]
    [InlineData(15, 241294)]
    [InlineData(18, 670943)]
    public void OtherLevelShrubGeometryRemainsWhole(int levelId, int expectedTriangles)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var world = Rac1WorldImport.Build(disc, levelId);
        Assert.Equal(expectedTriangles, world.Meshes
            .Where(m => m.AssetKind == "shrub").Sum(m => m.TriangleCount));
    }
}

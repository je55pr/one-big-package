using OBP.IO;
using OBP.PS2.Graphics;
using OBP.PS2.Iso;
using OBP.PS2.Presentation;
using OBP.RAC2;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;
using OBP.RAC3;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Cross-game regression for native leaf cards and class-local alpha intent.</summary>
public sealed class TrilogyFoliagePresentationTests
{
    [Fact]
    public void SparseNativeFoliageCardsUseClassGeometryAndAlphaRatherThanHardcodedIds()
    {
        var state = new RcMaterialState(0, RcTextureWrapHint.Unknown,
            RcTextureWrapHint.Unknown, RcTextureMinFilterHint.Unknown,
            0, 0, 0, 0, "synthetic")
        {
            AlphaBlend = new RcGsAlphaState(0, 1, 0, 1, 0),
        };
        var alpha = new Dictionary<int, AlphaProfile>
        {
            [77] = new(0.12, 0.76, 0.12, true),
            [78] = new(0.5, 0.25, 0.25, true),
        };

        HashSet<int> Find(int[] textures, bool?[] blend) =>
            RcFoliageCutoutPolicy.FindFoliageCutoutTextures(
                [0, 0, 0, 1, 0, 0, 0, 1, 0],
                Enumerable.Repeat(new[] { 0, 1, 2 }, textures.Length).SelectMany(x => x).ToArray(),
                textures, Enumerable.Repeat(0, textures.Length).ToArray(),
                blend, [state], alpha);

        Assert.Equal(new[] { 77 }, Find([77], [true]).Order());
        Assert.Empty(Find([77], [false]));
        Assert.Empty(Find([78], [true]));
        Assert.Empty(Find([77, 77], [true, false]));
        Assert.Empty(Find(Enumerable.Repeat(77, 33).ToArray(),
            Enumerable.Repeat<bool?>(true, 33).ToArray()));
        // Other dense texture groups must not suppress a sparse eligible one.
        var mixedTextures = new[] { 77 }.Concat(Enumerable.Repeat(78, 33)).ToArray();
        Assert.Equal(new[] { 77 }, Find(mixedTextures,
            Enumerable.Repeat<bool?>(true, mixedTextures.Length).ToArray()).Order());
    }

    [SkippableFact]
    public void Rac3MarcadiaWheatAndGrassShareSparseFoliageDepthRule()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var core = Rac3LevelCore.Open(disc, 4).Core;
        var classes = Rac3Assets.ReadShrubClasses(core);
        var alpha = Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Shrub)
            .ToDictionary(t => t.Index, t => AlphaProfile.Analyse(t.Rgba));
        HashSet<int> Selected(int sourceClass)
        {
            var shrub = classes[sourceClass];
            return RcFoliageCutoutPolicy.FindFoliageCutoutTextures(
                shrub.Positions, shrub.Indices, shrub.TriangleTextureIds,
                shrub.TriangleMaterialStateIndices, shrub.TriangleAlphaBlendEnabled,
                shrub.Materials, alpha);
        }
        // The saved planter wheat and nearby ground tufts follow exactly the
        // same rule; none is manually repositioned or ID-special-cased.
        Assert.Equal(new[] { 1, 2, 3 }, Selected(3058).Order());
        Assert.Equal(new[] { 4 }, Selected(3063).Order());
        Assert.Equal(new[] { 38 }, Selected(3401).Order());
        Assert.Equal(new[] { 38 }, Selected(3404).Order());
        Assert.Empty(Selected(3292)); // Fully opaque planter soil.
        Assert.Empty(Selected(3293)); // Fully opaque surrounding soil.

        // Verify that the real importer carries the source-class selection
        // into the neutral mesh, not just that the selector returns its IDs.
        var world = Rac3WorldImport.BuildObserved(disc, 4, nativeMobyPoseOverride: false).World;
        foreach (int wheatTexture in new[] { 1, 2, 3 })
            Assert.Contains(world.Meshes, m =>
                m.AssetKind == "shrub" && m.TextureId == wheatTexture &&
                m.MaterialPresentation is
                {
                    AlphaBlendEnabled: true, PreferFoliageCutout: true,
                    PreferPerPlacementFoliageDepth: true
                });
    }

    [SkippableFact]
    public void Rac3MarcadiaPlanterSoilWritesDepthAheadOfNearbyWheat()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var core = Rac3LevelCore.Open(disc, 4).Core;
        var classes = Rac3Assets.ReadShrubClasses(core);
        var alpha = Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Shrub)
            .ToDictionary(t => t.Index, t => AlphaProfile.Analyse(t.Rgba));

        // Retail Marcadia: class 3058 is cutout wheat; classes 3292/3293
        // supply opaque soil and planter surfaces. All have native blend
        // enabled, so an unsorted Alpha pass previously allowed the soil to
        // overdraw the little plants.
        var wheat = classes[3058];
        Assert.Equal(new[] { 1, 2, 3 }, wheat.TriangleTextureIds.Distinct().Order());
        Assert.All(wheat.TriangleTextureIds, id =>
            Assert.True(alpha[id].HasTransparency));
        foreach (int classId in new[] { 3292, 3293 })
        {
            var soil = classes[classId];
            Assert.NotEmpty(soil.TriangleTextureIds);
            for (int face = 0; face < soil.TriangleTextureIds.Length; face++)
            {
                var profile = alpha[soil.TriangleTextureIds[face]];
                Assert.False(profile.HasTransparency);
                Assert.True(soil.TriangleAlphaBlendEnabled[face]);
                var presentation = Ps2MaterialPresentation.From(
                    soil.Materials[soil.TriangleMaterialStateIndices[face]],
                    soil.TriangleAlphaBlendEnabled[face]);
                Assert.True(MaterialModel.UsesOpaqueShrubDepth("shrub", profile, presentation));
            }
        }
    }

    [SkippableFact]
    public void Rac3VeldinInterleavedBlueLeavesUseTheSameDepthCutoutAsRac1()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var core = Rac3LevelCore.Open(disc, 1).Core;
        var classes = Rac3Assets.ReadShrubClasses(core);
        var alpha = Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Shrub)
            .ToDictionary(t => t.Index, t => AlphaProfile.Analyse(t.Rgba));

        var blue = classes[3613];
        Assert.Equal(360, blue.TriangleTextureIds.Count(id => id == 42));
        Assert.Equal(360, blue.TriangleTextureIds.Count(id => id == 43));
        Assert.Equal(new[] { 42, 43 }, RcFoliageCutoutPolicy.FindLayeredCutoutTextures(
            blue.Positions, blue.Indices, blue.TriangleTextureIds,
            blue.TriangleMaterialStateIndices, blue.TriangleAlphaBlendEnabled,
            blue.Materials, alpha).OrderBy(id => id));
        Assert.InRange(alpha[42].CutoutFraction, 0.27, 0.29);
        Assert.InRange(alpha[43].CutoutFraction, 0.29, 0.31);

        // Other Veldin plants contain cutout texels but not interleaved pairs.
        foreach (int classId in new[] { 3364, 3415 })
        {
            var shrub = classes[classId];
            Assert.Empty(RcFoliageCutoutPolicy.FindLayeredCutoutTextures(
                shrub.Positions, shrub.Indices, shrub.TriangleTextureIds,
                shrub.TriangleMaterialStateIndices, shrub.TriangleAlphaBlendEnabled,
                shrub.Materials, alpha));
        }

        var world = Rac3WorldImport.BuildObserved(disc, 1, nativeMobyPoseOverride: false).World;
        foreach (int tex in new[] { 42, 43 })
            Assert.Contains(world.Meshes, m => m.AssetKind == "shrub" && m.TextureId == tex &&
                m.MaterialPresentation is
                { AlphaBlendEnabled: true, PreferFoliageCutout: true, PreferPerPlacementFoliageDepth: true });
        // The general sparse-card rule also admits some other Veldin shrubs.
        // Ensure every newly cutout texture has source-class evidence instead
        // of retaining the old assumption that only the two layered IDs qualify.
        var admittedTextures = classes.Values.SelectMany(shrub =>
            RcFoliageCutoutPolicy.FindFoliageCutoutTextures(
                shrub.Positions, shrub.Indices, shrub.TriangleTextureIds,
                shrub.TriangleMaterialStateIndices, shrub.TriangleAlphaBlendEnabled,
                shrub.Materials, alpha)).ToHashSet();
        Assert.Contains(3, admittedTextures);
        Assert.All(world.Meshes.Where(m => m.AssetKind == "shrub" &&
            m.MaterialPresentation?.PreferFoliageCutout == true),
            m => Assert.Contains(m.TextureId, admittedTextures));
        Assert.Equal(890426, world.TotalRenderTriangles); // unchanged retail geometry census
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    public void Rac2RetainsBlendedShrubsWithoutUnprovenCutoutPairs(int level)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(disc);
        var wad = fs.OpenFile($"/G/LEVEL{level}.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2Shrub.ReadClasses(core);
        var alpha = Rac2LevelTextures.Read(core, Rac2LevelTextures.Table.Shrub)
            .ToDictionary(t => t.Index, t => AlphaProfile.Analyse(t.Rgba));
        foreach (var shrub in classes.Values)
            Assert.Empty(RcFoliageCutoutPolicy.FindFoliageCutoutTextures(
                shrub.Mesh.Positions, shrub.Mesh.Indices, shrub.TriangleTextureIds,
                shrub.Mesh.TriangleMaterialStateIndices, shrub.Mesh.TriangleAlphaBlendEnabled,
                shrub.Mesh.Materials, alpha));

        var world = Rac2WorldImport.Build(disc, level);
        Assert.Contains(world.Meshes, m => m.AssetKind == "shrub");
        Assert.DoesNotContain(world.Meshes, m => m.AssetKind == "shrub" &&
            (m.MaterialPresentation?.PreferFoliageCutout == true ||
             m.MaterialPresentation?.PreferPerPlacementFoliageDepth == true));
        // GC's retail levels currently have no eligible sparse-card surfaces.
        // The importer must preserve all their native shrub geometry.
        if (level == 1)
        {
            Assert.Equal(852_785, world.Meshes.Where(m => m.AssetKind == "shrub")
                .Sum(m => m.TriangleCount));
            Assert.Equal(38, world.Meshes.Count(m => m.AssetKind == "shrub"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothLaterImportersKeepCutoutClassScopedWithoutSplittingOtherShrubs(bool rac3)
    {
        static float[] Matrix(float x) =>
        [
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, 0, 0, 1,
        ];
        var positions = new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
        var uvs = new float[] { 0, 0, 1, 0, 0, 1 };
        var indices = new[] { 0, 1, 2 };
        var alpha = new Dictionary<int, AlphaProfile> { [12] = new(.12, .76, .12, true) };
        var state = new RcMaterialState(0, RcTextureWrapHint.Unknown,
            RcTextureWrapHint.Unknown, RcTextureMinFilterHint.Unknown,
            0, 0, 0, 0, "synthetic")
        {
            AlphaBlend = new RcGsAlphaState(0, 1, 0, 1, 0),
        };
        HashSet<int> Select(bool blend) => RcFoliageCutoutPolicy.FindFoliageCutoutTextures(
            positions, indices, [12], [0], [blend], [state], alpha);
        var selected = new Dictionary<int, HashSet<int>>
        {
            [1] = Select(true),
            [2] = Select(false),
        };
        Assert.Equal(new[] { 12 }, selected[1].Order());
        Assert.Empty(selected[2]);
        var meshes = new List<RuntimeMesh>();
        if (rac3)
        {
            Rac3Assets.StaticClass StaticClass(int id) =>
                new(id, positions, uvs, indices, [12])
                {
                    Materials = [state],
                    TriangleMaterialStateIndices = [0],
                    TriangleAlphaBlendEnabled = [id == 1],
                };
            var classes = new Dictionary<int, Rac3Assets.StaticClass>
            {
                [1] = StaticClass(1),
                [2] = StaticClass(2),
            };
            Rac3WorldImport.PlaceStatics("shrub",
                [new(0, 1, Matrix(0)), new(1, 1, Matrix(10)),
                 new(2, 2, Matrix(20)), new(3, 2, Matrix(30))],
                classes, 13, meshes, (_, _, _) => { }, alpha, selected);
        }
        else
        {
            var presentation = new RuntimeMaterialPresentation(
                AlphaBlendEnabled: true, BlendEquation: RuntimeBlendEquation.SourceAlpha);
            Rac2WorldImport.PlaceMatrixInstances("shrub",
                [new(0, 1, Matrix(0)), new(1, 1, Matrix(10)),
                 new(2, 2, Matrix(20)), new(3, 2, Matrix(30))],
                id => (positions, uvs, indices, new[] { 12 },
                    new RuntimeMaterialPresentation?[]
                    { presentation with { AlphaBlendEnabled = id == 1 } }),
                meshes, (_, _, _) => { }, growBounds: false, alpha, selected);
        }

        Assert.Equal(4, meshes.Sum(m => m.TriangleCount));
        Assert.Equal(2, meshes.Count);
        var cutout = Assert.Single(meshes, m => m.MaterialPresentation?.PreferFoliageCutout == true);
        Assert.Equal(2, cutout.TriangleCount);
        Assert.True(cutout.MaterialPresentation?.PreferPerPlacementFoliageDepth);
        var ordinary = Assert.Single(meshes, m => m.MaterialPresentation?.PreferFoliageCutout != true);
        Assert.Equal(2, ordinary.TriangleCount);
        Assert.NotEqual(true, ordinary.MaterialPresentation?.PreferPerPlacementFoliageDepth);
    }

    [Theory]
    [InlineData(1, 12, true, RuntimeBlendEquation.SourceAlpha, true)]
    [InlineData(2, 12, true, RuntimeBlendEquation.SourceAlpha, false)]
    [InlineData(1, 13, true, RuntimeBlendEquation.SourceAlpha, false)]
    [InlineData(1, 12, false, RuntimeBlendEquation.SourceAlpha, false)]
    [InlineData(1, 12, true, RuntimeBlendEquation.FixedAlpha, false)]
    [InlineData(1, 12, true, RuntimeBlendEquation.AdditiveSourceAlpha, false)]
    [InlineData(1, 12, true, RuntimeBlendEquation.AdditiveFixedAlpha, false)]
    public void SharedCutoutMaterialRequiresClassTextureAndNativeBlend(
        int classId, int textureId, bool blend, RuntimeBlendEquation equation, bool expected)
    {
        var native = new RuntimeMaterialPresentation(
            AlphaBlendEnabled: blend, BlendEquation: equation);
        var alpha = new Dictionary<int, AlphaProfile>
        {
            [12] = new(.6, .35, .05, true),
        };
        var selected = new Dictionary<int, HashSet<int>> { [1] = [12] };
        var actual = RcFoliageCutoutPolicy.WithFoliageCutout(
            classId, textureId, native, alpha, selected);
        Assert.Equal(expected, actual?.PreferFoliageCutout == true);
        Assert.Equal(expected, actual?.PreferPerPlacementFoliageDepth == true);
    }

    [Fact]
    public void SharedDetectorRejectsMalformedMaterialArrays()
    {
        var state = new RcMaterialState(0, RcTextureWrapHint.Unknown,
            RcTextureWrapHint.Unknown, RcTextureMinFilterHint.Unknown,
            0, 0, 0, 0, "synthetic");
        Assert.Throws<InvalidDataException>(() =>
            RcFoliageCutoutPolicy.FindLayeredCutoutTextures(
                new double[9], [0, 1, 2], [12], [], [true], [state],
                new Dictionary<int, AlphaProfile>()));
    }
}

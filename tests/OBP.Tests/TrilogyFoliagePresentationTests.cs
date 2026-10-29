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
        Assert.DoesNotContain(world.Meshes, m => m.AssetKind == "shrub" &&
            m.TextureId is not (42 or 43) && m.MaterialPresentation?.PreferFoliageCutout == true);
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
            Assert.Empty(RcFoliageCutoutPolicy.FindLayeredCutoutTextures(
                shrub.Mesh.Positions, shrub.Mesh.Indices, shrub.TriangleTextureIds,
                shrub.Mesh.TriangleMaterialStateIndices, shrub.Mesh.TriangleAlphaBlendEnabled,
                shrub.Mesh.Materials, alpha));

        var world = Rac2WorldImport.Build(disc, level);
        Assert.Contains(world.Meshes, m => m.AssetKind == "shrub");
        Assert.DoesNotContain(world.Meshes, m => m.AssetKind == "shrub" &&
            (m.MaterialPresentation?.PreferFoliageCutout == true ||
             m.MaterialPresentation?.PreferPerPlacementFoliageDepth == true));
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
        var alpha = new Dictionary<int, AlphaProfile> { [12] = new(.6, .35, .05, true) };
        var selected = new Dictionary<int, HashSet<int>> { [1] = [12], [2] = [] };
        var meshes = new List<RuntimeMesh>();
        if (rac3)
        {
            var state = new RcMaterialState(0, RcTextureWrapHint.Unknown,
                RcTextureWrapHint.Unknown, RcTextureMinFilterHint.Unknown,
                0, 0, 0, 0, "synthetic")
            {
                AlphaBlend = new RcGsAlphaState(0, 1, 0, 1, 0),
            };
            Rac3Assets.StaticClass StaticClass(int id) =>
                new(id, positions, uvs, indices, [12])
                {
                    Materials = [state],
                    TriangleMaterialStateIndices = [0],
                    TriangleAlphaBlendEnabled = [true],
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
                _ => (positions, uvs, indices, new[] { 12 },
                    new RuntimeMaterialPresentation?[] { presentation }),
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
        var actual = RcFoliageCutoutPolicy.WithLayeredCutout(
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

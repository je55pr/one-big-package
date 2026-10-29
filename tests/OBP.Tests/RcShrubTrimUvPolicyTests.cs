using OBP.IO;
using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC3;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class RcShrubTrimUvPolicyTests
{
    [Fact]
    public void LowOpaqueTopLitTrimIsSelectedButGrassAndComplexTexturesAreNot()
    {
        var points = new double[]
        {
            0, 0, 0, 6, 0, 0, 0, 0, .5, 6, 0, .5,
            0, 6, 0, 6, 6, 0, 0, 6, .5, 6, 6, .5,
        };
        var uv = new float[]
        {
            0, 2, 1, 2, 0, 1, 1, 1,
            0, 2, 1, 2, 0, 1, 1, 1,
        };
        int[] triangles = Enumerable.Range(0, 12)
            .SelectMany(_ => new[] { 0, 1, 2, 1, 3, 2, 4, 5, 6, 5, 7, 6 })
            .ToArray();
        var texIds = Enumerable.Repeat(7, triangles.Length / 3).ToArray();
        var stateIds = new int[texIds.Length];
        var alphaBlendEnabled = Enumerable.Repeat<bool?>(true, texIds.Length).ToArray();
        var materials = new[]
        {
            new RcMaterialState(7, RcTextureWrapHint.Repeat,
                RcTextureWrapHint.Repeat, RcTextureMinFilterHint.Linear,
                0, 0, 0, 0, "synthetic"),
            new RcMaterialState(7, RcTextureWrapHint.Repeat,
                RcTextureWrapHint.Repeat, RcTextureMinFilterHint.Linear,
                0, 0, 0, 0, "additive")
            {
                AlphaBlend = new RcGsAlphaState(0, 2, 0, 1, 0),
            },
        };
        var gradient = Texture(7, (x, y) => (byte)(220 - y * 3));
        var images = new Dictionary<int, RcLevelTextureTable.Texture> { [7] = gradient };

        HashSet<int> Classify(double[]? p = null, float[]? u = null) =>
            RcShrubTrimUvPolicy.FindInvertedTrimTextures(
                p ?? points, u ?? uv, triangles, texIds, stateIds,
                alphaBlendEnabled, materials, images);

        Assert.Equal(new[] { 7 }, Classify().Order());
        // All faces with this texture must have compatible native blending.
        // A single additive or fixed-alpha face suppresses a class-wide flip.
        stateIds[0] = 1;
        Assert.Empty(Classify());
        materials[1] = materials[1] with
        {
            AlphaBlend = new RcGsAlphaState(0, 1, 2, 1, 64),
        };
        Assert.Empty(Classify());
        stateIds[0] = 0;
        Assert.Equal(new[] { 7 }, Classify().Order());
        Assert.Empty(Classify(points, uv.Select((v, i) => (i & 1) != 0 ? 3 - v : v).ToArray()));
        Assert.Empty(Classify(points.Select((v, i) => i == (i / 3) * 3 + 2 ? v * 5 : v).ToArray(), uv));

        images[7] = gradient with { Rgba = gradient.Rgba.ToArray() };
        images[7].Rgba[3] = 0;
        Assert.Empty(Classify()); // Vegetation alpha cards must never be flipped.

        images[7] = Texture(7, (x, y) => (byte)((x * 17 + y * 7) % 256));
        Assert.Empty(Classify()); // Painted/noisy textures have no bevel gradient.

        images[7] = gradient;
        materials[0] = materials[0] with { WrapT = RcTextureWrapHint.Clamp };
        Assert.Empty(Classify());
    }

    [Fact]
    public void Rac3ImporterReversesOnlySelectedTrimUvsAndPreservesGeometry()
    {
        static float[] Identity() =>
        [
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
        ];

        var meshClass = new Rac3Assets.StaticClass(
            17,
            [0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0],
            [0, 2, 1, 2, 0, 1, 1, 1],
            [0, 1, 2, 1, 3, 2],
            [7, 8])
        {
            Materials =
            [
                new RcMaterialState(7, RcTextureWrapHint.Repeat,
                    RcTextureWrapHint.Repeat, RcTextureMinFilterHint.Linear,
                    0, 0, 0, 0, "synthetic"),
            ],
            TriangleMaterialStateIndices = [0, 0],
            TriangleAlphaBlendEnabled = [true, true],
        };
        var output = new List<RuntimeMesh>();
        Rac3WorldImport.PlaceStatics(
            "shrub",
            [new(0, 17, Identity())],
            new Dictionary<int, Rac3Assets.StaticClass> { [17] = meshClass },
            9, output, (_, _, _) => { },
            reversedTrimTextures: new Dictionary<int, HashSet<int>> { [17] = [7] });

        Assert.Equal(2, output.Count);
        Assert.Equal(2, output.Sum(mesh => mesh.TriangleCount));
        Assert.Equal(new float[] { 0, -1, 1, -1, 0, 0 },
            Assert.Single(output, mesh => mesh.TextureId == 7).Uvs);
        Assert.Equal(new float[] { 1, 2, 1, 1, 0, 1 },
            Assert.Single(output, mesh => mesh.TextureId == 8).Uvs);

        // The same texture on another class must retain its source UVs.
        var other = new List<RuntimeMesh>();
        Rac3WorldImport.PlaceStatics(
            "shrub",
            [new(1, 18, Identity())],
            new Dictionary<int, Rac3Assets.StaticClass> { [18] = meshClass with { OClass = 18 } },
            9, other, (_, _, _) => { },
            reversedTrimTextures: new Dictionary<int, HashSet<int>> { [17] = [7] });
        Assert.Equal(new float[] { 0, 2, 1, 2, 0, 1 },
            Assert.Single(other, mesh => mesh.TextureId == 7).Uvs);

        // Ties may unwrap packed UV seams but must ignore shrub trim policy.
        var normalTie = new List<RuntimeMesh>();
        var mappedTie = new List<RuntimeMesh>();
        var classes = new Dictionary<int, Rac3Assets.StaticClass> { [17] = meshClass };
        Rac3WorldImport.PlaceStatics(
            "tie", [new(2, 17, Identity())], classes,
            9, normalTie, (_, _, _) => { });
        Rac3WorldImport.PlaceStatics(
            "tie", [new(2, 17, Identity())], classes,
            9, mappedTie, (_, _, _) => { },
            reversedTrimTextures: new Dictionary<int, HashSet<int>> { [17] = [7] });
        foreach (var baseline in normalTie)
            Assert.Equal(baseline.Uvs,
                Assert.Single(mappedTie, mesh => mesh.TextureId == baseline.TextureId).Uvs);
    }

    [SkippableFact]
    public void MarcadiaRetailSelectsLowPlanterFamiliesNotCutoutPlants()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_UYA_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var core = Rac3LevelCore.Open(disc, 4).Core;
        var classes = Rac3Assets.ReadShrubClasses(core);
        var textures = Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Shrub)
            .ToDictionary(t => t.Index);
        HashSet<int> For(int id)
        {
            var cls = classes[id];
            return RcShrubTrimUvPolicy.FindInvertedTrimTextures(
                cls.Positions, cls.Uvs, cls.Indices,
                cls.TriangleTextureIds, cls.TriangleMaterialStateIndices,
                cls.TriangleAlphaBlendEnabled, cls.Materials, textures);
        }

        Assert.Equal(new[] { 17 }, For(3292).Order());
        Assert.Equal(new[] { 17 }, For(3293).Order());
        Assert.Equal(new[] { 11 }, For(3279).Order());
        foreach (int foliage in new[] { 3058, 3063, 3401, 3404 })
            Assert.Empty(For(foliage));
        int[] all = classes.Values.SelectMany(cls =>
            RcShrubTrimUvPolicy.FindInvertedTrimTextures(
                cls.Positions, cls.Uvs, cls.Indices,
                cls.TriangleTextureIds, cls.TriangleMaterialStateIndices,
                cls.TriangleAlphaBlendEnabled, cls.Materials, textures)).Distinct().Order().ToArray();
        Assert.Equal(new[] { 11, 17 }, all);

        // Exercise the real importer against a decoded Marcadia planter class,
        // not just the detector. Only the eligible native texture is reversed.
        var planter = classes[3293];
        var meshes = new List<RuntimeMesh>();
        Rac3WorldImport.PlaceStatics(
            "shrub",
            [new(0, 3293,
            [
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
            ])],
            new Dictionary<int, Rac3Assets.StaticClass> { [3293] = planter },
            textures.Count, meshes, (_, _, _) => { },
            reversedTrimTextures: new Dictionary<int, HashSet<int>> { [3293] = For(3293) });

        Assert.Equal(planter.Indices.Length / 3, meshes.Sum(mesh => mesh.TriangleCount));
        foreach (int textureId in new[] { 17, 18, 19 })
        {
            bool reversed = textureId == 17;
            var expected = Enumerable.Range(0, planter.TriangleTextureIds.Length)
                .Where(face => planter.TriangleTextureIds[face] == textureId)
                .SelectMany(face => Enumerable.Range(0, 3)
                    .Select(corner =>
                    {
                        int nativeVertex = planter.Indices[face * 3 + corner];
                        float nativeV = planter.Uvs[nativeVertex * 2 + 1];
                        return reversed ? 1f - nativeV : nativeV;
                    }))
                .Order().ToArray();
            var actual = meshes.Where(mesh => mesh.TextureId == textureId)
                .SelectMany(mesh => mesh.Indices.Select(vertex => mesh.Uvs[vertex * 2 + 1]))
                .Order().ToArray();
            Assert.Equal(expected, actual);
        }
    }

    private static RcLevelTextureTable.Texture Texture(int id, Func<int, int, byte> shade)
    {
        const int w = 64, h = 64;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int offset = (y * w + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = shade(x, y);
                pixels[offset + 3] = 255;
            }
        return new RcLevelTextureTable.Texture(id, w, h, 0, 0, 0, pixels);
    }
}

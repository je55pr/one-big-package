using OBP.IO;
using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC3.Level;

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
        var materials = new[]
        {
            new RcMaterialState(7, RcTextureWrapHint.Repeat,
                RcTextureWrapHint.Repeat, RcTextureMinFilterHint.Linear,
                0, 0, 0, 0, "synthetic"),
        };
        var gradient = Texture(7, (x, y) => (byte)(220 - y * 3));
        var images = new Dictionary<int, RcLevelTextureTable.Texture> { [7] = gradient };

        HashSet<int> Classify(double[]? p = null, float[]? u = null) =>
            RcShrubTrimUvPolicy.FindInvertedTrimTextures(
                p ?? points, u ?? uv, triangles, texIds, stateIds, materials, images);

        Assert.Equal(new[] { 7 }, Classify().Order());
        Assert.Empty(Classify(points, uv.Select((v, i) => (i % 2) == 1 ? 3 - v : v).ToArray()));
        Assert.Empty(Classify(points.Select((v, i) => (i % 3) == 2 ? v * 5 : v).ToArray(), uv));

        images[7] = gradient with { Rgba = gradient.Rgba.ToArray() };
        images[7].Rgba[3] = 0;
        Assert.Empty(Classify()); // Vegetation alpha cards must never be flipped.

        images[7] = Texture(7, (x, y) => (byte)((x * 17 + y * 7) % 256));
        Assert.Empty(Classify()); // Painted/noisy textures have no bevel gradient.

        images[7] = gradient;
        materials[0] = materials[0] with { WrapT = RcTextureWrapHint.Clamp };
        Assert.Empty(Classify());
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
                cls.Materials, textures);
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
                cls.Materials, textures)).Distinct().Order().ToArray();
        Assert.Equal(new[] { 11, 17 }, all);
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

using OBP.IO;
using OBP.RAC1;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Rendering-only evidence for source tfrag alpha across R&C1 levels.
/// Collision face classes intentionally play no part in visible-material choice.
/// </summary>
public sealed class Rac1TfragAlphaPresentationTests
{
    [SkippableTheory]
    [InlineData(2, 50, AlphaMode.Blend)]  // Aridia translucent/cutout overlay
    [InlineData(9, 43, AlphaMode.Blend)]  // Gaspar mixed-alpha overlay
    [InlineData(11, 37, AlphaMode.Blend)] // Pokitaru translucent texture
    [InlineData(2, 1, AlphaMode.Opaque)]  // Ordinary Aridia sand
    [InlineData(9, 0, AlphaMode.Opaque)]  // Ordinary Gaspar terrain
    [InlineData(11, 3, AlphaMode.Opaque)] // Ordinary Pokitaru terrain
    public void NativeTfragTextureUsesContentAlphaWithoutChangingOrdinaryTerrain(
        int levelId, int textureId, AlphaMode expected)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(disc, levelId);
        var texture = Assert.Single(world.Textures,
            t => t.AssetKind == "tfrag" && t.TextureId == textureId);
        var (mode, _) = MaterialModel.ResolveAlpha(AlphaProfile.Analyse(texture.Rgba));
        Assert.Equal(expected, mode);

        // The importer keeps the original native material surface and UVs,
        // rather than replacing terrain by a collision-class proxy.
        Assert.Contains(world.Meshes,
            mesh => mesh.AssetKind == "tfrag" && mesh.TextureId == textureId);
    }
}

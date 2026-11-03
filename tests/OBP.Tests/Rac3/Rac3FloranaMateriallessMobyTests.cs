using OBP.IO;
using OBP.RAC3;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Retail Florana bridge visual repro (Ctrl+F12 view-20261029-051447-556).</summary>
public sealed class Rac3FloranaMateriallessMobyTests
{
    [SkippableFact]
    public void BridgeClass7816RetainsNativePlacementButHasNoSupportedVisibleMaterial()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac3WorldImport.BuildObserved(reader, 2, nativeMobyPoseOverride: true).World;
        var objects = Assert.IsAssignableFrom<IReadOnlyList<OBP.Runtime.RuntimeDynamicObject>>(world.DynamicObjects);

        // This is the enormous beige shell around the bridge. Its real geometry
        // is 84 triangles but has no decoded texture or authored vertex colours.
        var bridge = Assert.Single(objects, o => o.NativeClassId == 7816 && o.InstanceIndex == 641);
        Assert.Equal(84, bridge.Meshes.Sum(m => m.TriangleCount));
        Assert.InRange(bridge.Transform.Matrix[12], 522, 524);
        Assert.InRange(bridge.Transform.Matrix[13], 318, 320);
        Assert.InRange(bridge.Transform.Matrix[14], 556, 558);
        Assert.All(bridge.Meshes, mesh =>
        {
            Assert.Equal(-1, mesh.TextureId);
            Assert.Null(mesh.Colors);
            Assert.False(MaterialModel.CanPresentSurface(mesh.AssetKind,
                world.Textures.Any(t => t.AssetKind == mesh.AssetKind && t.TextureId == mesh.TextureId),
                mesh.Colors is { Length: > 0 }));
        });

        // Preserve every authored actor; do not remove unrelated textured
        // world surfaces just to hide this unsupported presentation.
        Assert.Equal(680, objects.Count);
        Assert.Contains(objects.SelectMany(o => o.Meshes), mesh =>
        {
            bool hasDecodedTexture = world.Textures.Any(t =>
                t.AssetKind == mesh.AssetKind && t.TextureId == mesh.TextureId &&
                t.Width > 0 && t.Height > 0 && t.Rgba.Length == t.Width * t.Height * 4);
            return mesh.AssetKind == "moby" && hasDecodedTexture &&
                MaterialModel.CanPresentSurface(mesh.AssetKind, hasDecodedTexture, false);
        });
        Assert.Contains(world.Meshes, m => m.AssetKind == "tie" && m.TriangleCount > 0);
    }
}

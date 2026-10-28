using OBP.Godot;
using OBP.PS2.Geometry;
using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Retail-backed native terrain texture addressing near Skid McMarx.</summary>
public sealed class Rac1TfragWrapPresentationTests
{
    [Fact]
    public void NativeTfragAddressSelectors_PreserveUnknownAndMixedAxes()
    {
        Assert.Equal(RuntimeTextureWrap.Repeat, Rac1WorldImport.DecodeTfragWrap(0));
        Assert.Equal(RuntimeTextureWrap.Clamp, Rac1WorldImport.DecodeTfragWrap(1));
        Assert.Equal(RuntimeTextureWrap.Unknown, Rac1WorldImport.DecodeTfragWrap(2));
        Assert.Equal(RuntimeTextureWrap.Unknown, Rac1WorldImport.DecodeTfragWrap(null));

        Assert.False(WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                RuntimeTextureWrap.Clamp, RuntimeTextureWrap.Clamp)).TextureRepeat);
        Assert.True(WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Repeat)).TextureRepeat);
        Assert.Null(WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Clamp)).TextureRepeat);
    }

    [Fact]
    public void SyntheticTfrag_SameTextureDifferentAxisWrapsStayInSeparateMeshes()
    {
        // Portable fixture: the same texture appears on three adjacent faces,
        // but the original per-face materials disagree on S/T addressing.
        // The grouping boundary must survive even without the retail ISO.
        static OBP.PS2.Graphics.RcMaterialState Material(int s, int t) => new(
            TextureId: 7,
            WrapS: OBP.PS2.Graphics.RcTextureWrapHint.Unknown,
            WrapT: OBP.PS2.Graphics.RcTextureWrapHint.Unknown,
            MinFilter: OBP.PS2.Graphics.RcTextureMinFilterHint.Unknown,
            LodKRaw: 0, MinFilterRaw: 0, ClampRawLo: s, ClampRawHi: t,
            Layout: "synthetic-tfrag");

        var native = new RcTfrag.Mesh(
            Positions:
            [
                0, 0, 0,
                1, 0, 0,
                1, 0, 1,
                0, 0, 1,
                -1, 0, 1,
            ],
            Uvs:
            [
                0, 0,
                2, 0,
                2, 2,
                0, 2,
                -2, 2,
            ],
            Colors:
            [
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                1, 1, 0,
                0, 1, 1,
            ],
            VertexAlpha: [1, 1, 1, 1, 1],
            Indices: [0, 1, 2, 0, 2, 3, 0, 3, 4],
            TriangleTextureIds: [7, 7, 7],
            TriangleMaterialIndices: [0, 1, 2],
            Materials: [Material(0, 0), Material(0, 1), Material(1, 0)],
            TfragCount: 1,
            BoundsMin: (-1, 0, 0),
            BoundsMax: (1, 0, 1),
            TextureIds: [7]);

        var output = Rac1WorldImport.ToTfragMeshes(native, [7]).ToArray();
        Assert.Equal(3, output.Length);
        Assert.All(output, mesh =>
        {
            Assert.Equal("tfrag", mesh.AssetKind);
            Assert.Equal(7, mesh.TextureId);
            Assert.Equal(1, mesh.TriangleCount);
        });

        var expectedModes = new[]
        {
            (RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Repeat),
            (RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Clamp),
            (RuntimeTextureWrap.Clamp, RuntimeTextureWrap.Repeat),
        };
        for (int face = 0; face < 3; face++)
        {
            var mesh = Assert.Single(output, m =>
                m.MaterialPresentation?.WrapS == expectedModes[face].Item1 &&
                m.MaterialPresentation?.WrapT == expectedModes[face].Item2);
            var colours = Assert.IsType<float[]>(mesh.Colors);
            for (int corner = 0; corner < 3; corner++)
            {
                int sourceVertex = native.Indices[face * 3 + corner];
                int dst = mesh.Indices[corner];
                for (int axis = 0; axis < 3; axis++)
                {
                    Assert.Equal(native.Positions[sourceVertex * 3 + axis],
                        mesh.Positions[dst * 3 + axis]);
                    Assert.Equal(native.Colors[sourceVertex * 3 + axis],
                        colours[dst * 4 + axis]);
                }
                Assert.Equal(native.Uvs[sourceVertex * 2], mesh.Uvs[dst * 2]);
                Assert.Equal(native.Uvs[sourceVertex * 2 + 1], mesh.Uvs[dst * 2 + 1]);
                Assert.Equal(native.VertexAlpha[sourceVertex],
                    colours[dst * 4 + 3]);
            }
        }
    }

    [SkippableFact]
    public void Aridia_NativeAddressModesArePreservedPerTextureAndTriangle()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(entry => entry.LevelId == 2);
        var core = Rac1LevelCore.Open(disc, level);
        var source = RcTfrag.Read(core.Assets.AsSpan(core.Header.TfragsOffset,
            core.Header.TfragsEnd - core.Header.TfragsOffset).ToArray());
        var world = Rac1WorldImport.Build(disc, 2);

        // Native Aridia texture 5 covers the saved grid under Ratchet and
        // requests clamp/clamp. Neighbouring textures 1/4/18 repeat.
        // Texture 2 demonstrates independent S/T flags (the host leaves this
        // mode unchanged rather than forcing the wrong uniform repeat mode).
        foreach (var (textureId, s, t) in new[]
        {
            (5, RuntimeTextureWrap.Clamp, RuntimeTextureWrap.Clamp),
            (3, RuntimeTextureWrap.Clamp, RuntimeTextureWrap.Clamp),
            (1, RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Repeat),
            (4, RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Repeat),
            (18, RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Repeat),
            (2, RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Clamp),
        })
        {
            var mesh = Assert.Single(world.Meshes, m =>
                m.AssetKind == "tfrag" && m.TextureId == textureId);
            Assert.Equal(s, mesh.MaterialPresentation?.WrapS);
            Assert.Equal(t, mesh.MaterialPresentation?.WrapT);
        }

        // Group the original triangle corners by the same native material
        // identity as the importer. Verify their complete indexed stream,
        // not only total counts: source positions, UV repeats, baked colours
        // and winding must all survive the presentation-only change.
        var nativeFaces = new Dictionary<(int Texture, RuntimeTextureWrap S,
            RuntimeTextureWrap T), List<int>>();
        for (int face = 0; face < source.TriangleTextureIds.Length; face++)
        {
            int materialIndex = source.TriangleMaterialIndices[face];
            var material = materialIndex >= 0 && materialIndex < source.Materials.Count
                ? source.Materials[materialIndex] : null;
            var key = (
                source.TriangleTextureIds[face],
                Rac1WorldImport.DecodeTfragWrap(material?.ClampRawLo),
                Rac1WorldImport.DecodeTfragWrap(material?.ClampRawHi));
            if (!nativeFaces.TryGetValue(key, out var corners))
                nativeFaces[key] = corners = [];
            for (int corner = 0; corner < 3; corner++)
                corners.Add(source.Indices[face * 3 + corner]);
        }

        var actual = world.Meshes.Where(m => m.AssetKind == "tfrag").ToDictionary(
            m => (m.TextureId, m.MaterialPresentation?.WrapS ?? RuntimeTextureWrap.Unknown,
                m.MaterialPresentation?.WrapT ?? RuntimeTextureWrap.Unknown));
        Assert.Equal(nativeFaces.Count, actual.Count);
        foreach (var (key, sourceCorners) in nativeFaces)
        {
            Assert.True(actual.TryGetValue(key, out var mesh),
                $"Missing native tfrag material group {key}.");
            Assert.NotNull(mesh);
            var colours = Assert.IsType<float[]>(mesh.Colors);
            Assert.Equal(sourceCorners.Count, mesh.Indices.Length);

            for (int corner = 0; corner < sourceCorners.Count; corner++)
            {
                int src = sourceCorners[corner];
                int dst = mesh.Indices[corner];
                for (int axis = 0; axis < 3; axis++)
                {
                    Assert.Equal(source.Positions[src * 3 + axis],
                        mesh.Positions[dst * 3 + axis]);
                    Assert.Equal(source.Colors[src * 3 + axis],
                        colours[dst * 4 + axis]);
                }
                Assert.Equal(source.Uvs[src * 2], mesh.Uvs[dst * 2]);
                Assert.Equal(source.Uvs[src * 2 + 1], mesh.Uvs[dst * 2 + 1]);
                Assert.Equal(source.VertexAlpha[src], colours[dst * 4 + 3]);
            }
        }
        Assert.Equal(source.Indices.Length,
            actual.Values.Sum(m => m.Indices.Length));
    }
}

using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Retail-backed evidence for the packed TIE UV seam beside Ratchet on Veldin.</summary>
public sealed class Rac1TieUvWrapTests
{
    [SkippableFact]
    public void Veldin_RockFaceUnwrapsSixteenUnitTieTextureSeamWithoutChangingGeometry()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var world = Rac1WorldImport.Build(disc, 0);

        // The grey rock beside Ratchet in view-20261027-183948-053
        // resolves to TIE texture 58, face 214 in the welded runtime surface.
        var rock = Assert.Single(world.Meshes, m =>
            m.AssetKind == "tie" && m.TextureId == 58);
        Assert.True(rock.TriangleCount > 215);
        int at = 214 * 3;
        int v0 = rock.Indices[at], v1 = rock.Indices[at + 1], v2 = rock.Indices[at + 2];
        Assert.Equal(15.5f, rock.Uvs[v0 * 2]);
        Assert.Equal(15.5f, rock.Uvs[v1 * 2]);
        Assert.Equal(16f, rock.Uvs[v2 * 2]);
        Assert.Equal(0f, rock.Uvs[v0 * 2 + 1]);
        Assert.Equal(0.5f, rock.Uvs[v1 * 2 + 1]);
        Assert.Equal(0f, rock.Uvs[v2 * 2 + 1]);

        // Placement positions are unchanged; only the integer-period UV
        // representation of the wrapped corner differs.
        Assert.InRange(rock.Positions[v0 * 3], 167.90, 167.92);
        Assert.InRange(rock.Positions[v0 * 3 + 1], 31.02, 31.04);
        Assert.InRange(rock.Positions[v0 * 3 + 2], 114.81, 114.83);

        // No other face in this rock's texture batch still interpolates
        // through the packed sixteen-unit wrap boundary.
        for (int face = 0; face < rock.TriangleCount; face++)
        {
            int a = rock.Indices[face * 3] * 2;
            int b = rock.Indices[face * 3 + 1] * 2;
            int c = rock.Indices[face * 3 + 2] * 2;
            foreach (int component in new[] { 0, 1 })
            {
                float low = Math.Min(rock.Uvs[a + component],
                    Math.Min(rock.Uvs[b + component], rock.Uvs[c + component]));
                float high = Math.Max(rock.Uvs[a + component],
                    Math.Max(rock.Uvs[b + component], rock.Uvs[c + component]));
                Assert.True(high - low <= 8f,
                    $"TIE texture 58 face {face} still crosses a wide UV seam in component {component}: {low} to {high}.");
            }
        }
    }

    [SkippableFact]
    public void Veldin_TieSourceGeometryAndTriangleCountRemainUntouched()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(item => item.LevelId == 0);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(disc, level)).Ties;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).TieInstances;
        var world = Rac1WorldImport.Build(disc, 0);
        int nativeFaces = placements.Sum(instance =>
            classes[instance.OClass].Mesh.Indices.Length / 3);
        int runtimeFaces = world.Meshes.Where(m => m.AssetKind == "tie")
            .Sum(m => m.TriangleCount);
        Assert.Equal(nativeFaces, runtimeFaces);
    }

    [Fact]
    public void PackedUvSeamDecision_IsPortableAndAxisIndependent()
    {
        // Only the wrapped axis changes; the orthogonal axis stays authored.
        bool seamS = Rac1WorldImport.CrossesPackedTieUvSeam(
            15.5f, 0f, 15.5f, RuntimeTextureWrap.Repeat);
        bool ordinaryT = Rac1WorldImport.CrossesPackedTieUvSeam(
            0f, 0.5f, 0f, RuntimeTextureWrap.Repeat);
        Assert.True(seamS);
        Assert.False(ordinaryT);
        Assert.Equal(15.5, Rac1WorldImport.UnwrapPackedTieUv(15.5, seamS));
        Assert.Equal(16, Rac1WorldImport.UnwrapPackedTieUv(0, seamS));
        Assert.Equal(0.5, Rac1WorldImport.UnwrapPackedTieUv(0.5, ordinaryT));

        bool ordinaryS = Rac1WorldImport.CrossesPackedTieUvSeam(
            0f, 0.5f, 0f, RuntimeTextureWrap.Repeat);
        bool seamT = Rac1WorldImport.CrossesPackedTieUvSeam(
            15.5f, 0f, 15.5f, RuntimeTextureWrap.Repeat);
        Assert.False(ordinaryS);
        Assert.True(seamT);
        Assert.Equal(16, Rac1WorldImport.UnwrapPackedTieUv(0, seamT));
    }

    [Fact]
    public void PackedUvSeamDecision_PreservesClampAndOrdinaryFaces()
    {
        bool clamp = Rac1WorldImport.CrossesPackedTieUvSeam(
            15.5f, 0f, 15.5f, RuntimeTextureWrap.Clamp);
        bool ordinary = Rac1WorldImport.CrossesPackedTieUvSeam(
            0.5f, 1f, 0.5f, RuntimeTextureWrap.Repeat);
        bool nonBoundary = Rac1WorldImport.CrossesPackedTieUvSeam(
            15.5f, 0f, 7f, RuntimeTextureWrap.Repeat);
        Assert.False(clamp);
        Assert.False(ordinary);
        Assert.False(nonBoundary);
        Assert.Equal(0, Rac1WorldImport.UnwrapPackedTieUv(0, clamp));
        Assert.Equal(0.5, Rac1WorldImport.UnwrapPackedTieUv(0.5, ordinary));
        Assert.Equal(0, Rac1WorldImport.UnwrapPackedTieUv(0, nonBoundary));

        // Unspecified native wrap state keeps the original fallback.
        Assert.True(Rac1WorldImport.CrossesPackedTieUvSeam(15.5f, 0f, 15.5f, null));
    }
}

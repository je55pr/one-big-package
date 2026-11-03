using OBP.IO;
using OBP.PS2.Presentation;
using OBP.PS2.Iso;
using OBP.RAC2;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Shared PS2 TIE sampler-UV recovery across R&C1, GC and UYA.</summary>
public sealed class RcTieUvSeamTests
{
    [Theory]
    [InlineData(15.0f, 0.0f, 15.5f)]
    [InlineData(15.5f, 0.0f, 15.5f)]
    [InlineData(0.0f, 15.0f, 0.5f)]
    public void PackedBoundary_UnwrapsOnlyAcrossTheRepeatingSixteenTileSeam(
        float a, float b, float c)
    {
        Assert.True(RcTieUvSeam.CrossesPackedBoundary(a, b, c, RuntimeTextureWrap.Repeat));
        Assert.True(RcTieUvSeam.CrossesPackedBoundary(a, b, c, null));
        Assert.Equal(16d, RcTieUvSeam.Unwrap(0, true));
        Assert.Equal(16.5d, RcTieUvSeam.Unwrap(0.5, true));
        Assert.Equal(15d, RcTieUvSeam.Unwrap(15, true));
    }

    [Theory]
    [InlineData(0f, 0.5f, 1f)]
    [InlineData(13f, 13.25f, 13.5f)]
    [InlineData(15f, 0f, 7f)]
    public void OrdinaryUvRanges_AreLeftUntouched(float a, float b, float c)
    {
        Assert.False(RcTieUvSeam.CrossesPackedBoundary(a, b, c, RuntimeTextureWrap.Repeat));
        Assert.Equal(a, RcTieUvSeam.Unwrap(a, false));
        Assert.Equal(b, RcTieUvSeam.Unwrap(b, false));
        Assert.Equal(c, RcTieUvSeam.Unwrap(c, false));
    }

    [Fact]
    public void ClampMode_DoesNotRewriteAuthoritativeCoordinates()
    {
        Assert.False(RcTieUvSeam.CrossesPackedBoundary(15.5f, 0f, 15f, RuntimeTextureWrap.Clamp));
    }

    [SkippableFact]
    public void TodanoRockWall_UsesContinuousUvAcrossPackedTieSeam()
    {
        string? source = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(source) || !File.Exists(source),
            "Requires a locally supplied Going Commando disc.");

        using var disc = new FileRandomAccessReader(source!);
        var world = Rac2WorldImport.Build(disc, 12); // Todano

        // Captured 2026-10-29 at x=-370, y=138, z=155. This rock face
        // previously interpolated S=0 -> S=15 within a few metres, turning
        // one rock-texture tile into a wall of narrow streaks.
        var rock = Assert.Single(world.Meshes, m =>
            m.AssetKind == "tie" && m.TextureId == 43);
        const double epsilon = 0.035d;
        bool Near(int vertex, double x, double y, double z) =>
            Math.Abs(rock.Positions[vertex * 3] - x) < epsilon
            && Math.Abs(rock.Positions[vertex * 3 + 1] - y) < epsilon
            && Math.Abs(rock.Positions[vertex * 3 + 2] - z) < epsilon;

        bool found = false;
        for (int face = 0; face < rock.TriangleCount; face++)
        {
            var corners = new[] { rock.Indices[face * 3],
                rock.Indices[face * 3 + 1], rock.Indices[face * 3 + 2] };
            if (!corners.Any(v => Near(v, 370.02, 137.41, 154.57))
                || !corners.Any(v => Near(v, 370.74, 139.16, 154.42))
                || !corners.Any(v => Near(v, 370.38, 137.57, 156.98)))
                continue;

            var s = corners.Select(v => rock.Uvs[v * 2]).Order().ToArray();
            var t = corners.Select(v => rock.Uvs[v * 2 + 1]).Order().ToArray();
            Assert.Equal([15f, 16f, 16f], s);
            Assert.Equal([0.5f, 0.75f, 0.75f], t);
            Assert.True(s[2] - s[0] <= 1f);
            found = true;
            break;
        }

        Assert.True(found, "Todano source rock face was not retained in the imported geometry.");
    }
}

using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1NovalisWaterSurfacesTests
{
    [SkippableFact]
    public void OriginalNovalisCarriesDistinctLocalWaterHeights()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 1);
        var water = Rac1NovalisWaterSurfaces.FromWorld(world);

        Assert.InRange(water.SurfaceTriangleCount, 3_000, 3_936);
        Assert.True(water.TrySample(115, 115, out double waterworks));
        Assert.InRange(waterworks, 55, 57);
        var blob = Assert.Single(world.CollisionMeshes);
        (double X, double Z) RepresentativeAtHeight(double h)
        {
            for (int f = 0; f < blob.Triangles; f++)
            {
                int raw = blob.TriangleMaterialIds[f];
                if (raw is < 0 or > 255 ||
                    Rac1CollisionFaceSemantics.Decode(raw).SurfaceClass != 0)
                    continue;
                int[] ix = [blob.Indices[f * 3], blob.Indices[f * 3 + 1], blob.Indices[f * 3 + 2]];
                double y = ix.Average(i => blob.Positions[i * 3 + 1]);
                if (Math.Abs(y - h) > .1) continue;
                return (ix.Average(i => blob.Positions[i * 3]),
                    ix.Average(i => blob.Positions[i * 3 + 2]));
            }
            throw new InvalidDataException($"No Novalis class-zero triangle near y={h}.");
        }
        var upperPoint = RepresentativeAtHeight(82.25);
        Assert.True(water.TrySample(upperPoint.X, upperPoint.Z, out double elevated));
        Assert.InRange(elevated, 81, 83);
        var lowerPoint = RepresentativeAtHeight(39);
        Assert.True(water.TrySample(lowerPoint.X, lowerPoint.Z, out double lower));
        Assert.InRange(lower, 38, 40);
        Assert.False(water.TrySample(5000, 5000, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            water.TrySample(double.NaN, 0, out _));
    }
}

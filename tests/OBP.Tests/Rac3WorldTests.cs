using System.Text.Json;
using OBP.Core;
using OBP.IO;
using OBP.RAC3;
using Xunit;

namespace OBP.Tests;

public sealed class Rac3WorldTests
{
    [Fact]
    public void CataloguePreservesSparseRetailTableIdentity()
    {
        var c = Rac3DestinationCatalogue.Instance;
        Assert.Equal(ObpSourceGame.Rac3, c.Game);
        Assert.Equal(51, c.Destinations.Count);
        Assert.Null(c.FindByTableIndex(38));
        Assert.Equal("rac3:TABLE1", c.FindByTableIndex(1)!.DestinationId);
        Assert.Equal(50, Rac3WorldProvider.Instance.ResolveTableIndex(c.FindByTableIndex(50)!));
        Assert.False(Rac3WorldProvider.Instance.CanLoad(c.Destinations[0] with { DestinationId = "rac3:invented" }));
    }

    [SkippableTheory]
    [InlineData(1, 802, 1960, 1894, 735, 670, 8, 890426, 264313, 484)]
    [InlineData(8, 2876, 646, 499, 777, 481, 3, 394555, 112813, 279)]
    [InlineData(20, 383, 595, 1105, 200, 186, 6, 508483, 236309, 350)]
    [InlineData(50, 525, 365, 85, 198, 191, 5, 85769, 222264, 327)]
    public void RetailRowsMatchProductionReferenceCensus(int table, int tfrags, int ties, int shrubs, int mobies, int pvars, int sky,
        int renderTriangles, int collisionTriangles, int materials)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var result = Rac3WorldImport.BuildObserved(reader, table);
        var world = result.World;
        Assert.Equal("rac3", world.Game);
        Assert.Equal(Rac3Authority.Primary.BuildId, world.BuildId);
        Assert.Equal(table, world.LevelId);
        Assert.Equal(tfrags, result.TfragCount);
        Assert.Equal(ties, result.TieInstanceCount);
        Assert.Equal(shrubs, result.ShrubInstanceCount);
        Assert.Equal(mobies, result.MobyInstanceCount);
        Assert.Equal(pvars, result.MobiesWithPvar);
        Assert.Equal(sky, result.SkyShellCount);
        Assert.Equal(renderTriangles, world.TotalRenderTriangles);
        Assert.Equal(collisionTriangles, world.TotalCollisionTriangles);
        Assert.Equal(materials, world.MaterialCount);
        Assert.Equal(mobies, world.DynamicObjects!.Count);
        Assert.Equal(pvars, world.DynamicObjects.Count(o => o.NativePayloads?.Any(p => p.Format == "rac3-pvar-gc-layout-compat") == true));
        Assert.All(world.DynamicObjects, o => Assert.Equal(16, o.Transform.Matrix.Length));
        Assert.All(world.Meshes, m => Assert.All(m.Positions, v => Assert.True(double.IsFinite(v))));
    }

    [SkippableFact]
    public void RetailAllObservedRowsMatchCommittedProductionCensus()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        string path = Path.Combine(RepoPaths.Root, "research", "generated", "rac3-ntscu-original.production-import-census.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement rows = document.RootElement.GetProperty("rows");
        Assert.Equal(51, rows.GetArrayLength());
        using var reader = new FileRandomAccessReader(iso!);
        foreach (JsonElement row in rows.EnumerateArray())
        {
            int table = row.GetProperty("tableIndex").GetInt32();
            var result = Rac3WorldImport.BuildObserved(reader, table);
            var world = result.World;
            Assert.Equal(row.GetProperty("worldMeshCount").GetInt32(), world.Meshes.Count);
            Assert.Equal(row.GetProperty("worldRenderTriangles").GetInt32(), world.TotalRenderTriangles);
            Assert.Equal(row.GetProperty("collisionTriangles").GetInt32(), world.TotalCollisionTriangles);
            Assert.Equal(row.GetProperty("materialCount").GetInt32(), world.MaterialCount);
            Assert.Equal(row.GetProperty("mobyInstanceCount").GetInt32(), result.MobyInstanceCount);
            Assert.Equal(row.GetProperty("mobiesWithPvar").GetInt32(), result.MobiesWithPvar);
            Assert.Equal(row.GetProperty("tieInstanceCount").GetInt32(), result.TieInstanceCount);
            Assert.Equal(row.GetProperty("shrubInstanceCount").GetInt32(), result.ShrubInstanceCount);
            Assert.Equal(row.GetProperty("skyShellCount").GetInt32(), result.SkyShellCount);
        }
    }
}

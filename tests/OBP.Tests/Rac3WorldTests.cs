using System.Text.Json;
using OBP.Core;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime;
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
    [InlineData(1, 802, 1960, 1894, 735, 670, 8, 890426, 264313, 484, 221349, 36, 427)]
    [InlineData(8, 2876, 646, 499, 777, 481, 3, 394555, 112813, 279, 231198, 43, 668)]
    [InlineData(20, 383, 595, 1105, 200, 186, 6, 508483, 236309, 350, 77960, 30, 117)]
    [InlineData(50, 525, 365, 85, 198, 191, 5, 85769, 222264, 327, 66802, 42, 182)]
    public void RetailRowsMatchProductionReferenceCensus(int table, int tfrags, int ties, int shrubs, int mobies, int pvars, int sky,
        int renderTriangles, int collisionTriangles, int materials, int dynamicTriangles, int mobyModels, int linkedMobies)
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
        int animatedTriangles = world.AnimatedMeshes?.Sum(m => m.TriangleCount) ?? 0;
        int animatedInstances = AnimatedInstanceCount(world);
        Assert.Equal(dynamicTriangles, world.TotalDynamicTriangles + animatedTriangles);
        Assert.Equal(renderTriangles + dynamicTriangles, world.TotalRenderTriangles + world.TotalDynamicTriangles + animatedTriangles);
        Assert.Equal(linkedMobies, world.DynamicObjects!.Count(o => o.Meshes.Count > 0) + animatedInstances);
        Assert.Equal(mobyModels, ModelClassCount(world));
        Assert.Equal(mobies, world.DynamicObjects!.Count);
        Assert.Equal(pvars, world.DynamicObjects.Count(o => o.NativePayloads?.Any(p => p.Format == "rac3-pvar-gc-layout-compat") == true));
        Assert.All(world.DynamicObjects, o => Assert.Equal(16, o.Transform.Matrix.Length));
        Assert.All(world.Meshes, m => Assert.All(m.Positions, v => Assert.True(double.IsFinite(v))));
        if (table == 1) Assert.Equal(45, world.AnimatedMeshes?.Count);
        else Assert.Empty(world.AnimatedMeshes ?? Array.Empty<RuntimeAnimatedMesh>());
    }

    [SkippableFact]
    public void RetailVeldinExposesEvidenceSafeMobyAnimationPreview()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld world = Rac3WorldImport.Build(reader, 1);
        var animated = world.AnimatedMeshes;
        Assert.NotNull(animated);
        Assert.Equal(45, animated!.Count);
        Assert.Equal(34_301, animated.Sum(m => m.TriangleCount));
        Assert.Equal(38, AnimatedInstanceCount(world));

        var specs = new[]
        {
            new { OClass = 6800, Sequence = 2, InstanceIds = new[] { 665, 666, 667, 668 }, Surfaces = 2, Frames = 9, Fps = 7.5f, Triangles = 2_390 },
            new { OClass = 6577, Sequence = 2, InstanceIds = new[] { 513, 514, 515 }, Surfaces = 1, Frames = 11, Fps = 15f, Triangles = 2_457 },
            new { OClass = 6317, Sequence = 4, InstanceIds = new[] { 477, 478, 479 }, Surfaces = 2, Frames = 25, Fps = 7.5f, Triangles = 2_290 },
            new { OClass = 6886, Sequence = 15, InstanceIds = Enumerable.Range(672, 28).ToArray(), Surfaces = 1, Frames = 8, Fps = 15f, Triangles = 375 },
        };

        foreach (var spec in specs)
        {
            var classMeshes = animated.Where(m => m.Name.StartsWith($"uya-preview-moby{spec.OClass}_i", StringComparison.Ordinal)).ToArray();
            Assert.Equal(spec.InstanceIds.Length * spec.Surfaces, classMeshes.Length);
            Assert.Equal(spec.InstanceIds.Length * spec.Triangles, classMeshes.Sum(m => m.TriangleCount));
            Assert.All(classMeshes, m =>
            {
                Assert.Contains($"_s{spec.Sequence}_", m.Name, StringComparison.Ordinal);
                Assert.Equal(spec.Frames, m.Frames.Count);
                Assert.Equal(spec.Fps, m.FramesPerSecond, 3);
                Assert.All(m.Frames.SelectMany(f => f), v => Assert.True(double.IsFinite(v)));
            });
            Assert.Contains(classMeshes[0].Frames.Skip(1), frame =>
                frame.Where((v, i) => Math.Abs(v - classMeshes[0].Frames[0][i]) > 1e-4).Any());

            foreach (int instanceId in spec.InstanceIds)
            {
                var source = Assert.Single(world.DynamicObjects!, o => o.InstanceIndex == instanceId);
                Assert.Equal(spec.OClass, source.NativeClassId);
                Assert.Empty(source.Meshes);
                Assert.NotEmpty(source.NativePayloads!);
                Assert.Contains(classMeshes, m => m.Name.Contains($"_i{instanceId}_", StringComparison.Ordinal));
            }
        }
    }

    [SkippableTheory]
    [InlineData(1, 370.285888671875, 78.89811706542969, 95.85699462890625, 0.8845519423484802)]
    [InlineData(8, 174.20101928710938, 475.1811218261719, 170.3060760498047, 0.0)]
    public void RetailCampaignRowsExposeCompatibleShipStart(int table, double x, double y, double z, double yaw)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var ship = Rac3WorldImport.Build(reader, table).Ship;
        Assert.NotNull(ship);
        Assert.Equal(x, ship!.X, 5);
        Assert.Equal(y, ship.Y, 5);
        Assert.Equal(z, ship.Z, 5);
        Assert.Equal(yaw, ship.Yaw, 5);
    }

    [SkippableTheory]
    [InlineData(20)]
    [InlineData(50)]
    public void RetailDefaultShipTransformIsNotPromotedAsSpawn(int table)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        Assert.Null(Rac3WorldImport.Build(reader, table).Ship);
    }

    [SkippableFact]
    public void RetailRow1SkyMatchesPinnedNativeEvidenceAndMateriallessBackdrop()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var opened = UyaLevelCore.Open(reader, 1);
        var range = UyaLevelCore.SectionRange(opened.Core, opened.Core.Header.Sky);
        Assert.NotNull(range);
        Assert.Equal(2_731_712, range!.Value.Offset);
        Assert.Equal(485_248, range.Value.Size);
        byte[] bytes = opened.Core.Assets.AsSpan(range.Value.Offset, range.Value.Size).ToArray();
        Assert.Equal(
            "825afb60c18db867575bbaaa2ebe4bc911f242ad815e65c74d4d64cc29b98fb0",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());

        var sky = UyaSky.Read(bytes);
        Assert.Equal(10, sky.Textures.Count);
        Assert.Equal(8, sky.Shells.Count);
        Assert.Equal(181, sky.Shells.Sum(shell => shell.ClusterCount));
        Assert.Equal(4_111, sky.Shells.Sum(shell => shell.Positions.Length / 3));
        Assert.Equal(4_348, sky.Shells.Sum(shell => shell.Indices.Length / 3));
        Assert.Equal(4, sky.Shells.Count(shell => shell.AngularVelocityRaw.Any(v => v != 0)));
        Assert.DoesNotContain(sky.Shells, shell => shell.Bloom);
        Assert.Equal(new short[] { 2, 3, 4, 5 }, sky.Shells
            .Select(shell => shell.AngularVelocityRaw[2]).Where(v => v != 0).Order().ToArray());

        RuntimeWorld world = Rac3WorldImport.Build(reader, 1);
        Assert.Equal(4_348, world.Meshes.Where(m => m.AssetKind == "sky").Sum(m => m.TriangleCount));
        Assert.Equal(10, world.Textures.Count(t => t.AssetKind == "sky"));
        RuntimeMesh backdrop = Assert.Single(world.Meshes, m => m.AssetKind == "sky" && m.RenderWithoutTexture);
        Assert.Equal(-1, backdrop.TextureId);
        Assert.Equal(1_596, backdrop.TriangleCount);
        Assert.Equal(890_426, world.TotalRenderTriangles);
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
        int admittedShipStarts = 0;
        int defaultShipTransforms = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            int table = row.GetProperty("tableIndex").GetInt32();
            var result = Rac3WorldImport.BuildObserved(reader, table);
            var world = result.World;
            Assert.Equal(row.GetProperty("worldMeshCount").GetInt32(), world.Meshes.Count);
            Assert.Equal(row.GetProperty("worldRenderTriangles").GetInt32(), world.TotalRenderTriangles);
            Assert.Equal(row.GetProperty("collisionTriangles").GetInt32(), world.TotalCollisionTriangles);
            Assert.Equal(row.GetProperty("materialCount").GetInt32(), world.MaterialCount);
            int animatedTriangles = world.AnimatedMeshes?.Sum(m => m.TriangleCount) ?? 0;
            Assert.Equal(row.GetProperty("expandedMobyTriangles").GetInt32(), world.TotalDynamicTriangles + animatedTriangles);
            Assert.Equal(row.GetProperty("totalVisibleTriangles").GetInt32(), world.TotalRenderTriangles + world.TotalDynamicTriangles + animatedTriangles);
            Assert.Equal(row.GetProperty("linkedMobyInstanceCount").GetInt32(), world.DynamicObjects!.Count(o => o.Meshes.Count > 0) + AnimatedInstanceCount(world));
            Assert.Equal(row.GetProperty("mobyModelCount").GetInt32(), ModelClassCount(world));
            Assert.Equal(row.GetProperty("mobyInstanceCount").GetInt32(), result.MobyInstanceCount);
            Assert.Equal(row.GetProperty("mobiesWithPvar").GetInt32(), result.MobiesWithPvar);
            Assert.Equal(row.GetProperty("tieInstanceCount").GetInt32(), result.TieInstanceCount);
            Assert.Equal(row.GetProperty("shrubInstanceCount").GetInt32(), result.ShrubInstanceCount);
            Assert.Equal(row.GetProperty("skyShellCount").GetInt32(), result.SkyShellCount);
            if (world.Ship is null) defaultShipTransforms++;
            else admittedShipStarts++;
        }
        Assert.Equal(21, admittedShipStarts);
        Assert.Equal(30, defaultShipTransforms);
    }
    private static int AnimatedInstanceCount(RuntimeWorld world)
    {
        return world.AnimatedMeshes?
            .Select(m => m.Name.Contains("_t", StringComparison.Ordinal) ? m.Name[..m.Name.LastIndexOf("_t", StringComparison.Ordinal)] : m.Name)
            .Distinct(StringComparer.Ordinal)
            .Count() ?? 0;
    }

    private static int ModelClassCount(RuntimeWorld world)
    {
        var classes = world.DynamicObjects!.Where(o => o.Meshes.Count > 0).Select(o => o.NativeClassId).ToHashSet();
        const string prefix = "uya-preview-moby";
        foreach (var mesh in world.AnimatedMeshes ?? Array.Empty<RuntimeAnimatedMesh>())
        {
            int end = mesh.Name.IndexOf("_i", prefix.Length, StringComparison.Ordinal);
            if (mesh.Name.StartsWith(prefix, StringComparison.Ordinal) && end > prefix.Length &&
                int.TryParse(mesh.Name[prefix.Length..end], out int oClass)) classes.Add(oClass);
        }
        return classes.Count;
    }

}

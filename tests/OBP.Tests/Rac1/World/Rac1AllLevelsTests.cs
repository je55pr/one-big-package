using OBP.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.RAC1;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>All-level retail survival and structural invariants for the native R&C1 world path.</summary>
public sealed class Rac1AllLevelsTests
{
    public static IEnumerable<object[]> NativeLevels => Enumerable.Range(0, 19).Select(level => new object[] { level });

    [Fact]
    public void RuntimeCensusRetainsEveryNativeLevel()
    {
        using var census = ReadCensus();
    }

    private static JsonDocument ReadCensus()
    {
        string path = Path.Combine(RepoPaths.Root, "research", "generated", "rac1-dynamic-moby-runtime-census.json");
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        try
        {
            RequireCensus(doc.RootElement);
            return doc;
        }
        catch { doc.Dispose(); throw; }
    }

    private static readonly string[] CensusCountFields =
    [
        "placements", "linkedInstances", "animatedHandoffInstances", "noGeometryInstances",
        "dynamicTriangles", "staticTriangles", "animatedTriangles", "animationCapableInstances",
    ];

    public static IEnumerable<object[]> CensusCountFaults =>
        from field in CensusCountFields
        from fault in new[] { "missing", "negative", "fractional", "overflow" }
        select new object[] { field, fault };

    [Theory]
    [MemberData(nameof(CensusCountFaults))]
    public void RuntimeCensusRejectsIncompleteCountFields(string field, string fault)
    {
        var census = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root,
            "research/generated/rac1-dynamic-moby-runtime-census.json")))!;
        var row = census["levels"]![0]!.AsObject();
        switch (fault)
        {
            case "missing": row.Remove(field); break;
            case "negative": row[field] = -1; break;
            case "fractional": row[field] = 1.5; break;
            case "overflow": row[field] = (long)int.MaxValue + 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => RequireCensus(JsonSerializer.SerializeToElement(census)));
    }

    private static void RequireCensus(JsonElement census)
    {
        var levels = census.GetProperty("levels").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(0, 19), levels.Select(row => row.GetProperty("level").GetInt32()).Order());
        foreach (var row in levels)
            foreach (string field in CensusCountFields)
                Assert.True(row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out int count) && count >= 0, $"Census count '{field}' must be a nonnegative integer.");
    }

    [SkippableTheory]
    [MemberData(nameof(NativeLevels))]
    public void NativeWorldBuildsWithFiniteLinkedGeometry(int levelId)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var census = ReadCensus();
        var row = census.RootElement.GetProperty("levels").EnumerateArray()
            .Single(row => row.GetProperty("level").GetInt32() == levelId);
        int placements = row.GetProperty("placements").GetInt32();
        int linkedInstances = row.GetProperty("linkedInstances").GetInt32();
        int animatedHandoffs = row.GetProperty("animatedHandoffInstances").GetInt32();
        int noGeometryInstances = row.GetProperty("noGeometryInstances").GetInt32();
        int dynamicTriangles = row.GetProperty("dynamicTriangles").GetInt32();
        int staticTriangles = row.GetProperty("staticTriangles").GetInt32();
        int animatedTriangles = row.GetProperty("animatedTriangles").GetInt32();
        int animationCapableInstances = row.GetProperty("animationCapableInstances").GetInt32();
        using var reader = Rac1VerifiedDisc.Open(iso!);
        var world = Rac1WorldImport.Build(reader, levelId);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == levelId);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level)).Mobies;
        var dynamicObjects = Assert.IsAssignableFrom<IReadOnlyList<OBP.Runtime.RuntimeDynamicObject>>(world.DynamicObjects);

        // The generated census predates #397's presentation-only suppression of
        // Veldin's unregistered, wholly untextured class 1964. Preserve the
        // original census evidence while accounting for its 30 hidden triangles.
        int unpresentedInstances = levelId == 0 ? 1 : 0;
        Assert.Equal(placements, dynamicObjects.Count);
        Assert.Equal(linkedInstances - unpresentedInstances, dynamicObjects.Count(o => o.Meshes.Count > 0));
        int noGeometry = dynamicObjects.Count(o => o.Meshes.Count == 0 &&
            (!classes.TryGetValue(o.NativeClassId, out var cls) || cls.Mesh.Indices.Length == 0));
        Assert.Equal(noGeometryInstances, noGeometry);
        Assert.Equal(animatedHandoffs,
            dynamicObjects.Count - (linkedInstances - unpresentedInstances) - noGeometry - unpresentedInstances);
        Assert.Equal(unpresentedInstances,
            dynamicObjects.Count(o => o.NativeClassId == 1964 && o.Meshes.Count == 0));
        Assert.Equal(staticTriangles, world.TotalRenderTriangles);
        Assert.Equal(dynamicTriangles - 30 * unpresentedInstances, world.TotalDynamicTriangles);
        Assert.Equal(animatedTriangles, (world.AnimatedMeshes ?? []).Sum(m => m.TriangleCount));
        Assert.Equal(animationCapableInstances, dynamicObjects.Count(o => o.Animations is not null));
        Assert.Equal(animationCapableInstances, dynamicObjects.Count(o => o.Animations is not null));

        Assert.Equal((WorldContentIdentity)ObpSourceGame.Rac1, world.Game);
        Assert.Equal(levelId, world.LevelId);
        Assert.NotEmpty(world.Meshes);
        Assert.NotEmpty(world.Textures);
        Assert.NotEmpty(world.CollisionMeshes);
        Assert.NotNull(world.Environment);
        Assert.False(world.Environment!.IsSphericalWorld);
        Assert.NotNull(world.Ship);

        Assert.True(double.IsFinite(world.Bounds.Min.X));
        Assert.True(double.IsFinite(world.Bounds.Min.Y));
        Assert.True(double.IsFinite(world.Bounds.Min.Z));
        Assert.True(double.IsFinite(world.Bounds.Max.X));
        Assert.True(double.IsFinite(world.Bounds.Max.Y));
        Assert.True(double.IsFinite(world.Bounds.Max.Z));
        Assert.True(world.Bounds.Min.X <= world.Bounds.Max.X);
        Assert.True(world.Bounds.Min.Y <= world.Bounds.Max.Y);
        Assert.True(world.Bounds.Min.Z <= world.Bounds.Max.Z);

        var textureKeys = world.Textures.Select(texture => (texture.AssetKind, texture.TextureId)).ToHashSet();
        Assert.All(world.Textures, texture => Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length));
        Assert.All(world.Meshes, mesh =>
        {
            Assert.Equal(0, mesh.Positions.Length % 3);
            Assert.Equal(0, mesh.Indices.Length % 3);
            Assert.All(mesh.Positions, value => Assert.True(double.IsFinite(value)));
            Assert.All(mesh.Uvs, value => Assert.True(float.IsFinite(value)));
            Assert.All(mesh.Indices, index => Assert.InRange(index, 0, mesh.Positions.Length / 3 - 1));
            if (mesh.TextureId >= 0) Assert.Contains((mesh.AssetKind, mesh.TextureId), textureKeys);
        });

        Assert.All(world.CollisionMeshes, collision =>
        {
            Assert.Equal(collision.Triangles * 3, collision.Indices.Length);
            Assert.Equal(collision.Triangles, collision.TriangleMaterialIds.Length);
            Assert.All(collision.Positions, value => Assert.True(double.IsFinite(value)));
            Assert.All(collision.Indices, index => Assert.InRange(index, 0, collision.VertexCount - 1));
        });

        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "tfrag");
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "sky");
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "tie");
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "shrub");
        Assert.NotNull(world.DynamicObjects);
        Assert.Contains(world.DynamicObjects!, obj => obj.Meshes.Count > 0);
        Assert.All(world.DynamicObjects!, obj =>
        {
            Assert.Equal(ObpSourceGame.Rac1, obj.SourceGame);
            Assert.Equal(16, obj.Transform.Matrix.Length);
            Assert.All(obj.Transform.Matrix, value => Assert.True(double.IsFinite(value)));
            Assert.All(obj.Meshes, mesh =>
            {
                Assert.Equal("moby", mesh.AssetKind);
                Assert.Equal(0, mesh.Positions.Length % 3);
                Assert.Equal(0, mesh.Indices.Length % 3);
                Assert.All(mesh.Positions, value => Assert.True(double.IsFinite(value)));
                Assert.All(mesh.Uvs, value => Assert.True(float.IsFinite(value)));
                Assert.All(mesh.Indices, index => Assert.InRange(index, 0, mesh.Positions.Length / 3 - 1));
                if (mesh.TextureId >= 0) Assert.Contains((mesh.AssetKind, mesh.TextureId), textureKeys);
            });
            if (obj.Animations is { } animations)
            {
                Assert.Equal(OBP.Runtime.RuntimeObjectAnimationRole.Rest, animations.InitialRole);
                Assert.NotEmpty(animations.Clips);
                Assert.All(animations.Clips, clip =>
                {
                    Assert.True(clip.FrameCount > 0);
                    Assert.Equal(clip.FrameCount, clip.FrameDurationsSeconds.Count);
                    Assert.NotEmpty(clip.Surfaces);
                    Assert.All(clip.FrameDurationsSeconds, duration => Assert.True(duration > 0 && double.IsFinite(duration)));
                    Assert.All(clip.Surfaces, surface =>
                    {
                        Assert.InRange(surface.SurfaceIndex, 0, obj.Meshes.Count - 1);
                        Assert.Equal(clip.FrameCount, surface.FrameCount);
                        Assert.All(surface.LocalFrames, frame =>
                        {
                            Assert.Equal(obj.Meshes[surface.SurfaceIndex].Positions.Length, frame.Length);
                            Assert.All(frame, value => Assert.True(double.IsFinite(value)));
                        });
                    });
                });
            }
        });
    }
}

using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>Retail equivalence for the native R&amp;C1 RuntimeWorld slice.</summary>
public sealed class Rac1LevelTests_World
{
    [SkippableFact]
    public void Level0_NativeRuntimeWorldMatchesReferenceStaticWorldSlice()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);

        var world = Rac1WorldImport.Build(reader, 0);

        Assert.Equal("rac1", world.Game);
        Assert.Equal(Rac1Authority.Primary.BuildId, world.BuildId);
        Assert.Equal(0, world.LevelId);
        Assert.Equal("LEVEL0", world.DisplayName);

        Assert.Equal(286, world.Meshes.Count);
        Assert.Equal(751_435, world.TotalRenderTriangles);
        int Meshes(string kind) => world.Meshes.Count(m => m.AssetKind == kind);
        int Tris(string kind) => world.Meshes.Where(m => m.AssetKind == kind).Sum(m => m.TriangleCount);
        Assert.Equal((78, 24_520), (Meshes("tfrag"), Tris("tfrag")));
        Assert.Equal((7, 2_366), (Meshes("sky"), Tris("sky")));
        Assert.Equal((131, 396_708), (Meshes("tie"), Tris("tie")));
        Assert.Equal((70, 327_841), (Meshes("shrub"), Tris("shrub")));
        Assert.Equal((0, 0), (Meshes("moby"), Tris("moby")));

        Assert.All(world.Meshes.Where(m => m.AssetKind == "tfrag"), mesh =>
        {
            Assert.NotNull(mesh.Colors);
            Assert.Equal(mesh.Positions.Length / 3 * 4, mesh.Colors!.Length);
            Assert.Equal(mesh.Positions.Length / 3 * 2, mesh.Uvs.Length);
            Assert.Equal(0, mesh.Indices.Length % 3);
        });
        Assert.All(world.Meshes.Where(m => m.AssetKind == "sky"), mesh =>
        {
            Assert.NotNull(mesh.Colors);
            Assert.Equal(mesh.Positions.Length / 3 * 4, mesh.Colors!.Length);
            Assert.Equal(mesh.Positions.Length / 3 * 2, mesh.Uvs.Length);
            Assert.Equal(0, mesh.Indices.Length % 3);
        });

        Assert.Equal(411, world.MaterialCount);
        Assert.Equal(410, world.Textures.Count);
        Assert.Equal(78, world.Textures.Count(t => t.AssetKind == "tfrag"));
        Assert.Equal(8, world.Textures.Count(t => t.AssetKind == "sky"));
        Assert.Equal(131, world.Textures.Count(t => t.AssetKind == "tie"));
        Assert.Equal(70, world.Textures.Count(t => t.AssetKind == "shrub"));
        Assert.Equal(123, world.Textures.Count(t => t.AssetKind == "moby"));
        Assert.All(world.Textures, texture =>
            Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length));
        var textureKeys = world.Textures.Select(t => (t.AssetKind, t.TextureId)).ToHashSet();
        Assert.All(world.Meshes.Where(m => m.TextureId >= 0), mesh =>
            Assert.Contains((mesh.AssetKind, mesh.TextureId), textureKeys));

        var collision = Assert.Single(world.CollisionMeshes);
        Assert.Equal(5_783, collision.Octants);
        Assert.Equal(109_751, collision.VertexCount);
        Assert.Equal(86_184, collision.Triangles);
        Assert.Equal(86_184, world.TotalCollisionTriangles);
        Assert.Equal(collision.Triangles * 3, collision.Indices.Length);
        Assert.Equal(collision.Triangles, collision.TriangleMaterialIds.Length);
        Assert.Equal(new[] { 9, 10, 12, 31 }, collision.TriangleMaterialIds.Distinct().Order().ToArray());

        Assert.Equal(2.5745086669921875, world.Bounds.Min.X, 9);
        Assert.Equal(-5.954422950744629, world.Bounds.Min.Y, 9);
        Assert.Equal(-56.46558770118281, world.Bounds.Min.Z, 9);
        Assert.Equal(352.2431854769455, world.Bounds.Max.X, 9);
        Assert.Equal(101.66961976741732, world.Bounds.Max.Y, 9);
        Assert.Equal(427.795684825629, world.Bounds.Max.Z, 9);

        Assert.NotNull(world.Environment);
        Assert.False(world.Environment!.IsSphericalWorld);
        Assert.Equal((71 / 255.0, 66 / 255.0, 58 / 255.0), world.Environment.BackgroundColour);
        Assert.Equal((47 / 255.0, 26 / 255.0, 15 / 255.0), world.Environment.FogColour);
        Assert.Equal(0f, world.Environment.FogNearDistance);
        Assert.Equal(235f, world.Environment.FogFarDistance);
        Assert.Equal(255f, world.Environment.FogNearIntensity);
        Assert.Equal(53.54999923706055f, world.Environment.FogFarIntensity);
        Assert.Equal(27f, world.Environment.DeathHeight);

        Assert.NotNull(world.Ship);
        Assert.Equal(20, world.Ship!.X);
        Assert.Equal(20, world.Ship.Y);
        Assert.Equal(20, world.Ship.Z);
        Assert.Equal(0, world.Ship.Yaw);

        Assert.Null(world.Lighting);
        Assert.NotNull(world.AnimatedMeshes);
        var ratchet = world.AnimatedMeshes!.Where(m => m.Name.StartsWith("ratchet_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, ratchet.Length);
        Assert.Equal(6_856, ratchet.Sum(m => m.TriangleCount));
        Assert.Equal(900_909, world.TotalRenderTriangles + world.TotalDynamicTriangles + ratchet.Sum(m => m.TriangleCount));
        Assert.All(ratchet, mesh =>
        {
            Assert.Equal("moby", mesh.AssetKind);
            Assert.Equal(5_583, mesh.VertexCount);
            Assert.Equal(10, mesh.Frames.Count);
            Assert.Equal(7.5f, mesh.FramesPerSecond);
            Assert.All(mesh.Frames, frame => Assert.Equal(mesh.VertexCount * 3, frame.Length));
        });
    }
    [SkippableFact]
    public void Level0_MobiesPreserveRuntimeIdentityAndRenderAccounting()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 0);
        var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
        var world = Rac1WorldImport.Build(reader, 0);

        var dynamicObjects = Assert.IsAssignableFrom<IReadOnlyList<OBP.Runtime.RuntimeDynamicObject>>(world.DynamicObjects);
        Assert.Equal(296, dynamicObjects.Count);
        Assert.Equal(285, dynamicObjects.Count(o => o.Meshes.Count > 0));
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level)).Mobies;
        Assert.Equal(10, dynamicObjects.Count(o => o.Meshes.Count == 0 && (!classes.TryGetValue(o.NativeClassId, out var cls) || cls.Mesh.Indices.Length == 0)));
        Assert.Single(dynamicObjects, o => o.Meshes.Count == 0 && classes.TryGetValue(o.NativeClassId, out var cls) && cls.Mesh.Indices.Length > 0);
        Assert.Equal(142_618, world.TotalDynamicTriangles);
        Assert.Equal(894_053, world.TotalRenderTriangles + world.TotalDynamicTriangles);

        var class500 = dynamicObjects.Where(o => o.NativeClassId == 500).ToArray();
        Assert.Equal(103, class500.Length);
        Assert.All(class500, o => { Assert.Equal("rac1", o.SourceGame); Assert.Equal("moby:500", o.ModelRef); Assert.Equal(108, o.Meshes.Sum(m => m.TriangleCount)); });
        var specimen = class500[0];
        Assert.Equal(new[] { 53 }, specimen.Meshes.Select(m => m.TextureId).Distinct().ToArray());
        Assert.Equal(16, specimen.Transform.Matrix.Length);
        var source = gameplay.MobyInstances[specimen.InstanceIndex];
        Assert.Equal(500, source.OClass);
        var local = specimen.Meshes[0].Positions;
        double lx = local[0], ly = local[1], lz = local[2];
        var native = Rac1Instances.TransformMobyPoint(source, lx, lz, ly);
        var m = specimen.Transform.Matrix;
        Assert.Equal(native.X, m[0] * lx + m[4] * ly + m[8] * lz + m[12], 9);
        Assert.Equal(native.Z, m[1] * lx + m[5] * ly + m[9] * lz + m[13], 9);
        Assert.Equal(native.Y, m[2] * lx + m[6] * ly + m[10] * lz + m[14], 9);
    }

    [SkippableFact]
    public void Level1_Class1134StaysAtRestUntilNativeSelectorIsRecovered()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 1);
        Assert.Equal(383, world.Meshes.Count);
        Assert.Equal(1_118_293, world.TotalRenderTriangles);
        Assert.Equal(520_649, world.TotalDynamicTriangles);
        Assert.Equal(6_856, (world.AnimatedMeshes ?? []).Sum(m => m.TriangleCount));
        Assert.Equal(1_645_798, world.TotalRenderTriangles + world.TotalDynamicTriangles + (world.AnimatedMeshes ?? []).Sum(m => m.TriangleCount));

        var class1134 = world.DynamicObjects!.Where(o => o.NativeClassId == 1134).ToArray();
        Assert.Equal(3, class1134.Length);
        Assert.Equal(282, class1134.Sum(o => o.Meshes.Sum(m => m.TriangleCount)));
        Assert.All(class1134, obj => Assert.Null(obj.Animations));
    }

    [SkippableFact]
    public void Level2_Class766StaysAtRestUntilNativeSelectorIsRecovered()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 2);
        Assert.Equal(250, world.Meshes.Count);
        Assert.Equal(687_037, world.TotalRenderTriangles);
        Assert.Equal(409_559, world.TotalDynamicTriangles);
        Assert.Equal(6_856, (world.AnimatedMeshes ?? []).Sum(m => m.TriangleCount));
        Assert.Equal(1_103_452, world.TotalRenderTriangles + world.TotalDynamicTriangles + (world.AnimatedMeshes ?? []).Sum(m => m.TriangleCount));

        var hierarchy = world.DynamicObjects!.Where(o => o.NativeClassId == 766).ToArray();
        Assert.Equal(54, hierarchy.Length);
        Assert.Equal(5_184, hierarchy.Sum(o => o.Meshes.Sum(m => m.TriangleCount)));
        Assert.All(hierarchy, obj => Assert.Null(obj.Animations));
    }

}

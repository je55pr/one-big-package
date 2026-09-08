using OBP.IO;
using OBP.RAC1;

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

        Assert.Equal(319, world.Meshes.Count);
        Assert.Equal(900_909, world.TotalRenderTriangles);
        int Meshes(string kind) => world.Meshes.Count(m => m.AssetKind == kind);
        int Tris(string kind) => world.Meshes.Where(m => m.AssetKind == kind).Sum(m => m.TriangleCount);
        Assert.Equal((78, 24_520), (Meshes("tfrag"), Tris("tfrag")));
        Assert.Equal((7, 2_366), (Meshes("sky"), Tris("sky")));
        Assert.Equal((131, 396_708), (Meshes("tie"), Tris("tie")));
        Assert.Equal((70, 327_841), (Meshes("shrub"), Tris("shrub")));
        Assert.Equal((33, 149_474), (Meshes("moby"), Tris("moby")));

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
        Assert.Null(world.AnimatedMeshes);
    }
    [SkippableFact]
    public void Level1_PinnedClass1134InstancesUseNativeAnimatedMeshPath()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 1);
        Assert.Equal(522, world.Meshes.Count);
        Assert.Equal(1_645_516, world.TotalRenderTriangles);
        Assert.Equal((139, 527_223),
            (world.Meshes.Count(m => m.AssetKind == "moby"), world.Meshes.Where(m => m.AssetKind == "moby").Sum(m => m.TriangleCount)));
        Assert.NotNull(world.AnimatedMeshes);
        Assert.Equal(3, world.AnimatedMeshes!.Count);
        Assert.Equal(282, world.AnimatedMeshes.Sum(m => m.TriangleCount));
        Assert.Equal(1_645_798, world.TotalRenderTriangles + world.AnimatedMeshes.Sum(m => m.TriangleCount));
        Assert.All(world.AnimatedMeshes, mesh =>
        {
            Assert.Equal("moby", mesh.AssetKind);
            Assert.Equal(109, mesh.VertexCount);
            Assert.Equal(94, mesh.TriangleCount);
            Assert.Equal(170, mesh.Frames.Count);
            Assert.Equal(30f, mesh.FramesPerSecond);
            Assert.All(mesh.Frames, frame => Assert.Equal(mesh.VertexCount * 3, frame.Length));
        });
    }

}

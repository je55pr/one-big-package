using OBP.IO;
using OBP.RAC1;

namespace OBP.Tests;

/// <summary>All-level retail survival and structural invariants for the native R&C1 world path.</summary>
public sealed class Rac1AllLevelsTests
{
    public static IEnumerable<object[]> NativeLevels => Enumerable.Range(0, 19).Select(level => new object[] { level });

    [SkippableTheory]
    [MemberData(nameof(NativeLevels))]
    public void NativeWorldBuildsWithFiniteLinkedGeometry(int levelId)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, levelId);

        Assert.Equal("rac1", world.Game);
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
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "moby");
    }
}

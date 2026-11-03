using OBP.RAC1;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>Retail-backed clearance regression for Veldin's authored ground decorations (#398).</summary>
public sealed class Rac1VeldinDecalClearanceTests
{
    [SkippableFact]
    public void Level0_Class500GroundShrubKeepsNativeSubCentimetreHeightAboveTerrain()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(entry => entry.LevelId == 0);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(disc, level)).Shrubs;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;
        // The 150th placement in retail Veldin creates the ground decoration
        // visible in view-20261027-175829-211, over terrain textures 3 and 4.
        var placement = placements[149];
        Assert.Equal(500, placement.OClass);
        var cls = classes[placement.OClass];
        Assert.Equal(42, cls.Mesh.Indices.Length / 3);
        Assert.All(cls.TriangleTextureIds, id => Assert.Equal(17, id));

        var source = new List<(double X, double Y, double Z)>();
        for (int i = 0; i < cls.Mesh.Positions.Length; i += 3)
        {
            var (x, y, z) = Rac1Instances.TransformPoint(
                placement.Matrix,
                cls.Mesh.Positions[i], cls.Mesh.Positions[i + 1], cls.Mesh.Positions[i + 2]);
            source.Add((x, z, y));
        }

        // Native shrub vertices are 0.0025 world units above Y=30.5000 terrain.
        // Rounding their Y to two decimals made the two distinct planes coplanar.
        Assert.All(source, vertex => Assert.InRange(vertex.Y, 30.5024, 30.5026));
        double centreX = (source.Min(v => v.X) + source.Max(v => v.X)) * 0.5;
        double centreZ = (source.Min(v => v.Z) + source.Max(v => v.Z)) * 0.5;

        var world = Rac1WorldImport.Build(disc, 0);
        var surface = Assert.Single(world.Meshes, mesh =>
        {
            if (mesh.AssetKind != "shrub" || mesh.TextureId != 17 || mesh.TriangleCount != 42)
                return false;
            var xs = Enumerable.Range(0, mesh.Positions.Length / 3)
                .Select(i => mesh.Positions[i * 3]).ToArray();
            var zs = Enumerable.Range(0, mesh.Positions.Length / 3)
                .Select(i => mesh.Positions[i * 3 + 2]).ToArray();
            return Math.Abs((xs.Min() + xs.Max()) * 0.5 - centreX) < 0.02 &&
                   Math.Abs((zs.Min() + zs.Max()) * 0.5 - centreZ) < 0.02;
        });
        Assert.All(Enumerable.Range(0, surface.Positions.Length / 3), vertex =>
            Assert.InRange(surface.Positions[vertex * 3 + 1], 30.5024, 30.5026));
        Assert.True(surface.MaterialPresentation?.AlphaBlendEnabled);

        // The opaque ground remains in its original source-authored position.
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "tfrag" &&
            mesh.TextureId is 3 or 4 &&
            Enumerable.Range(0, mesh.Positions.Length / 3).Any(i =>
                Math.Abs(mesh.Positions[i * 3 + 1] - 30.5) < 0.0001 &&
                Math.Abs(mesh.Positions[i * 3] - centreX) < 8 &&
                Math.Abs(mesh.Positions[i * 3 + 2] - centreZ) < 8));
    }
}

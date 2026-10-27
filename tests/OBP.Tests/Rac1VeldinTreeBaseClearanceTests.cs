using OBP.RAC1;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>Retail-backed clearance for the coplanar tree-root overlay on Veldin.</summary>
public sealed class Rac1VeldinTreeBaseClearanceTests
{
    [SkippableFact]
    public void Class501Texture13RendersAboveUnmovedTerrainAtSavedTreeBase()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = Rac1VerifiedDisc.Open(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(entry => entry.LevelId == 0);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(disc, level)).Shrubs;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;

        // Saved view-20261027-195543-771: directly under Ratchet and around
        // the tree, this native class-501 root ring is coplanar with tfrag 21.
        var placement = placements[155];
        Assert.Equal(501, placement.OClass);
        var cls = classes[placement.OClass];
        Assert.Equal(119, cls.TriangleTextureIds.Count(id => id == 13));

        var sourceHeights = new List<double>();
        for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
        {
            if (cls.TriangleTextureIds[face] != 13)
                continue;
            for (int corner = 0; corner < 3; corner++)
            {
                int v = cls.Mesh.Indices[face * 3 + corner];
                var (_, _, nativeZ) = Rac1Instances.TransformPoint(
                    placement.Matrix,
                    cls.Mesh.Positions[v * 3],
                    cls.Mesh.Positions[v * 3 + 1],
                    cls.Mesh.Positions[v * 3 + 2]);
                sourceHeights.Add(nativeZ);
            }
        }
        Assert.InRange(sourceHeights.Min(), 29.4999, 29.5001);

        var world = Rac1WorldImport.Build(disc, 0);
        var rootRing = Assert.Single(world.Meshes, mesh =>
        {
            if (mesh.AssetKind != "shrub" || mesh.TextureId != 13 ||
                mesh.TriangleCount != 119)
                return false;
            var xs = Enumerable.Range(0, mesh.Positions.Length / 3)
                .Select(i => mesh.Positions[i * 3]).ToArray();
            var zs = Enumerable.Range(0, mesh.Positions.Length / 3)
                .Select(i => mesh.Positions[i * 3 + 2]).ToArray();
            return xs.Min() < 172.63 && xs.Max() > 172.63 &&
                   zs.Min() < 116.78 && zs.Max() > 116.78;
        });

        var renderedHeights = Enumerable.Range(0, rootRing.Positions.Length / 3)
            .Select(i => rootRing.Positions[i * 3 + 1]).ToArray();
        // Rendering-only shim: 0.005 world units, not a change to the
        // authenticated retail geometry, tfrag surface, or collision.
        Assert.InRange(renderedHeights.Min() - sourceHeights.Min(), 0.0048, 0.0052);
        Assert.InRange(renderedHeights.Max() - sourceHeights.Max(), 0.0048, 0.0052);
        Assert.Contains(world.Meshes, mesh => mesh.AssetKind == "tfrag" &&
            mesh.TextureId == 21 &&
            Enumerable.Range(0, mesh.Positions.Length / 3).Any(i =>
                Math.Abs(mesh.Positions[i * 3 + 1] - 29.5) < 0.0001));
    }
}

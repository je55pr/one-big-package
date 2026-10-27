using OBP.PS2.Geometry;
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
        var core = Rac1LevelCore.Open(disc, level);
        var classes = Rac1StaticClasses.Read(core).Shrubs;
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).ShrubInstances;

        // Saved view-20261027-195543-771: directly under Ratchet and around
        // the tree, this native class-501 root ring is coplanar with tfrag 21.
        var placement = placements[155];
        Assert.Equal(501, placement.OClass);
        var cls = classes[placement.OClass];
        Assert.Equal(119, cls.TriangleTextureIds.Count(id => id == 13));

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

        // Compare ALL 119 emitted triangles, in the native face order.
        // Wielded runtime vertices can be shared but each corner must retain
        // the exact original X/Z and native shrub Y rounded to four places,
        // plus only the proven 0.005-unit render-only separation.
        int runtimeFace = 0;
        for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
        {
            if (cls.TriangleTextureIds[face] != 13)
                continue;
            for (int corner = 0; corner < 3; corner++)
            {
                int sourceIndex = cls.Mesh.Indices[face * 3 + corner];
                var (nativeX, nativeZ, nativeY) = Rac1Instances.TransformPoint(
                    placement.Matrix,
                    cls.Mesh.Positions[sourceIndex * 3],
                    cls.Mesh.Positions[sourceIndex * 3 + 1],
                    cls.Mesh.Positions[sourceIndex * 3 + 2]);

                int runtimeIndex = rootRing.Indices[runtimeFace * 3 + corner];
                double actualX = rootRing.Positions[runtimeIndex * 3];
                double actualY = rootRing.Positions[runtimeIndex * 3 + 1];
                double actualZ = rootRing.Positions[runtimeIndex * 3 + 2];

                Assert.Equal(Round2(nativeX), actualX, precision: 7);
                Assert.Equal(Round2(nativeZ), actualZ, precision: 7);
                Assert.Equal(0.005d, actualY - Round4(nativeY), precision: 7);
            }
            runtimeFace++;
        }
        Assert.Equal(119, runtimeFace);
        Assert.Equal(119 * 3, rootRing.Indices.Length);

        // The source terrain fragment family has its own native vertices and
        // is emitted without the shrub offset. Compare ONLY texture-21
        // vertices beneath this tree, not an arbitrary vertex elsewhere.
        var terrain = RcTfrag.Read(core.Assets.AsSpan(
            core.Header.TfragsOffset,
            core.Header.TfragsEnd - core.Header.TfragsOffset).ToArray());
        const double xCenter = 172.63;
        const double zCenter = 116.78;
        const double footprintRadius = 3d;
        static bool AtTree(double x, double z) =>
            Math.Abs(x - xCenter) < footprintRadius &&
            Math.Abs(z - zCenter) < footprintRadius;

        var nativeTerrain = new HashSet<(double X, double Y, double Z)>();
        for (int face = 0; face < terrain.TriangleTextureIds.Length; face++)
        {
            if (terrain.TriangleTextureIds[face] != 21)
                continue;
            for (int corner = 0; corner < 3; corner++)
            {
                int v = terrain.Indices[face * 3 + corner];
                double x = terrain.Positions[v * 3];
                double y = terrain.Positions[v * 3 + 1];
                double z = terrain.Positions[v * 3 + 2];
                if (AtTree(x, z))
                    nativeTerrain.Add((x, y, z));
            }
        }

        var runtimeTerrainMesh = Assert.Single(world.Meshes, m =>
            m.AssetKind == "tfrag" && m.TextureId == 21);
        var runtimeTerrain = new HashSet<(double X, double Y, double Z)>();
        for (int vertex = 0; vertex < runtimeTerrainMesh.Positions.Length / 3; vertex++)
        {
            double x = runtimeTerrainMesh.Positions[vertex * 3];
            double y = runtimeTerrainMesh.Positions[vertex * 3 + 1];
            double z = runtimeTerrainMesh.Positions[vertex * 3 + 2];
            if (AtTree(x, z))
                runtimeTerrain.Add((x, y, z));
        }

        Assert.NotEmpty(nativeTerrain);
        Assert.Contains(nativeTerrain, p => Math.Abs(p.Y - 29.5) < 0.0001);
        Assert.True(nativeTerrain.SetEquals(runtimeTerrain),
            "Terrain texture 21 differs from source at the saved tree-base footprint.");
    }

    private static double Round2(double value) => Math.Floor(value * 100 + 0.5) / 100 + 0.0;
    private static double Round4(double value) => Math.Floor(value * 10000 + 0.5) / 10000 + 0.0;
}

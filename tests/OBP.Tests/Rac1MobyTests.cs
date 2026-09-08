using OBP.IO;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>Retail equivalence for R&C1 Moby bind/rest-pose surface recovery.</summary>
public sealed class Rac1MobyTests
{
    [SkippableFact]
    public void Level1_BindPoseClassesMatchReferenceCensus()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        var level = catalogue.Levels.Single(l => l.LevelId == 1);
        var core = Rac1LevelCore.Open(reader, level);
        var classes = Rac1StaticClasses.Read(core);

        Assert.Equal(169, classes.Mobies.Count);
        Assert.Equal(1_440, classes.Mobies.Values.Sum(c => c.Mesh.HighLodPacketCount));
        Assert.Equal(125_923, classes.Mobies.Values.Sum(c => c.Mesh.Positions.Length / 3));
        Assert.Equal(127_773, classes.Mobies.Values.Sum(c => c.Mesh.Indices.Length / 3));
        Assert.Equal(2, classes.Mobies.Values.Count(c => c.Mesh.HighLodPacketCount == 0));
        Assert.All(classes.Mobies.Values, cls =>
        {
            Assert.Equal(cls.Mesh.Positions.Length / 3 * 2, cls.Mesh.Uvs.Length);
            Assert.Equal(cls.Mesh.Indices.Length / 3, cls.TriangleTextureIds.Length);
            Assert.All(cls.TriangleTextureIds, id =>
            {
                if (id >= 0) Assert.InRange(id, 0, core.Header.MobyTextures.Count - 1);
            });
        });
    }

    [SkippableFact]
    public void AllLevels_BindPoseAndPlacementLinkageMatchReferenceCensus()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        long payloads = 0, packets = 0, vertices = 0, triangles = 0, empty = 0;
        long placements = 0, linked = 0, rigid = 0, animated = 0, unlinked = 0;
        foreach (var level in catalogue.Levels.OrderBy(l => l.LevelId))
        {
            var core = Rac1LevelCore.Open(reader, level);
            var classes = Rac1StaticClasses.Read(core);
            var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
            Assert.Equal(level.LevelId == 17 ? 400 : 256, gameplay.SpawnableMobyCount);
            payloads += classes.Mobies.Count;
            packets += classes.Mobies.Values.Sum(c => c.Mesh.HighLodPacketCount);
            vertices += classes.Mobies.Values.Sum(c => c.Mesh.Positions.Length / 3);
            triangles += classes.Mobies.Values.Sum(c => c.Mesh.Indices.Length / 3);
            empty += classes.Mobies.Values.Count(c => c.Mesh.Indices.Length == 0);
            placements += gameplay.MobyInstances.Count;
            foreach (var instance in gameplay.MobyInstances)
            {
                if (!classes.Mobies.TryGetValue(instance.OClass, out var cls) || cls.Mesh.Indices.Length == 0) { unlinked++; continue; }
                linked++; if (cls.JointCount == 0) rigid++; else animated++;
            }
        }
        Assert.Equal((2_968L, 22_227L, 1_920_636L, 1_933_983L, 38L), (payloads, packets, vertices, triangles, empty));
        Assert.Equal((16_232L, 15_359L, 9_122L, 6_237L, 873L), (placements, linked, rigid, animated, unlinked));
    }
}

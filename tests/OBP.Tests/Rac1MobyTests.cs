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
            if (cls.JointCount == 0)
            {
                Assert.Empty(cls.Joints);
                Assert.Empty(cls.Mesh.VertexJoints);
                Assert.Empty(cls.Mesh.VertexWeights);
            }
            else if (cls.Mesh.HighLodPacketCount > 0)
            {
                Assert.Equal(cls.JointCount, cls.Joints.Count);
                Assert.Equal(cls.Mesh.Positions.Length, cls.Mesh.VertexJoints.Length);
                Assert.Equal(cls.Mesh.Positions.Length, cls.Mesh.VertexWeights.Length);
            }
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
        long animatedClasses = 0, animatedPackets = 0, animatedInFileVertices = 0;
        long matrixTransfers = 0, twoWayVertices = 0, threeWayVertices = 0, geometryFreeAnimated = 0;
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
            foreach (var cls in classes.Mobies.Values.Where(c => c.JointCount > 0))
            {
                if (cls.Mesh.HighLodPacketCount == 0)
                {
                    geometryFreeAnimated++;
                    Assert.Empty(cls.Joints);
                    Assert.Empty(cls.Mesh.VertexJoints);
                    Assert.Empty(cls.Mesh.VertexWeights);
                    continue;
                }

                animatedClasses++;
                animatedPackets += cls.Mesh.HighLodPacketCount;
                animatedInFileVertices += cls.Mesh.InFileVertexCount;
                matrixTransfers += cls.Mesh.MatrixTransferCount;
                twoWayVertices += cls.Mesh.TwoWayBlendVertexCount;
                threeWayVertices += cls.Mesh.ThreeWayBlendVertexCount;
                Assert.Equal(cls.JointCount, cls.Joints.Count);
                Assert.Equal(cls.Mesh.Positions.Length, cls.Mesh.VertexJoints.Length);
                Assert.Equal(cls.Mesh.Positions.Length, cls.Mesh.VertexWeights.Length);
                for (int v = 0; v < cls.Mesh.Positions.Length / 3; v++)
                {
                    int b = v * 3;
                    float sum = cls.Mesh.VertexWeights[b] + cls.Mesh.VertexWeights[b + 1] + cls.Mesh.VertexWeights[b + 2];
                    Assert.InRange(sum, 0.99999f, 1.00001f);
                    for (int k = 0; k < 3; k++)
                    {
                        if (cls.Mesh.VertexWeights[b + k] > 0f)
                            Assert.InRange(cls.Mesh.VertexJoints[b + k], 0, cls.JointCount - 1);
                    }
                }
            }
            placements += gameplay.MobyInstances.Count;
            foreach (var instance in gameplay.MobyInstances)
            {
                if (!classes.Mobies.TryGetValue(instance.OClass, out var cls) || cls.Mesh.Indices.Length == 0) { unlinked++; continue; }
                linked++; if (cls.JointCount == 0) rigid++; else animated++;
            }
        }
        Assert.Equal((2_968L, 22_227L, 1_920_636L, 1_933_983L, 38L), (payloads, packets, vertices, triangles, empty));
        Assert.Equal((16_232L, 15_359L, 9_122L, 6_237L, 873L), (placements, linked, rigid, animated, unlinked));
        Assert.Equal((1_407L, 16_963L, 1_314_409L, 4_310L, 47_245L, 16_245L, 38L),
            (animatedClasses, animatedPackets, animatedInFileVertices, matrixTransfers, twoWayVertices, threeWayVertices, geometryFreeAnimated));
    }
}

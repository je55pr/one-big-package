using OBP.IO;
using OBP.RAC1.Animation;
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
    [SkippableFact]
    public void AllLevels_AnimationStructureMatchesReferenceCensus()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        long slots = 0, present = 0, frames = 0, quaternions = 0, joints = 0;
        long jointlessSlots = 0, jointlessPresent = 0;
        foreach (var level in catalogue.Levels.OrderBy(l => l.LevelId))
        {
            var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level));
            foreach (var cls in classes.Mobies.Values)
            {
                joints += cls.Joints.Count;
                Assert.All(cls.Joints, joint => Assert.Equal(15, joint.NativeAffine.Length));
                slots += cls.Sequences.Count;
                if (cls.JointCount == 0) jointlessSlots += cls.Sequences.Count;
                for (int slotIndex = 0; slotIndex < cls.Sequences.Count; slotIndex++)
                {
                    var slot = cls.Sequences[slotIndex];
                    Assert.Equal(slotIndex, slot.Index);
                    if (slot.Value is not { } sequence) continue;
                    present++;
                    if (cls.JointCount == 0) jointlessPresent++;
                    Assert.Equal(slotIndex, sequence.Index);
                    Assert.Equal(sequence.TriggerCount, sequence.Triggers.Count);
                    Assert.Equal(sequence.Frames.Count, sequence.FrameEntries.Count);
                    frames += sequence.Frames.Count;
                    foreach (var frame in sequence.Frames)
                    {
                        Assert.Equal(cls.JointCount * 8, frame.JointDataSize);
                        Assert.Equal(frame.Thing1Count, frame.Thing1.Count);
                        Assert.Equal(frame.Thing2Count, frame.Thing2.Count);
                        int payloadBytes = frame.JointDataSize + (frame.Thing1Count + frame.Thing2Count) * 8;
                        Assert.Equal((payloadBytes + 15) & ~15, frame.DataSizeQwords * 16);
                        Assert.Equal(cls.JointCount, frame.JointRotations.Count);
                        foreach (var q in frame.JointRotations)
                        {
                            quaternions++;
                            double norm = q.Xf * q.Xf + q.Yf * q.Yf + q.Zf * q.Zf + q.Wf * q.Wf;
                            Assert.InRange(norm, 0.999, 1.001);
                        }
                    }
                }
            }
        }
        Assert.Equal((13_697L, 10_745L, 2_952L, 109_156L, 3_110_018L, 28_193L),
            (slots, present, slots - present, frames, quaternions, joints));
        Assert.Equal((1_526L, 1_526L), (jointlessSlots, jointlessPresent));
    }

    [SkippableFact]
    public void Level1_Class1134_SingleJointPoseHasRetailRestAnchorAndAnimatedFrames()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 1);
        var cls = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level)).Mobies[1134];

        Assert.Single(cls.Joints);
        var rest = Assert.IsType<Rac1MobyAnimation.Sequence>(cls.Sequences[0].Value);
        var animated = Assert.IsType<Rac1MobyAnimation.Sequence>(cls.Sequences[1].Value);
        Assert.Single(rest.Frames);
        Assert.Equal(170, animated.Frames.Count);
        Assert.True(Rac1MobyPose.CanPoseSingleJointRigid(cls.Mesh, cls.Joints, rest.Frames[0]));
        Assert.True(Rac1MobyPose.IsRestAnchor(cls.Joints[0], rest.Frames[0]));

        var posedRest = Rac1MobyPose.PoseSingleJointRigid(cls.Mesh, cls.Joints, rest.Frames[0]);
        double restError = posedRest.Zip(cls.Mesh.Positions, (a, b) => System.Math.Abs(a - b)).Max();
        Assert.InRange(restError, 0, 0.00002);

        var posedAnimated = Rac1MobyPose.PoseSingleJointRigid(cls.Mesh, cls.Joints, animated.Frames[0]);
        double animatedDelta = posedAnimated.Zip(cls.Mesh.Positions, (a, b) => System.Math.Abs(a - b)).Max();
        Assert.True(animatedDelta > 0.5, $"Expected a visibly distinct retail animation pose, got max delta {animatedDelta}.");
    }

    [SkippableFact]
    public void Level2_Class766_RigidHierarchyReproducesRestAndMovesFourJointMesh()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 2);
        var cls = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level)).Mobies[766];
        Assert.Equal(4, cls.Joints.Count);
        var rest = Assert.IsType<Rac1MobyAnimation.Sequence>(cls.Sequences[0].Value);
        var animated = Assert.IsType<Rac1MobyAnimation.Sequence>(cls.Sequences[1].Value);
        Assert.Single(rest.Frames);
        Assert.Equal(16, animated.Frames.Count);
        Assert.True(Rac1MobyPose.CanPoseRigidHierarchy(cls.Mesh, cls.Joints, rest.Frames[0]));
        Assert.True(Rac1MobyPose.IsRigidHierarchyRestAnchor(cls.Joints, rest.Frames[0]));
        var posedRest = Rac1MobyPose.PoseRigidHierarchy(cls.Mesh, cls.Joints, rest.Frames[0]);
        double restError = posedRest.Zip(cls.Mesh.Positions, (a, b) => Math.Abs(a - b)).Max();
        Assert.InRange(restError, 0, 0.002);

        var posedAnimated = Rac1MobyPose.PoseRigidHierarchy(cls.Mesh, cls.Joints, animated.Frames[1]);
        Assert.All(posedAnimated, value => Assert.True(double.IsFinite(value)));
        double animatedDelta = posedAnimated.Zip(cls.Mesh.Positions, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(animatedDelta > 0.01,
            $"Expected the retail four-joint animation to move, got max delta {animatedDelta}.");
    }

    [SkippableFact]
    public void AllLevels_RigidHierarchyRestAnchorsReproduceStoredSurface()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        int occurrences = 0;
        long vertices = 0;
        double maxRestError = 0;
        foreach (var level in catalogue.Levels.OrderBy(l => l.LevelId))
        {
            var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level));
            foreach (var cls in classes.Mobies.Values.Where(c => c.JointCount > 1))
            {
                var rest = cls.Sequences.SingleOrDefault(s => s.Index == 0)?.Value;
                if (rest is null || rest.Frames.Count != 1 ||
                    !Rac1MobyPose.IsRigidHierarchyRestAnchor(cls.Joints, rest.Frames[0])) continue;
                Assert.True(Rac1MobyPose.CanPoseRigidHierarchy(cls.Mesh, cls.Joints, rest.Frames[0]));
                var posed = Rac1MobyPose.PoseRigidHierarchy(cls.Mesh, cls.Joints, rest.Frames[0]);
                double error = posed.Zip(cls.Mesh.Positions, (a, b) => Math.Abs(a - b)).Max();
                Assert.InRange(error, 0, 0.002);
                maxRestError = Math.Max(maxRestError, error);
                occurrences++;
                vertices += cls.Mesh.Positions.Length / 3;
            }
        }
        Assert.Equal(286, occurrences);
        Assert.Equal(206_213L, vertices);
        Assert.InRange(maxRestError, 0, 0.002);
    }

    [SkippableFact]
    public void AllLevels_RigidHierarchyMovingSequencesStayFiniteAndBounded()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        int occurrences = 0, sequences = 0, frames = 0;
        double maxBoundRatio = 0;
        foreach (var level in catalogue.Levels.OrderBy(l => l.LevelId))
        {
            var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level));
            foreach (var cls in classes.Mobies.Values.Where(c => c.JointCount > 1))
            {
                var rest = cls.Sequences.SingleOrDefault(s => s.Index == 0)?.Value;
                if (rest is null || rest.Frames.Count != 1 ||
                    !Rac1MobyPose.IsRigidHierarchyRestAnchor(cls.Joints, rest.Frames[0])) continue;
                var moving = cls.Sequences.Where(s => s.Value is { Frames.Count: > 1 }).Select(s => s.Value!).ToArray();
                if (moving.Length == 0) continue;
                occurrences++;
                double restExtent = Math.Max(1e-6, cls.Mesh.Positions.Max(Math.Abs));
                foreach (var sequence in moving)
                {
                    sequences++;
                    foreach (var frame in sequence.Frames)
                    {
                        Assert.True(Rac1MobyPose.CanPoseRigidHierarchy(cls.Mesh, cls.Joints, frame));
                        var posed = Rac1MobyPose.PoseRigidHierarchy(cls.Mesh, cls.Joints, frame);
                        double posedExtent = 0;
                        foreach (double value in posed)
                        {
                            Assert.True(double.IsFinite(value));
                            posedExtent = Math.Max(posedExtent, Math.Abs(value));
                        }
                        double ratio = posedExtent / restExtent;
                        maxBoundRatio = Math.Max(maxBoundRatio, ratio);
                        Assert.InRange(ratio, 0, 1.26);
                        frames++;
                    }
                }
            }
        }
        Assert.Equal(35, occurrences);
        Assert.Equal(130, sequences);
        Assert.Equal(3_630, frames);
        Assert.InRange(maxBoundRatio, 1.25, 1.26);
    }

    [SkippableFact]
    public void AllLevels_AnimationTimingMatchesRetailRateEquation()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        long adjacentPairs = 0, zeroDurationPairs = 0, variableRateSequences = 0;
        foreach (var level in catalogue.Levels.OrderBy(l => l.LevelId))
        {
            var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level));
            foreach (var sequence in classes.Mobies.Values.SelectMany(c => c.Sequences).Where(s => s.Value is not null).Select(s => s.Value!))
            {
                if (sequence.Frames.Select(f => f.TransitionRateRaw).Distinct().Skip(1).Any())
                {
                    variableRateSequences++;
                    Assert.Equal(0u, sequence.ConstantTransitionRateRaw);
                }
                for (int i = 0; i + 1 < sequence.Frames.Count; i++)
                {
                    var frame = sequence.Frames[i];
                    var next = sequence.Frames[i + 1];
                    int delta = next.TimestampUnits - frame.TimestampUnits;
                    Assert.True(delta >= 0, $"R&C1 animation timestamp regressed at level {level.LevelId}, sequence {sequence.Index}, frame {i}.");
                    uint expected = delta == 0
                        ? 0x7f800000u
                        : unchecked((uint)BitConverter.SingleToInt32Bits(8f / delta));
                    Assert.Equal(expected, frame.TransitionRateRaw);
                    adjacentPairs++;
                    if (delta == 0) zeroDurationPairs++;
                }
            }
        }
        Assert.Equal(98_411L, adjacentPairs);
        Assert.Equal(5L, zeroDurationPairs);
        Assert.Equal(586L, variableRateSequences);
    }

    [SkippableFact]
    public void Levels1Through18_Class1134RemainsThePinnedRuntimeAnimationSpecimen()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        int placements = 0;
        foreach (var level in catalogue.Levels.Where(l => l.LevelId is >= 1 and <= 18).OrderBy(l => l.LevelId))
        {
            var core = Rac1LevelCore.Open(reader, level);
            var classes = Rac1StaticClasses.Read(core);
            var cls = classes.Mobies[1134];
            var rest = Assert.IsType<Rac1MobyAnimation.Sequence>(cls.Sequences.Single(s => s.Index == 0).Value);
            var animated = Assert.IsType<Rac1MobyAnimation.Sequence>(cls.Sequences.Single(s => s.Index == 1).Value);
            Assert.Single(rest.Frames);
            Assert.Equal(170, animated.Frames.Count);
            Assert.Equal(0x3f000000u, animated.ConstantTransitionRateRaw);
            Assert.Equal(0.5f, animated.ConstantTransitionRate);
            Assert.True(Rac1MobyPose.CanPoseSingleJointRigid(cls.Mesh, cls.Joints, rest.Frames[0]));
            Assert.True(Rac1MobyPose.IsRestAnchor(cls.Joints[0], rest.Frames[0]));
            Assert.All(animated.Frames, frame => Assert.True(Rac1MobyPose.CanPoseSingleJointRigid(cls.Mesh, cls.Joints, frame)));
            var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
            placements += gameplay.MobyInstances.Count(i => i.OClass == 1134);
        }
        Assert.Equal(40, placements);
    }

}

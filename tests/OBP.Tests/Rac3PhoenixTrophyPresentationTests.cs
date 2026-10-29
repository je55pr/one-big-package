using OBP.IO;
using OBP.PS2.Geometry;
using OBP.RAC3;
using OBP.RAC3.Level;
using OBP.Runtime;
using Xunit;

namespace OBP.Tests;

public sealed class Rac3PhoenixTrophyPresentationTests
{
    [Fact]
    public void RigidJointEligibilityRequiresCompleteSingleJointInfluences()
    {
        var mesh = new GcUyaMoby.Mesh(
            [0d, 0d, 0d, 1d, 1d, 1d], [], [], [], 1f,
            Skinned: false, SkinningApplied: false)
        {
            SkinLocalPositions = [0d, 0d, 0d, 1d, 1d, 1d],
            VertexJoints = [0, 0, 0, 1, 0, 0],
            VertexWeights = [1f, 0f, 0f, 1f, 0f, 0f],
        };

        Assert.True(Ps2MobyNativePose.HasRigidJointBinding(mesh, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(mesh, 1));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with { VertexWeights = [1f, 0f, 0f] }, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with
            {
                Positions = [1d, 2d],
                SkinLocalPositions = [1d, 2d],
                VertexJoints = [0, 0],
                VertexWeights = [1f, 0f],
            }, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with { VertexWeights = [0.5f, 0.5f, 0f, 1f, 0f, 0f] }, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with { VertexWeights = [float.NaN, 0f, 0f, 1f, 0f, 0f] }, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with { VertexWeights = [1f, float.PositiveInfinity, 0f, 1f, 0f, 0f] }, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with { SkinStateFullyResolved = false }, 2));
        Assert.False(Ps2MobyNativePose.HasRigidJointBinding(
            mesh with { Skinned = true }, 2));
    }

    [SkippableFact]
    public void PhoenixTrophyFiguresStandAndRingJointsStayTogetherWithoutMovingInstances()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld legacy = Rac3WorldImport.BuildObserved(reader, 3, nativeMobyPoseOverride: false).World;
        RuntimeWorld corrected = Rac3WorldImport.BuildObserved(reader, 3, nativeMobyPoseOverride: true).World;
        var core = Rac3LevelCore.Open(reader, 3).Core;
        var originalMeshes = Rac3Assets.ReadMobyClasses(core,
            Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Moby).Count,
            nativePoseOverride: true).Decoded;
        var oldObjects = legacy.DynamicObjects!.ToDictionary(m => (m.NativeClassId, m.InstanceIndex));
        var newObjects = corrected.DynamicObjects!.ToDictionary(m => (m.NativeClassId, m.InstanceIndex));

        Assert.Equal(oldObjects.Keys.Order(), newObjects.Keys.Order());
        foreach (var key in oldObjects.Keys)
            Assert.Equal(oldObjects[key].Transform.Matrix, newObjects[key].Transform.Matrix);

        // These fifteen classes represent Phoenix's miniature and ring displays;
        // some classes have multiple placed instances. Y is native Z-up converted.
        for (int classId = 7501; classId <= 7515; classId++)
        {
            var figures = corrected.DynamicObjects!
                .Where(m => m.NativeClassId == classId).ToArray();
            Assert.NotEmpty(figures);
            foreach (var figure in figures)
            {
                var vertices = figure.Meshes.SelectMany(mesh => Enumerable.Range(0, mesh.Positions.Length / 3)
                    .Select(i => (X: mesh.Positions[i * 3], Y: mesh.Positions[i * 3 + 1],
                        Z: mesh.Positions[i * 3 + 2]))).ToArray();
                Assert.NotEmpty(vertices);
                double width = vertices.Max(v => v.X) - vertices.Min(v => v.X);
                double height = vertices.Max(v => v.Y) - vertices.Min(v => v.Y);
                if (classId <= 7509)
                {
                    // Preserve the retail reveal key's horizontal orientation
                    // exactly; a 180-degree yaw can keep the same height.
                    var cls = originalMeshes[classId];
                    Assert.NotNull(cls.NativePoseSource);
                    var restKey = cls.Sequences.Single(s => s.Index == 0).Frames[0];
                    var revealKey = cls.Sequences.Single(s => s.Index == 1).Frames[1] with
                    {
                        TranslationOverrides = restKey.TranslationOverrides,
                    };
                    var expectedPose = Ps2MobyNativePose.Pose(cls.NativePoseSource!, cls.Mesh, revealKey);
                    var expectedHorizontal = cls.Mesh.Indices.Distinct().Select(index => (
                        X: Math.Round(expectedPose[index * 3], 5),
                        Z: Math.Round(expectedPose[index * 3 + 1], 5))).ToHashSet();
                    var actualHorizontal = vertices.Select(v => (
                        X: Math.Round(v.X, 5), Z: Math.Round(v.Z, 5))).ToHashSet();
                    Assert.Equal(expectedHorizontal.Count, actualHorizontal.Count);
                    Assert.All(expectedHorizontal, point => Assert.Contains(point, actualHorizontal));
                    Assert.True(height > width * 1.2,
                        $"Trophy figurine class {classId} is still lying sideways: width={width}, height={height}.");
                    // Figurines were collected in flight, so their model origin
                    // is at the pickup centre. The back-row shelf is at local
                    // Y=0, and no rendered geometry should be buried beneath it.
                    Assert.InRange(vertices.Min(v => v.Y), -1e-5, 1e-5);
                }
                else
                {
                    // The non-collectible ring trophies in front already sit
                    // correctly. Their geometry must remain identical to the
                    // original native first-key pose.
                    var native = originalMeshes[classId].Mesh;
                    double originalMinY = native.Indices.Min(i => native.Positions[i * 3 + 2]);
                    double originalMaxY = native.Indices.Max(i => native.Positions[i * 3 + 2]);
                    Assert.Equal(originalMinY, vertices.Min(v => v.Y), 5);
                    Assert.Equal(originalMaxY, vertices.Max(v => v.Y), 5);
                    Assert.InRange(vertices.Min(v => v.Y), -0.8, -0.5);
                    Assert.True(height > 1.3,
                        $"Trophy ring class {classId} is still separated: height={height}.");
                }
            }
        }
    }
}

using OBP.Godot;
using OBP.IO;
using OBP.RAC1;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class RuntimeWorldSceneCollisionIdentityTests
{
    [Fact]
    public void DynamicObjectTransformRoundTripsThroughGodotSceneSpace()
    {
        var source = new RuntimeObjectTransform(
        [
            0.5d, 0.25d, -0.75d, 0d,
            -0.125d, 1.25d, 0.375d, 0d,
            0.625d, -0.5d, 0.875d, 0d,
            132.09d, 31.4266d, 115.48d, 1d,
        ]);

        var scene = RuntimeWorldScene.ToSceneTransform(source);
        var roundTrip = RuntimeWorldScene.ToRuntimeTransform(scene);

        Assert.Equal(16, roundTrip.Matrix.Length);
        for (int i = 0; i < source.Matrix.Length; i++)
            Assert.Equal(source.Matrix[i], roundTrip.Matrix[i], precision: 5);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    public void GodotFacesMapBackThroughExplicitSourceIdentity(
        int godotFaceIndex,
        int expectedTriangle)
    {
        Assert.Equal(
            expectedTriangle,
            RuntimeWorldScene.SourceTriangleIndexForFace([2, 0, 2, 1, 0, 1], godotFaceIndex));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    public void FaceMappingRejectsInvalidOrPastEndFaces(int godotFaceIndex)
    {
        Assert.Null(
            RuntimeWorldScene.SourceTriangleIndexForFace([2, 0, 2, 1, 0, 1], godotFaceIndex));
    }

    [Theory]
    [InlineData(0, 31)]
    [InlineData(1, 9)]
    [InlineData(2, 31)]
    [InlineData(3, 10)]
    [InlineData(4, 9)]
    [InlineData(5, 10)]
    public void MappedFacesRecoverOriginalCollisionMaterialIds(
        int godotFaceIndex,
        int expectedMaterial)
    {
        var collision = new RuntimeCollisionBlob(
            Octants: 1,
            Positions:
            [
                0, 0, 0,
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
            ],
            Indices:
            [
                0, 1, 2,
                0, 2, 3,
                0, 3, 1,
            ],
            TriangleMaterialIds: [9, 10, 31]);

        Assert.Equal(
            expectedMaterial,
            RuntimeWorldScene.MaterialIdForFace(collision, [2, 0, 2, 1, 0, 1], godotFaceIndex));
    }

    [Fact]
    public void CollisionShapeConsolidatesRepeatedGeometryAndWindingWithoutChangingTheSource()
    {
        var source = new RuntimeCollisionBlob(4,
            [0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1],
            [0, 1, 2, 1, 2, 0, 2, 1, 0, 3, 4, 5], [31, 31, 31, 31]);
        double[] originalPositions = source.Positions.ToArray();
        int[] originalIndices = source.Indices.ToArray();
        int[] originalMaterials = source.TriangleMaterialIds.ToArray();

        var plan = RuntimeWorldScene.BuildCollisionFacePlan(source);

        Assert.Equal(3, plan.Faces.Length);
        Assert.Equal([0], plan.SourceTriangleIndices);
        Assert.Equal(originalPositions, source.Positions);
        Assert.Equal(originalIndices, source.Indices);
        Assert.Equal(originalMaterials, source.TriangleMaterialIds);
        Assert.Equal(31, RuntimeWorldScene.MaterialIdForFace(source, plan.SourceTriangleIndices, 0));
        Assert.Null(RuntimeWorldScene.MaterialIdForFace(source, plan.SourceTriangleIndices, 1));
    }

    [Fact]
    public void CollisionShapeKeepsDifferentMaterialsAndDistinctSurfaces()
    {
        var source = new RuntimeCollisionBlob(1,
            [0, 0, 0, 1, 0, 0, 0, 0, 1, 0, .0001, 0],
            [0, 1, 2, 2, 1, 0, 3, 1, 2], [31, 7, 31]);

        var plan = RuntimeWorldScene.BuildCollisionFacePlan(source);

        Assert.Equal(9, plan.Faces.Length);
        Assert.Equal([0, 1, 2], plan.SourceTriangleIndices);
        Assert.Equal(7, RuntimeWorldScene.MaterialIdForFace(source, plan.SourceTriangleIndices, 1));
    }

    [Fact]
    public void CollisionShapeAllowsUnlabelledGeometryWithoutInventingAMaterial()
    {
        var source = new RuntimeCollisionBlob(1, [0, 0, 0, 1, 0, 0, 0, 0, 1], [0, 1, 2, 2, 1, 0], []);
        var plan = RuntimeWorldScene.BuildCollisionFacePlan(source);
        Assert.Equal(3, plan.Faces.Length);
        Assert.Null(RuntimeWorldScene.MaterialIdForFace(source, plan.SourceTriangleIndices, 0));
    }

    [SkippableFact]
    public void VeldinShapeConsolidatesOctreeReferencesAndRetainsEveryEmittedSourceIdentity()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var source = Assert.Single(Rac1WorldImport.Build(reader, 0).CollisionMeshes);
        var plan = RuntimeWorldScene.BuildCollisionFacePlan(source);

        Assert.Equal(86_184, source.Triangles);
        Assert.Equal(31_423, plan.SourceTriangleIndices.Length);
        Assert.Equal(plan.SourceTriangleIndices.Length * 3, plan.Faces.Length);
        for (int face = 0; face < plan.SourceTriangleIndices.Length; face++)
        {
            int triangle = plan.SourceTriangleIndices[face];
            Assert.Equal(source.TriangleMaterialIds[triangle],
                RuntimeWorldScene.MaterialIdForFace(source, plan.SourceTriangleIndices, face));
            for (int vertex = 0; vertex < 3; vertex++)
            {
                int offset = source.Indices[triangle * 3 + vertex] * 3;
                Assert.Equal(RuntimeWorldScene.ToScene(source.Positions[offset], source.Positions[offset + 1],
                    source.Positions[offset + 2]), plan.Faces[face * 3 + vertex]);
            }
        }
    }
}

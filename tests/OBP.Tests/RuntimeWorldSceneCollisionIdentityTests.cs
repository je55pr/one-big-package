using OBP.Godot;
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
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void DoubledGodotFacesMapBackToSourceTriangles(
        int godotFaceIndex,
        int expectedTriangle)
    {
        Assert.Equal(
            expectedTriangle,
            RuntimeWorldScene.SourceTriangleIndexForDoubledFace(
                godotFaceIndex,
                sourceTriangleCount: 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    public void DoubledFaceMappingRejectsInvalidOrPastEndFaces(int godotFaceIndex)
    {
        Assert.Null(
            RuntimeWorldScene.SourceTriangleIndexForDoubledFace(
                godotFaceIndex,
                sourceTriangleCount: 3));
    }

    [Theory]
    [InlineData(0, 9)]
    [InlineData(1, 9)]
    [InlineData(2, 10)]
    [InlineData(3, 10)]
    [InlineData(4, 31)]
    [InlineData(5, 31)]
    public void DoubledFacesRecoverOriginalCollisionMaterialIds(
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
            RuntimeWorldScene.MaterialIdForDoubledFace(
                collision,
                godotFaceIndex));
    }
}

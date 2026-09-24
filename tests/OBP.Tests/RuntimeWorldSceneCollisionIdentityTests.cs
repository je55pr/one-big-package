using OBP.Godot;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class RuntimeWorldSceneCollisionIdentityTests
{
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

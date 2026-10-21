using OBP.Godot;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1NovalisWaterCollisionTests
{
    [Fact]
    public void HostFiltersWaterTaggedFacesWithoutRewritingNativeCollisionEvidence()
    {
        // Two independent triangles: a flat Novalis water-like source material,
        // then a normal solid face. The source collision blob remains verbatim.
        var blob = new RuntimeCollisionBlob(1,
            [0, 56, 0, 1, 56, 0, 0, 56, 1,
             5, 60, 5, 6, 60, 5, 5, 60, 6,
             20, 56, 20, 20, 57, 20, 20, 56, 21],
            [0, 1, 2, 3, 4, 5, 6, 7, 8],
            [0, 4, 0]);
        var unfiltered = RuntimeWorldScene.BuildCollisionFacePlan(blob);
        Assert.Equal(3, unfiltered.SourceTriangleIndices.Length);
        var filtered = RuntimeWorldScene.BuildCollisionFacePlan(blob,
            (raw, a, b, c) =>
            {
                if (Rac1CollisionFaceSemantics.Decode(raw).SurfaceClass != 0)
                    return true;
                var normal = (b - a).Cross(c - a);
                return MathF.Abs(normal.Y) < new global::Godot.Vector2(normal.X, normal.Z).Length();
            });
        Assert.Equal([1, 2], filtered.SourceTriangleIndices);
        Assert.Equal(6, filtered.Faces.Length);
        Assert.Equal([0, 4, 0], blob.TriangleMaterialIds);
        Assert.Equal(3, blob.Triangles);
    }
}

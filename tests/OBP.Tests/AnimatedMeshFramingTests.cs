using OBP.Runtime;
using OBP.Runtime.Camera;

namespace OBP.Tests;

public sealed class AnimatedMeshFramingTests
{
    private static RuntimeAnimatedMesh Mesh(string name, params double[][] frames) =>
        new(name, "moby", -1, [], [], [], frames, 30f);

    [Fact]
    public void ValidFramesAcrossSurfacesProduceBounds()
    {
        var r = AnimatedMeshFraming.MeasureFirstInstance([
            Mesh("ratchet_i0_t1", [0, 0, 0, 1, 2, 3], [-1, 0, 0]),
            Mesh("ratchet_i0_t2", [0, 5, 0]),
        ]);
        Assert.Equal(AnimatedMeshBoundsStatus.Ok, r.Status);
        Assert.Equal(2, r.MeshCount);
        Assert.Equal(-1, r.Bounds!.Value.Min.X);
        Assert.Equal(5, r.Bounds.Value.Max.Y);
        Assert.Equal(3, r.Bounds.Value.Max.Z);
    }

    [Fact]
    public void DegenerateSinglePointIsValid()
    {
        var r = AnimatedMeshFraming.MeasureFirstInstance([Mesh("a_i0_t0", [4, 4, 4])]);
        Assert.True(r.Succeeded);
        Assert.Equal(r.Bounds!.Value.Min, r.Bounds.Value.Max);
    }

    [Fact]
    public void NullOrEmptyCollectionFails()
    {
        Assert.Equal(AnimatedMeshBoundsStatus.NoAnimatedMeshes, AnimatedMeshFraming.MeasureFirstInstance(null).Status);
        Assert.Equal(AnimatedMeshBoundsStatus.NoAnimatedMeshes, AnimatedMeshFraming.MeasureFirstInstance([]).Status);
    }

    [Fact]
    public void NonEmptyCollectionWithOnlyEmptyFramesFailsWithoutInfiniteBounds()
    {
        var r = AnimatedMeshFraming.MeasureFirstInstance([Mesh("a_i0_t0", [], []), Mesh("a_i0_t1")]);
        Assert.Equal(AnimatedMeshBoundsStatus.NoUsableVertices, r.Status);
        Assert.Null(r.Bounds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void IncompleteTripletFails(int length)
    {
        var r = AnimatedMeshFraming.MeasureFirstInstance([Mesh("a_i0_t0", new double[length])]);
        Assert.Equal(AnimatedMeshBoundsStatus.MalformedFrame, r.Status);
        Assert.Null(r.Bounds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteCoordinateFailsInAnyAxis(double bad)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            double[] frame = [0, 0, 0];
            frame[axis] = bad;
            var r = AnimatedMeshFraming.MeasureFirstInstance([Mesh("a_i0_t0", [1, 1, 1], frame)]);
            Assert.Equal(AnimatedMeshBoundsStatus.NonFiniteCoordinate, r.Status);
            Assert.Null(r.Bounds);
        }
    }

    [Fact]
    public void OverlappingInstancePrefixesAreNotGrouped()
    {
        var r = AnimatedMeshFraming.MeasureFirstInstance([
            Mesh("moby1_i1_t0", [0, 0, 0]),
            Mesh("moby1_i10_t0", [100, 100, 100]),
            Mesh("moby1_i1_t7", [1, 1, 1]),
        ]);
        Assert.True(r.Succeeded);
        Assert.Equal("moby1_i1", r.Instance);
        Assert.Equal(2, r.MeshCount);
        Assert.Equal(1, r.Bounds!.Value.Max.X);
    }

    [Fact]
    public void DefectsInOtherInstancesDoNotAffectTheFramedOne()
    {
        var r = AnimatedMeshFraming.MeasureFirstInstance([
            Mesh("a_i0_t0", [0, 0, 0]),
            Mesh("a_i1_t0", [double.NaN, 0, 0]),
        ]);
        Assert.True(r.Succeeded);
    }

    [Theory]
    [InlineData("ratchet_i3_t12", "ratchet_i3")]
    [InlineData("ratchet_i3_t-1", "ratchet_i3")]
    [InlineData("ratchet_i3_t-", "ratchet_i3_t-")]
    [InlineData("ratchet_i3_t--1", "ratchet_i3_t--1")]
    [InlineData("uya-preview-moby5_i2_s1_t0", "uya-preview-moby5_i2_s1")]
    [InlineData("noSuffix", "noSuffix")]
    [InlineData("tail_t", "tail_t")]
    [InlineData("x_tab", "x_tab")]
    public void InstanceKeyStripsOnlyTheSurfaceSuffix(string name, string expected) =>
        Assert.Equal(expected, AnimatedMeshFraming.InstanceKey(name));
}

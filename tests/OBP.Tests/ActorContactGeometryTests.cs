using Godot;
using OBP.Game;

namespace OBP.Tests;

public sealed class ActorContactGeometryTests
{
    private static Aabb Box => new(Vector3.Zero, Vector3.One);

    [Fact]
    public void QueryFractionRetainsInsideTangentAndParallelBoundaryBehavior()
    {
        Assert.True(ActorContactGeometry.SegmentBoxFraction(new(-2, .5f, .5f), new(2, .5f, .5f), Box, out var fraction));
        Assert.Equal(.5f, fraction);
        Assert.True(ActorContactGeometry.SegmentBoxFraction(new(.5f, .5f, .5f), new(2, .5f, .5f), Box, out fraction));
        Assert.Equal(0, fraction);
        Assert.True(ActorContactGeometry.SegmentBoxFraction(new(-2, 1, .5f), new(2, 1, .5f), Box, out fraction));
        Assert.Equal(.5f, fraction);
        Assert.False(ActorContactGeometry.SegmentBoxFraction(new(-2, 2, .5f), new(2, 2, .5f), Box, out _));
    }

    [Fact]
    public void NativePyrocitorParticlesUseTheOriginalInflatedBoxEnvelopeWithoutInventedReach()
    {
        var sourceBox = new Aabb(new(2, 0, 0), Vector3.One);
        Assert.False(ActorContactGeometry.FlamePointIntersects(sourceBox, new(1.6f, .5f, .5f), .3f));
        Assert.True(ActorContactGeometry.FlamePointIntersects(sourceBox, new(1.8f, .5f, .5f), .3f));
        Assert.False(ActorContactGeometry.FlamePointIntersects(sourceBox, new(5, .5f, .5f), .3f));
        Assert.False(ActorContactGeometry.FlamePointIntersects(sourceBox, new(2.5f, .5f, .5f), 0f));
        Assert.False(ActorContactGeometry.FlamePointIntersects(sourceBox, new(2.5f, .5f, .5f), float.NaN));
        Assert.False(ActorContactGeometry.FlamePointIntersects(sourceBox, new(float.NaN, .5f, .5f), .3f));
    }

    [Fact]
    public void WrenchAndQueryKeepTheirDistinctExistingSmallDeltaTolerances()
    {
        var from = new Vector3(-.00000025f, .5f, .5f); var to = new Vector3(.00000025f, .5f, .5f);
        Assert.False(ActorContactGeometry.SegmentBoxFraction(from, to, Box, out _));
        Assert.True(ActorContactGeometry.WrenchSegmentIntersects(Box, from, to));
        Assert.False(ActorContactGeometry.WrenchSegmentIntersects(Box, new(-2, 2, .5f), new(2, 2, .5f)));
        Assert.True(ActorContactGeometry.WrenchSegmentIntersects(Box, new(-2, 1, .5f), new(2, 1, .5f)));
    }
}

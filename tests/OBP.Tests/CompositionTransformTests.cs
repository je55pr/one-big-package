using OBP.Composition;
using OBP.Core.Math;

namespace OBP.Tests;

/// <summary>
/// Engine-independent tests for the world-composition transform + alignment
/// solver + persistence (<c>src/OBP.Composition</c>). No Godot, no retail data.
/// See <c>docs/WORLD_COMPOSITION.md</c>.
/// </summary>
public class CompositionTransformTests
{
    private static void AssertClose(Vec3 expected, Vec3 actual, double tol = 1e-9)
    {
        Assert.True(
            System.Math.Abs(expected.X - actual.X) < tol
            && System.Math.Abs(expected.Y - actual.Y) < tol
            && System.Math.Abs(expected.Z - actual.Z) < tol,
            $"expected {expected}, got {actual}");
    }

    [Fact]
    public void Identity_LeavesPointsUnchanged()
    {
        var p = new Vec3(12.5, -3.0, 40.0);
        AssertClose(p, CompositionTransform.Identity.Apply(p));
        Assert.True(CompositionTransform.Identity.IsIdentity);
    }

    [Fact]
    public void Translation_AddsOffset()
    {
        var t = new CompositionTransform(10, 5, -2);
        AssertClose(new Vec3(11, 6, 1), t.Apply(new Vec3(1, 1, 3)));
    }

    [Fact]
    public void RotationY_90Degrees_MapsAxisConsistently()
    {
        var t = new CompositionTransform(RotationYDegrees: 90);
        // Convention: x' = x cos + z sin ; z' = -x sin + z cos.
        // At 90°: (1,0,0) -> (0,0,-1); (0,0,1) -> (1,0,0).
        AssertClose(new Vec3(0, 0, -1), t.Apply(new Vec3(1, 0, 0)), 1e-12);
        AssertClose(new Vec3(1, 0, 0), t.Apply(new Vec3(0, 0, 1)), 1e-12);
    }

    [Fact]
    public void RotationY_PreservesPlanarLengthAndY()
    {
        var t = new CompositionTransform(RotationYDegrees: 37.5);
        var p = new Vec3(3, 9, -4);
        var q = t.Apply(p);
        Assert.Equal(9, q.Y, 12);
        Assert.Equal(
            System.Math.Sqrt(p.X * p.X + p.Z * p.Z),
            System.Math.Sqrt(q.X * q.X + q.Z * q.Z),
            9);
    }

    [Fact]
    public void Scale_ScalesAboutOrigin()
    {
        var t = new CompositionTransform(Scale: 2.0);
        AssertClose(new Vec3(4, 6, -8), t.Apply(new Vec3(2, 3, -4)));
    }

    [Fact]
    public void NormalizeDegrees_FoldsToHalfOpenRange()
    {
        Assert.Equal(0, CompositionTransform.NormalizeDegrees(360));
        Assert.Equal(90, CompositionTransform.NormalizeDegrees(450));
        Assert.Equal(-90, CompositionTransform.NormalizeDegrees(270));
        Assert.Equal(180, CompositionTransform.NormalizeDegrees(180));
        Assert.Equal(-179, CompositionTransform.NormalizeDegrees(181));
    }
}

using OBP.Composition;
using OBP.Core.Math;

namespace OBP.Tests;

public class PlanarAlignmentTests
{
    private static AnchorPair Pair(Vec3 a, Vec3 b, string? label = null) =>
        new() { WorldAId = "a", WorldBId = "b", LocalA = a, LocalB = b, Label = label };

    /// <summary>Build anchor pairs whose A side is exactly <paramref name="truth"/> applied to the B side.</summary>
    private static List<AnchorPair> SyntheticPairs(CompositionTransform truth, params Vec3[] bPoints)
    {
        var list = new List<AnchorPair>();
        foreach (var b in bPoints)
        {
            list.Add(Pair(truth.Apply(b), b));
        }

        return list;
    }

    [Fact]
    public void NoAnchors_ReturnsIdentity()
    {
        var r = PlanarAlignmentSolver.Solve(new List<AnchorPair>());
        Assert.Equal(AlignmentQuality.Empty, r.Quality);
        Assert.True(r.Transform.IsIdentity);
    }

    [Fact]
    public void OneAnchor_SolvesTranslationOnly()
    {
        var r = PlanarAlignmentSolver.Solve(new[] { Pair(new Vec3(10, 5, 20), new Vec3(1, 1, 2)) });
        Assert.Equal(AlignmentQuality.TranslationOnly, r.Quality);
        Assert.Equal(9, r.Transform.TranslationX, 9);
        Assert.Equal(4, r.Transform.TranslationY, 9);
        Assert.Equal(18, r.Transform.TranslationZ, 9);
        Assert.Equal(0, r.Transform.RotationYDegrees, 9);
        Assert.Equal(1.0, r.Transform.Scale, 9);
        Assert.True(r.MaxError < 1e-9);
    }

    [Fact]
    public void TwoAnchors_RecoverPureTranslation()
    {
        var truth = new CompositionTransform(123.4, -7.5, -88.2);
        var pairs = SyntheticPairs(truth, new Vec3(0, 0, 0), new Vec3(10, 3, -4));
        var r = PlanarAlignmentSolver.Solve(pairs);

        Assert.Equal(AlignmentQuality.Solved, r.Quality);
        Assert.Equal(123.4, r.Transform.TranslationX, 6);
        Assert.Equal(-7.5, r.Transform.TranslationY, 6);
        Assert.Equal(-88.2, r.Transform.TranslationZ, 6);
        Assert.Equal(0, r.Transform.RotationYDegrees, 6);
        Assert.True(r.MeanError < 1e-6);
    }

    [Fact]
    public void ThreeAnchors_RecoverTranslationAndRotation()
    {
        var truth = new CompositionTransform(50, 2, -30, RotationYDegrees: 42.1);
        var pairs = SyntheticPairs(truth,
            new Vec3(0, 0, 0), new Vec3(20, 1, 5), new Vec3(-8, -2, 17));
        var r = PlanarAlignmentSolver.Solve(pairs);

        Assert.Equal(AlignmentQuality.Solved, r.Quality);
        Assert.Equal(50, r.Transform.TranslationX, 5);
        Assert.Equal(-30, r.Transform.TranslationZ, 5);
        Assert.Equal(2, r.Transform.TranslationY, 5);
        Assert.Equal(42.1, r.Transform.RotationYDegrees, 5);
        Assert.False(r.ScaleWasFitted);
        Assert.Equal(1.0, r.Transform.Scale, 9);
        Assert.True(r.MaxError < 1e-5, $"max error {r.MaxError}");
    }

    [Fact]
    public void RigidSolve_OnScaledData_LeavesResidualAndUnitScale()
    {
        // B points scaled 1.5x + rotated + moved. Rigid solve must NOT absorb the
        // scale — it should report a real residual (archaeological signal).
        var truth = new CompositionTransform(10, 0, 10, RotationYDegrees: 15, Scale: 1.5);
        var pairs = SyntheticPairs(truth,
            new Vec3(0, 0, 0), new Vec3(30, 0, 0), new Vec3(0, 0, 30), new Vec3(12, 0, -20));

        var rigid = PlanarAlignmentSolver.Solve(pairs, allowScale: false);
        Assert.Equal(1.0, rigid.Transform.Scale, 9);
        Assert.True(rigid.MeanError > 1.0, $"expected a real residual, got {rigid.MeanError}");
    }

    [Fact]
    public void ScaledSolve_RecoversUniformScale()
    {
        var truth = new CompositionTransform(10, 3, 10, RotationYDegrees: 15, Scale: 1.5);
        var pairs = SyntheticPairs(truth,
            new Vec3(0, 0, 0), new Vec3(30, 1, 0), new Vec3(0, -1, 30), new Vec3(12, 0, -20));

        var r = PlanarAlignmentSolver.Solve(pairs, allowScale: true);
        Assert.True(r.ScaleWasFitted);
        Assert.Equal(1.5, r.Transform.Scale, 5);
        Assert.Equal(1.5, r.FittedScale, 5);
        Assert.Equal(15, r.Transform.RotationYDegrees, 5);
        Assert.Equal(10, r.Transform.TranslationX, 4);
        Assert.Equal(3, r.Transform.TranslationY, 4);
        Assert.True(r.MaxError < 1e-4);
    }

    [Fact]
    public void CoincidentAnchors_ReportedDegenerate()
    {
        var pairs = new[]
        {
            Pair(new Vec3(5, 0, 5), new Vec3(1, 0, 1)),
            Pair(new Vec3(5, 0, 5.0000001), new Vec3(1, 0, 1)),
        };
        var r = PlanarAlignmentSolver.Solve(pairs);
        Assert.Equal(AlignmentQuality.Degenerate, r.Quality);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public void CollinearAnchors_FlaggedButStillSolved()
    {
        var truth = new CompositionTransform(4, 0, 9, RotationYDegrees: 20);
        // all B points on the X axis
        var pairs = SyntheticPairs(truth,
            new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(25, 0, 0));
        var r = PlanarAlignmentSolver.Solve(pairs);
        Assert.Equal(AlignmentQuality.Degenerate, r.Quality);
        Assert.Contains("collinear", r.Note!);
    }

    [Fact]
    public void PerAnchorResiduals_AreReportedPerPair()
    {
        var truth = new CompositionTransform(1, 0, 1, RotationYDegrees: 10);
        var pairs = SyntheticPairs(truth,
            new Vec3(0, 0, 0), new Vec3(10, 0, 2), new Vec3(3, 0, 14)).ToList();
        // nudge one B point so its pair no longer fits
        pairs[1] = pairs[1] with { LocalB = pairs[1].LocalB + new Vec3(0, 0, 5) };

        var r = PlanarAlignmentSolver.Solve(pairs);
        Assert.Equal(3, r.PerAnchorResiduals.Count);
        Assert.True(r.PerAnchorResiduals[1] > r.PerAnchorResiduals[0]);
        Assert.True(r.PerAnchorResiduals[1] > r.PerAnchorResiduals[2]);
        Assert.Equal(r.PerAnchorResiduals.Max(), r.MaxError, 12);
    }

    [Fact]
    public void DisabledAnchors_AreExcluded()
    {
        var truth = new CompositionTransform(2, 0, 3, RotationYDegrees: 5);
        var pairs = SyntheticPairs(truth, new Vec3(0, 0, 0), new Vec3(9, 0, 1), new Vec3(-4, 0, 12)).ToList();
        pairs.Add(new AnchorPair
        {
            WorldAId = "a", WorldBId = "b",
            LocalA = new Vec3(999, 999, 999), LocalB = new Vec3(0, 0, 0),
            Enabled = false,
        });

        var r = PlanarAlignmentSolver.Solve(pairs);
        Assert.Equal(3, r.AnchorCount);
        Assert.True(r.MaxError < 1e-6);
    }

    [Fact]
    public void SolveAlignment_UpdatesOnlyWorldB()
    {
        var truth = new CompositionTransform(100, 0, -50, RotationYDegrees: 30);
        var comp = new WorldComposition
        {
            Worlds = new[]
            {
                new WorldPlacement { Id = "a" },
                new WorldPlacement { Id = "b" },
            },
            Anchors = SyntheticPairs(truth, new Vec3(0, 0, 0), new Vec3(20, 0, 5), new Vec3(-9, 0, 12))
                .Select(p => p with { WorldAId = "a", WorldBId = "b" }).ToList(),
        };

        var (updated, result) = comp.SolveAlignment("a", "b");
        Assert.True(updated.World("a")!.Transform.IsIdentity);
        Assert.Equal(30, updated.World("b")!.Transform.RotationYDegrees, 4);
        Assert.Equal(100, updated.World("b")!.Transform.TranslationX, 3);
        Assert.True(result.MaxError < 1e-4);
    }
}

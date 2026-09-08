using OBP.Core.Math;

namespace OBP.Composition;

/// <summary>Outcome of a <see cref="PlanarAlignmentSolver"/> run.</summary>
public sealed record PlanarAlignmentResult(
    CompositionTransform Transform,
    int AnchorCount,
    double MeanError,
    double MaxError,
    double RmsError,
    IReadOnlyList<double> PerAnchorResiduals,
    bool ScaleWasFitted,
    double FittedScale,
    AlignmentQuality Quality,
    string? Note)
{
    /// <summary>Solved Y rotation, folded to (−180, 180].</summary>
    public double RotationYDegrees => Transform.RotationYDegrees;

    public (double X, double Y, double Z) Translation =>
        (Transform.TranslationX, Transform.TranslationY, Transform.TranslationZ);
}

public enum AlignmentQuality
{
    /// <summary>No anchors — transform is identity.</summary>
    Empty,

    /// <summary>One anchor — translation only, rotation/scale undetermined.</summary>
    TranslationOnly,

    /// <summary>Anchor points too close together / collinear to trust rotation or scale.</summary>
    Degenerate,

    /// <summary>A full planar transform was solved.</summary>
    Solved,
}

/// <summary>
/// Closed-form least-squares rigid (optionally uniform-scale) planar alignment:
/// given anchor pairs <c>(a_i in world A local, b_i in world B local)</c>, find the
/// <see cref="CompositionTransform"/> (X/Z translation, Y translation, Y rotation,
/// optional uniform scale) that minimises <c>Σ |T(b_i) − a_i|²</c>.
///
/// <para>
/// The planar fit is the 2-D Procrustes / Umeyama solution on the XZ plane; the
/// Y component is a pure mean offset (this is a <em>planar</em> alignment — it
/// answers "are these two layouts the same map, moved and turned?", not "how do
/// their height fields relate"). Rigid (no-scale) is the default; uniform scale
/// is opt-in and reported separately because a non-unit result is evidence the
/// games re-proportioned the space rather than reusing it.
/// </para>
/// </summary>
public static class PlanarAlignmentSolver
{
    private const double CoincidenceEpsilon = 1e-6;

    public static PlanarAlignmentResult Solve(IReadOnlyList<AnchorPair> pairs, bool allowScale = false)
    {
        var ab = new List<(Vec3 A, Vec3 B)>(pairs.Count);
        foreach (var p in pairs)
        {
            if (p.Enabled)
            {
                ab.Add((p.LocalA, p.LocalB));
            }
        }

        return Solve(ab, allowScale);
    }

    public static PlanarAlignmentResult Solve(IReadOnlyList<(Vec3 A, Vec3 B)> pairs, bool allowScale = false)
    {
        int n = pairs.Count;
        if (n == 0)
        {
            return new PlanarAlignmentResult(
                CompositionTransform.Identity, 0, 0, 0, 0, System.Array.Empty<double>(),
                false, 1.0, AlignmentQuality.Empty, "no anchor pairs");
        }

        if (n == 1)
        {
            var d = pairs[0].A - pairs[0].B;
            var t = new CompositionTransform(d.X, d.Y, d.Z, 0, 1.0);
            return Finish(t, pairs, false, 1.0, AlignmentQuality.TranslationOnly,
                "one anchor pair — translation only; rotation and scale undetermined");
        }

        // Centroids.
        Vec3 ca = Vec3.Zero, cb = Vec3.Zero;
        foreach (var (a, b) in pairs)
        {
            ca += a;
            cb += b;
        }

        ca *= 1.0 / n;
        cb *= 1.0 / n;

        // Planar (XZ) cross-covariance of centred points.
        //   sDot   = Σ (b'x·a'x + b'z·a'z)
        //   sCross = Σ (b'x·a'z − b'z·a'x)
        // and the source spread Σ|b'|² for the scale estimate.
        double sDot = 0, sCross = 0, srcSpread = 0, tgtSpread = 0;
        foreach (var (a, b) in pairs)
        {
            double ax = a.X - ca.X, az = a.Z - ca.Z;
            double bx = b.X - cb.X, bz = b.Z - cb.Z;
            sDot += bx * ax + bz * az;
            sCross += bx * az - bz * ax;
            srcSpread += bx * bx + bz * bz;
            tgtSpread += ax * ax + az * az;
        }

        if (srcSpread < CoincidenceEpsilon || tgtSpread < CoincidenceEpsilon)
        {
            var d = ca - cb;
            var t = new CompositionTransform(d.X, d.Y, d.Z, 0, 1.0);
            return Finish(t, pairs, false, 1.0, AlignmentQuality.Degenerate,
                "anchor points are coincident on the XZ plane — solved translation only");
        }

        // Optimal Y rotation. atan2(sCross, sDot) is the standard 2-D Procrustes
        // angle φ for R_std(φ); CompositionTransform's rotation R_y(θ) equals
        // R_std(−θ), so θ = −φ.
        double phi = System.Math.Atan2(sCross, sDot);
        double rotationDegrees = CompositionTransform.NormalizeDegrees(-phi * 180.0 / System.Math.PI);

        double scale = 1.0;
        if (allowScale)
        {
            // s = |covariance| / source spread  (Umeyama, no reflection possible in 2-D rotation).
            scale = System.Math.Sqrt(sDot * sDot + sCross * sCross) / srcSpread;
        }

        // Translation: carry the scaled, rotated source centroid onto the target
        // centroid. Y is a plain mean offset of the (optionally scaled) heights.
        var (rcx, rcz) = CompositionTransform.RotateY(cb.X * scale, cb.Z * scale, rotationDegrees);
        double tx = ca.X - rcx;
        double tz = ca.Z - rcz;

        double ty = 0;
        foreach (var (a, b) in pairs)
        {
            ty += a.Y - b.Y * scale;
        }

        ty /= n;

        var transform = new CompositionTransform(tx, ty, tz, rotationDegrees, scale);

        // Flag collinear / near-degenerate arrangements: the smaller eigenvalue of
        // the planar scatter matrix being tiny relative to the larger one means
        // rotation (and especially scale) rest on almost no perpendicular spread.
        string? note = null;
        var quality = AlignmentQuality.Solved;
        // Two distinct points give an exactly-determined planar fit; the
        // collinearity guard only matters for an over-determined (3+) set where a
        // near-linear arrangement leaves the perpendicular unconstrained.
        double collinearity = PlanarCollinearity(pairs, cb);
        if (n >= 3 && collinearity < 1e-3)
        {
            quality = AlignmentQuality.Degenerate;
            note = "anchor points are nearly collinear — Y rotation is weakly constrained"
                + (allowScale ? " and the scale estimate is unreliable" : string.Empty);
        }

        return Finish(transform, pairs, allowScale, scale, quality, note);
    }

    /// <summary>Ratio (0..1) of the minor to major spread of the source points on XZ; ~0 = collinear, ~1 = isotropic.</summary>
    private static double PlanarCollinearity(IReadOnlyList<(Vec3 A, Vec3 B)> pairs, Vec3 cb)
    {
        double sxx = 0, szz = 0, sxz = 0;
        foreach (var (_, b) in pairs)
        {
            double bx = b.X - cb.X, bz = b.Z - cb.Z;
            sxx += bx * bx;
            szz += bz * bz;
            sxz += bx * bz;
        }

        double trace = sxx + szz;
        if (trace < CoincidenceEpsilon)
        {
            return 0;
        }

        double diff = System.Math.Sqrt(System.Math.Max(0, (sxx - szz) * (sxx - szz) + 4 * sxz * sxz));
        double major = (trace + diff) * 0.5;
        double minor = (trace - diff) * 0.5;
        return major <= 0 ? 0 : System.Math.Max(0, minor / major);
    }

    private static PlanarAlignmentResult Finish(
        CompositionTransform transform,
        IReadOnlyList<(Vec3 A, Vec3 B)> pairs,
        bool scaleFitted,
        double fittedScale,
        AlignmentQuality quality,
        string? note)
    {
        var residuals = new double[pairs.Count];
        double sum = 0, sumSq = 0, max = 0;
        for (int i = 0; i < pairs.Count; i++)
        {
            var predicted = transform.Apply(pairs[i].B);
            var e = predicted - pairs[i].A;
            double dist = System.Math.Sqrt(e.X * e.X + e.Y * e.Y + e.Z * e.Z);
            residuals[i] = dist;
            sum += dist;
            sumSq += dist * dist;
            if (dist > max)
            {
                max = dist;
            }
        }

        double mean = pairs.Count > 0 ? sum / pairs.Count : 0;
        double rms = pairs.Count > 0 ? System.Math.Sqrt(sumSq / pairs.Count) : 0;
        return new PlanarAlignmentResult(
            transform.Rounded(), pairs.Count, mean, max, rms, residuals,
            scaleFitted, fittedScale, quality, note);
    }
}

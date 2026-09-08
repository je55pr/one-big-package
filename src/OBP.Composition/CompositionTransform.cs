using OBP.Core.Math;

namespace OBP.Composition;

/// <summary>
/// A placement transform that sits <em>above</em> a reconstructed
/// <c>RuntimeWorld</c> in the composition tree. It never touches the decoded
/// world data — it maps a world-local point (OBP space, Y-up) into shared
/// composition space so two or more independently reconstructed worlds can be
/// examined together.
///
/// <para>
/// Applied in the order <b>scale → Y-rotation → translation</b>:
/// <c>p_composition = translation + R_y(rotationYDegrees) · (scale · p_local)</c>.
/// </para>
///
/// <para>
/// <b>Scale defaults to 1 and is archaeological evidence, not a fitting knob</b> —
/// a non-unit scale means the two source worlds genuinely differ in proportion,
/// which is exactly the kind of finding this lab exists to surface. Rigid
/// (scale-locked) alignment is the default comparison.
/// </para>
/// </summary>
public sealed record CompositionTransform(
    double TranslationX = 0,
    double TranslationY = 0,
    double TranslationZ = 0,
    double RotationYDegrees = 0,
    double Scale = 1.0)
{
    public static readonly CompositionTransform Identity = new();

    public bool IsIdentity =>
        TranslationX == 0 && TranslationY == 0 && TranslationZ == 0
        && RotationYDegrees == 0 && Scale == 1.0;

    /// <summary>Map a world-local point (OBP Y-up) into composition space.</summary>
    public Vec3 Apply(Vec3 local)
    {
        double s = Scale;
        double x = local.X * s;
        double y = local.Y * s;
        double z = local.Z * s;

        (double rx, double rz) = RotateY(x, z, RotationYDegrees);
        return new Vec3(rx + TranslationX, y + TranslationY, rz + TranslationZ);
    }

    /// <summary>
    /// Rotate the planar component <c>(x, z)</c> about +Y by
    /// <paramref name="degrees"/>. This is the single definition of the rotation
    /// convention shared by <see cref="Apply"/> and the alignment solver:
    /// <c>x' = x·cosθ + z·sinθ ; z' = −x·sinθ + z·cosθ</c>.
    /// </summary>
    public static (double X, double Z) RotateY(double x, double z, double degrees)
    {
        double t = degrees * System.Math.PI / 180.0;
        double c = System.Math.Cos(t);
        double sn = System.Math.Sin(t);
        return (x * c + z * sn, -x * sn + z * c);
    }

    /// <summary>Compose two transforms: <c>this ∘ inner</c> (inner applied first).</summary>
    public CompositionTransform Then(CompositionTransform outer)
    {
        // outer.Apply(this.Apply(p)). Only needed for nested roots; kept simple.
        var origin = outer.Apply(Apply(Vec3.Zero));
        var unitX = outer.Apply(Apply(new Vec3(1, 0, 0))) - origin;
        var unitZ = outer.Apply(Apply(new Vec3(0, 0, 1))) - origin;
        double scale = System.Math.Sqrt(unitX.X * unitX.X + unitX.Z * unitX.Z);
        double deg = System.Math.Atan2(-unitX.Z, unitX.X) * 180.0 / System.Math.PI;
        _ = unitZ;
        return new CompositionTransform(origin.X, origin.Y, origin.Z, deg, scale);
    }

    /// <summary>Round every field to <paramref name="decimals"/> places — used to keep serialization deterministic.</summary>
    public CompositionTransform Rounded(int decimals = 6) => new(
        System.Math.Round(TranslationX, decimals),
        System.Math.Round(TranslationY, decimals),
        System.Math.Round(TranslationZ, decimals),
        System.Math.Round(NormalizeDegrees(RotationYDegrees), decimals),
        System.Math.Round(Scale, decimals));

    /// <summary>Fold a rotation into (−180, 180].</summary>
    public static double NormalizeDegrees(double d)
    {
        d %= 360.0;
        if (d > 180.0) d -= 360.0;
        if (d <= -180.0) d += 360.0;
        return d;
    }
}

namespace OBP.Runtime.Presentation;

/// <summary>One <see cref="RuntimeAmbientAnimation"/> evaluated at a point in time.</summary>
/// <param name="U">UV offset to add on the target material's X axis (UvScroll).</param>
/// <param name="V">UV offset to add on the target material's Y axis (UvScroll).</param>
/// <param name="Radians">Rotation about the animation's <see cref="RuntimeAmbientAnimation.Rate"/> axis (Spin).</param>
public readonly record struct AmbientAnimationSample(double U, double V, double Radians)
{
    public static readonly AmbientAnimationSample Neutral = new(0, 0, 0);
}

/// <summary>
/// Pure evaluator for <see cref="RuntimeAmbientAnimation"/>. Deterministic — the
/// output depends only on the animation and the elapsed time — so a capture at
/// frame N is reproducible and the logic is unit-testable without Godot. The
/// Godot host turns an <see cref="AmbientAnimationSample"/> into a material UV
/// offset or a node rotation.
/// </summary>
public static class AmbientAnimator
{
    public static AmbientAnimationSample Sample(RuntimeAmbientAnimation anim, double timeSeconds)
    {
        switch (anim.Kind)
        {
            case RuntimeAmbientAnimationKind.UvScroll:
                return new AmbientAnimationSample(
                    Frac((anim.Rate.X * timeSeconds) + anim.Phase),
                    Frac((anim.Rate.Y * timeSeconds) + anim.Phase),
                    0);

            case RuntimeAmbientAnimationKind.Spin:
                double speed = System.Math.Sqrt(
                    (anim.Rate.X * anim.Rate.X) + (anim.Rate.Y * anim.Rate.Y) + (anim.Rate.Z * anim.Rate.Z));
                return new AmbientAnimationSample(0, 0, WrapRadians((speed * timeSeconds) + anim.Phase));

            default:
                return AmbientAnimationSample.Neutral;
        }
    }

    /// <summary>Fractional part in [0, 1) — keeps the offset bounded over a long session.</summary>
    private static double Frac(double x) => x - System.Math.Floor(x);

    private static double WrapRadians(double r)
    {
        const double twoPi = 2 * System.Math.PI;
        r %= twoPi;
        return r < 0 ? r + twoPi : r;
    }
}

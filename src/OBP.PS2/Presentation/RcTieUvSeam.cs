using OBP.Runtime.Presentation;

namespace OBP.PS2.Presentation;

/// <summary>
/// Restores interpolation across the packed sixteen-tile UV boundary used by
/// PS2 TIE classes. The source stores S/T in unsigned 16-bit fixed point
/// (4096 per tile): a triangle spanning 15.x -> 0.x must interpolate across
/// 16, not through fifteen repetitions of the texture. This is a per-face
/// presentation transform; native class vertices and triangle topology stay
/// untouched, and only repeat/unknown sampler modes are eligible.
/// </summary>
public static class RcTieUvSeam
{
    public static bool CrossesPackedBoundary(
        float a, float b, float c, RuntimeTextureWrap? wrap)
    {
        if (wrap == RuntimeTextureWrap.Clamp)
            return false;

        static bool NearBoundary(float value) =>
            value is >= 0f and <= 1f or >= 15f and < 16f;

        return NearBoundary(a) && NearBoundary(b) && NearBoundary(c)
            && (a <= 1f || b <= 1f || c <= 1f)
            && (a >= 15f || b >= 15f || c >= 15f);
    }

    public static double Unwrap(double value, bool crossesBoundary) =>
        crossesBoundary && value <= 1d ? value + 16d : value;
}

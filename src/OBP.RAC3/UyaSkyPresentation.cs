using OBP.RAC3.Geometry;
using OBP.Runtime;

namespace OBP.RAC3;

/// <summary>
/// Source-specific adapter for the retail-backed UYA sky-shell motion fields.
/// Raw velocity is native evidence; the 60 Hz angle conversion follows the
/// documented public Wrench interpretation for the NTSC-U authority.
/// </summary>
public static class UyaSkyPresentation
{
    public const double PublicConversionFrameRate = 60.0;

    public static string GroupName(int shellIndex) => $"uya-sky-shell-{shellIndex}";

    public static RuntimeAmbientAnimation? SpinFor(UyaSky.Shell shell, int shellIndex)
    {
        var rate = AngularVelocityObp(shell.AngularVelocityRaw);
        if (rate == (0d, 0d, 0d)) return null;

        return new RuntimeAmbientAnimation(
            "sky", null, RuntimeAmbientAnimationKind.Spin, rate,
            TargetGroup: GroupName(shellIndex));
    }

    public static (double X, double Y, double Z) AngularVelocityObp(IReadOnlyList<short> raw)
    {
        if (raw.Count != 3) throw new ArgumentException("UYA sky angular velocity must have three components.", nameof(raw));
        double scale = PublicConversionFrameRate * 2.0 * System.Math.PI / 32768.0;
        // Native Z-up -> OBP Y-up swaps Y/Z, an odd basis change. Angular
        // velocity is an axial vector, so it also acquires the determinant sign.
        return (-raw[0] * scale, -raw[2] * scale, -raw[1] * scale);
    }
}

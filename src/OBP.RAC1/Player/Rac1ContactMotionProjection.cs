namespace OBP.RAC1.Player;

/// <summary>
/// Ordinary mode-0 P+0x110 construction at Veldin 0x213ae0..0x213b5c.
/// Vectors use native axis order (Z up), in the controller's planar basis.
/// Requested motion and displacement must describe the same primary contact
/// pass, before separately applied support/conveyor/external transport.
/// </summary>
public static class Rac1ContactMotionProjection
{
    public const double CrouchEntryMaximumLength = 7d / 60d;

    public static Rac1NativeVector3 Resolve(
        Rac1NativeVector3 request, Rac1NativeVector3 contactDisplacement)
    {
        Validate(request);
        Validate(contactDisplacement);
        double requestLength = request.Length;
        double displacementLength = contactDisplacement.Length;
        if (requestLength == 0 || displacementLength == 0)
            return Rac1NativeVector3.Zero;
        double projectedLength = Math.Max(0,
            (contactDisplacement.X / displacementLength) * request.X +
            (contactDisplacement.Y / displacementLength) * request.Y +
            (contactDisplacement.Z / displacementLength) * request.Z);
        // The delay slot at 0x213b58 passes the original request to the
        // set-length helper. The result follows request, not actual movement.
        return Scale(request, projectedLength / requestLength);
    }

    /// <summary>Action-4 entry copies P+0x110 and caps its full XYZ length.</summary>
    public static Rac1NativeVector3 EnterCrouch(Rac1NativeVector3 contactMotion)
    {
        Validate(contactMotion);
        double length = contactMotion.Length;
        return length > CrouchEntryMaximumLength
            ? Scale(contactMotion, CrouchEntryMaximumLength / length)
            : contactMotion;
    }

    private static Rac1NativeVector3 Scale(Rac1NativeVector3 vector, double scale) =>
        new(vector.X * scale, vector.Y * scale, vector.Z * scale);

    private static void Validate(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) || !double.IsFinite(vector.Z) ||
            !double.IsFinite(vector.Length))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}

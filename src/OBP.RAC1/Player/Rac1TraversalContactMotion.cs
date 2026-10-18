namespace OBP.RAC1.Player;

public readonly record struct Rac1TraversalContactFacts(
    int UnsupportedTicks,
    double SupportCorrectionMetric,
    double PreviousDisplacementVertical,
    bool HasRadialContact,
    Rac1NativeVector3 RadialContactNormal);

/// <summary>
/// The ascending steep-contact correction at LEVEL0 0x2121c0..0x212300.
/// It consumes the prior radial contact, distinct from the support normal and
/// from a host's undifferentiated collection of slide contacts.
/// </summary>
public static class Rac1TraversalContactMotion
{
    public const double NearSupportMetric = 0.15f;
    public const double MinimumSteepContactAngle = 0.8726646304130554f;
    public const double AdditionalDownwardRequest = 54f * (1f / 3600f) * 2f;

    public static Rac1NativeVector3 ResolveAscendingSteepContact(
        Rac1NativeVector3 request,
        in Rac1TraversalContactFacts facts)
    {
        var normal = facts.RadialContactNormal;
        if (facts.UnsupportedTicks == 0 ||
            !(facts.SupportCorrectionMetric < NearSupportMetric) ||
            !(facts.PreviousDisplacementVertical > 0d) ||
            !facts.HasRadialContact || !(normal.Z > 0d))
            return request;

        double horizontalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
        double angle = Math.Atan2(horizontalLength, normal.Z);
        if (angle < MinimumSteepContactAngle || horizontalLength <= 1e-12d)
            return request;

        double x = normal.X / horizontalLength;
        double y = normal.Y / horizontalLength;
        double opposed = -(request.X * x + request.Y * y);
        if (opposed <= 0d)
            return request;

        return new(
            request.X + opposed * x,
            request.Y + opposed * y,
            request.Z - AdditionalDownwardRequest);
    }
}

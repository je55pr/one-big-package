using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Recovered ordinary R&C1 terrain/contact request construction.
/// </summary>
public static class Rac1OrdinaryGroundContactMotion
{
    public const double NativeTickScalar = 1d / 3600d;
    public const double GroundDownwardRequestPerTick = 54d * NativeTickScalar;
    public const double EdgeFallAccelerationPerTick = 25d * NativeTickScalar;
    // P+0x2dc is the signed player/contact correction metric. Retail takes its
    // absolute value and requires it to be below this exact f32 threshold before
    // ordinary support can clear the short contact counter.
    public const double OrdinarySupportContactMetricLimit = 0.019999999552965164d;
    // Loaded ordinary-support gate at 0x00212ba0..0x00212bb8.
    // Keep the exact retail f32 value rather than rounding the runtime law.
    public const double OrdinarySupportMaxAngleRadians = 0.8726646304130554d;
    // Host collision-transition envelope, not a claimed native step-height field.
    // It is bounded entirely by recovered ordinary contact terms: one 54/3600
    // ground request plus the exact 0.02 contact-correction admission metric.
    // The retained Veldin flat->slope transition penetrates the destination plane
    // by 0.0275313622u, which lies inside this 0.035u envelope.
    public const double OrdinarySupportTransitionHostEnvelope =
        GroundDownwardRequestPerTick + OrdinarySupportContactMetricLimit;

    public readonly record struct PreContactStep(
        double PlanarX,
        double PlanarY,
        double Vertical);

    public static double ResolveHostTransitionVertical(
        double requestedVertical,
        double prospectiveSupportRise,
        double prospectiveSupportAngleRadians)
    {
        if (!double.IsFinite(requestedVertical) ||
            !double.IsFinite(prospectiveSupportRise) ||
            !double.IsFinite(prospectiveSupportAngleRadians))
            throw new ArgumentOutOfRangeException(nameof(prospectiveSupportRise));

        if (prospectiveSupportRise <= 0d ||
            prospectiveSupportRise > OrdinarySupportTransitionHostEnvelope ||
            prospectiveSupportAngleRadians < 0d ||
            prospectiveSupportAngleRadians > OrdinarySupportMaxAngleRadians)
            return requestedVertical;

        return Math.Max(requestedVertical, prospectiveSupportRise);
    }

    public static bool AdmitsOrdinarySupport(
        double contactCorrectionMetric,
        double contactAngleRadians)
    {
        if (!double.IsFinite(contactCorrectionMetric) ||
            !double.IsFinite(contactAngleRadians) ||
            contactAngleRadians < 0d)
            return false;

        return Math.Abs(contactCorrectionMetric) < OrdinarySupportContactMetricLimit &&
            contactAngleRadians <= OrdinarySupportMaxAngleRadians;
    }

    /// <summary>
    /// Build the ordinary grounded request against one admitted support plane.
    /// The normal components use the same basis as the already-mapped controller
    /// result: planar X/Y plus Up. Retail preserves horizontal yaw, lifts that
    /// heading by the plane's directional slope, and normalizes the resulting
    /// 3D tangent back to the controller's planar-step magnitude.
    /// </summary>
    public static PreContactStep ResolvePreContactStep(
        Rac1RatchetMovementController.StepResult movement,
        PlayerContactFacts priorContact,
        double normalPlanarX,
        double normalPlanarY,
        double normalUp)
    {
        double planarX = movement.PlanarX;
        double planarY = movement.PlanarY;
        double vertical = movement.Vertical;

        if (priorContact.IsGrounded &&
            movement.Phase == Rac1RatchetMovementPhase.Grounded)
        {
            double planarMagnitude = Math.Sqrt(
                (planarX * planarX) + (planarY * planarY));
            if (planarMagnitude > 1e-12d &&
                double.IsFinite(normalPlanarX) &&
                double.IsFinite(normalPlanarY) &&
                double.IsFinite(normalUp) &&
                normalUp > 1e-9d)
            {
                double headingX = planarX / planarMagnitude;
                double headingY = planarY / planarMagnitude;
                double risePerPlanarUnit =
                    -((normalPlanarX * headingX) +
                      (normalPlanarY * headingY)) /
                    normalUp;
                if (double.IsFinite(risePerPlanarUnit))
                {
                    double tangentScale =
                        planarMagnitude /
                        Math.Sqrt(1d + (risePerPlanarUnit * risePerPlanarUnit));
                    planarX = headingX * tangentScale;
                    planarY = headingY * tangentScale;
                    vertical += risePerPlanarUnit * tangentScale;
                }
            }

            // The contact query has already admitted this support. Retail then
            // adds the world-down 54/3600 request; if support disappears, this
            // becomes the first real edge displacement.
            vertical -= GroundDownwardRequestPerTick;
        }

        return new PreContactStep(planarX, planarY, vertical);
    }

    public static double ResolvePreContactVertical(
        Rac1RatchetMovementController.StepResult movement,
        PlayerContactFacts priorContact)
    {
        // Compatibility seam for callers without a recovered support normal.
        // A world-up normal preserves the existing flat-ground law.
        return ResolvePreContactStep(
            movement,
            priorContact,
            normalPlanarX: 0d,
            normalPlanarY: 0d,
            normalUp: 1d).Vertical;
    }
}

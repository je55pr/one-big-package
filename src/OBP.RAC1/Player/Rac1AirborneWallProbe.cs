namespace OBP.RAC1.Player;

public readonly record struct Rac1AirborneWallProbeInput(
    Rac1NativeVector3 Position, Rac1NativeVector3 ContactMotion,
    double RequestedVertical, double FacingYaw, double GroundClearance,
    double GravityPerUpdate, int CurrentAction,
    int CurrentDomain, int PreviousDomain, int SecondPreviousDomain,
    bool ForwardContactFlag, bool ForwardCrateFamily,
    bool ExistingLedgeCandidate, bool HasWallJumped,
    double PreviousWallNormalYaw, double BodyRadius = 0.45f);

public enum Rac1AirborneWallProbeOutcome
{
    Unchanged,
    ClearedForLedge,
    Refreshed,
}

public readonly record struct Rac1AirborneWallProbeResult(
    Rac1AirborneWallProbeOutcome Outcome,
    Rac1NativeVector3 Normal, double NormalYaw,
    int CandidateLeaseUpdates, int LedgeCooldownUpdates);

/// <summary>
/// Regular-axis wall-jump candidate producer at 0x20b5b8..0x20b914.
/// The collision and ledge-continuation callbacks supply native query facts;
/// an unsuccessful probe normally leaves the previous candidate lease intact.
/// No assumed authored wall-jump marker or arbitrary-wall rule is introduced.
/// </summary>
public static class Rac1AirborneWallProbe
{
    public const double MinimumGroundClearance = 0.4f;
    public const double MinimumRequestedVertical = -20f * (1f / 60f);
    public const double MinimumWallAngle = 1.3089969158172607f;
    public const double MaximumFacingError = 0.8726646304130554f;
    public const double MinimumRepeatedNormalDifference = 2.96705961227417f;
    public const double OrdinaryMaximumHitDistance = 0.72f;
    public const double RepeatedMaximumHitDistance = 0.95f;

    public readonly record struct AdmissionInput(
        int CurrentAction, int NativeActionAge, int CandidateLeaseUpdates,
        bool LandingStarted, bool CrossPressBufferedSevenUpdates,
        int BufferedCrossPressAge);

    /// <summary>Action selector at 0x22f414..0x22f4c8, after candidate production.</summary>
    public static bool CanEnter(AdmissionInput input)
    {
        if (input.NativeActionAge < 0 || input.CandidateLeaseUpdates < 0 || input.BufferedCrossPressAge < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        return input.CurrentAction is 7 or 9 or 0x11 &&
            input.CandidateLeaseUpdates != 0 && !input.LandingStarted &&
            (input.CurrentAction != 0x11 || input.NativeActionAge > 25) &&
            input.CrossPressBufferedSevenUpdates && input.BufferedCrossPressAge < input.NativeActionAge;
    }

    public static Rac1AirborneWallProbeResult Probe(
        in Rac1AirborneWallProbeInput input,
        Func<Rac1TraversalRay, Rac1TraversalRayHit?> raycast,
        Func<Rac1NativeVector3, bool> predictedLedgeContinuation)
    {
        ArgumentNullException.ThrowIfNull(raycast);
        ArgumentNullException.ThrowIfNull(predictedLedgeContinuation);
        Validate(input);
        if (input.GroundClearance < MinimumGroundClearance || input.RequestedVertical < MinimumRequestedVertical)
            return default;
        if (input.ExistingLedgeCandidate)
            return Cleared();

        const int predictionTicks = 20;
        var predicted = new Rac1NativeVector3(
            input.Position.X + input.ContactMotion.X * predictionTicks,
            input.Position.Y + input.ContactMotion.Y * predictionTicks,
            input.Position.Z + input.RequestedVertical * predictionTicks -
                input.GravityPerUpdate * predictionTicks * predictionTicks * 0.5f);
        if (predictedLedgeContinuation(predicted))
            return Cleared(22);

        var low = new Rac1TraversalRay(
            LocalPoint(input, input.BodyRadius - 0.02f, 0.3f),
            LocalPoint(input, 1f, 0.3f), Rac1TraversalRayMask.Support);
        if (raycast(low) is null)
            return default;

        double height = input.CurrentAction == 0x11 || input.ForwardContactFlag && !input.ForwardCrateFamily
            ? 1.7f : 3f;
        var high = new Rac1TraversalRay(LocalPoint(input, 0, height),
            LocalPoint(input, input.BodyRadius + 1.4f, height), Rac1TraversalRayMask.Support);
        if (raycast(high) is not { } hit || hit.SurfaceClass is 10 or 12)
            return default;
        ValidateVector(hit.Position);
        ValidateVector(hit.Normal);
        double normalPlanarLength = Math.Sqrt(hit.Normal.X * hit.Normal.X + hit.Normal.Y * hit.Normal.Y);
        double normalAngle = Math.Atan2(normalPlanarLength, hit.Normal.Z);
        if (normalAngle < MinimumWallAngle)
            return default;
        double normalYaw = Math.Atan2(hit.Normal.Y, hit.Normal.X);
        if (AngularDistance(normalYaw, input.FacingYaw + Math.PI) > MaximumFacingError)
            return default;
        double dx = hit.Position.X - high.From.X;
        double dy = hit.Position.Y - high.From.Y;
        double dz = hit.Position.Z - high.From.Z;
        double maximumDistance = input.CurrentAction == 0x11
            ? RepeatedMaximumHitDistance : OrdinaryMaximumHitDistance;
        if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > maximumDistance)
            return default;
        if (input.CurrentDomain != 4 && input.PreviousDomain != 4 && input.SecondPreviousDomain != 4)
            return default;
        if (input.HasWallJumped &&
            AngularDistance(input.PreviousWallNormalYaw, normalYaw) < MinimumRepeatedNormalDifference)
            return default;
        return new(Rac1AirborneWallProbeOutcome.Refreshed, hit.Normal, normalYaw,
            input.CurrentAction == 0x11 ? 7 : 6, 0);
    }

    private static Rac1AirborneWallProbeResult Cleared(int cooldown = 0) =>
        new(Rac1AirborneWallProbeOutcome.ClearedForLedge, default, 0, 0, cooldown);

    private static Rac1NativeVector3 LocalPoint(in Rac1AirborneWallProbeInput input, double forward, double height) =>
        new(input.Position.X + Math.Cos(input.FacingYaw) * forward,
            input.Position.Y + Math.Sin(input.FacingYaw) * forward, input.Position.Z + height);

    private static double AngularDistance(double first, double second) =>
        Math.Abs(Math.IEEERemainder(first - second, Math.Tau));

    private static void Validate(in Rac1AirborneWallProbeInput input)
    {
        ValidateVector(input.Position);
        ValidateVector(input.ContactMotion);
        if (!double.IsFinite(input.RequestedVertical) || !double.IsFinite(input.FacingYaw) ||
            !double.IsFinite(input.GroundClearance) || !double.IsFinite(input.GravityPerUpdate) ||
            input.GravityPerUpdate < 0 || !double.IsFinite(input.PreviousWallNormalYaw) ||
            !double.IsFinite(input.BodyRadius) || input.BodyRadius < 0.02f)
            throw new ArgumentOutOfRangeException(nameof(input));
    }

    private static void ValidateVector(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) || !double.IsFinite(vector.Z))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}

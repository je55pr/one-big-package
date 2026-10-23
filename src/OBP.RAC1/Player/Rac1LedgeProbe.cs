namespace OBP.RAC1.Player;

/// <summary>Native collision-query mask supplied to 0x1efc70.</summary>
public enum Rac1TraversalRayMask
{
    Support = 2,
    Solid = 4,
}

public readonly record struct Rac1TraversalRay(
    Rac1NativeVector3 From,
    Rac1NativeVector3 To,
    Rac1TraversalRayMask Mask);

/// <summary>
/// Source contact facts only. Dynamic surfaces require the native traversal
/// flag before admission; an unidentified dynamic object is not climbable.
/// </summary>
public readonly record struct Rac1TraversalRayHit(
    Rac1NativeVector3 Position,
    Rac1NativeVector3 Normal,
    int SurfaceClass,
    bool TraversalAllowed = true)
{
    /// <summary>
    /// Identified static-world face. Native 0x1f35d8 returns -1 for low class
    /// 31; that sentinel is not one of the ledge query's excluded classes.
    /// A static face needs no dynamic-owner traversal flag. Signed/special
    /// face query membership remains unsupported and conservatively closed.
    /// </summary>
    public static Rac1TraversalRayHit StaticWorld(
        Rac1NativeVector3 position, Rac1NativeVector3 normal, int rawFaceType)
    {
        var face = Rac1CollisionFaceSemantics.Decode(rawFaceType);
        return new(position, normal, face.SurfaceClass ?? -1, !face.IsSpecialHighBitFace);
    }
}

/// <summary>
/// Raw parameters of the native 0x1f19a0 clearance query, whose exact
/// radial/axial collision execution remains a host-adapter boundary.
/// </summary>
public readonly record struct Rac1TraversalBodyQuery(
    Rac1NativeVector3 Origin,
    double HorizontalRadius,
    double AxialSpan);

public readonly record struct Rac1LedgeProbeInput(
    Rac1NativeVector3 Position,
    Rac1NativeVector3 PreviousMotion,
    double FacingYaw,
    double LastSupportHeight,
    double PredictionGravityPerTick,
    int PredictionTicks = 2);

public readonly record struct Rac1LedgeCandidate(
    Rac1NativeVector3 TopPoint,
    Rac1NativeVector3 HangPosition,
    double WallNormalYaw,
    bool ClimbBodyBlocked)
{
    public double FacingYaw => Rac1LedgeProbe.WrapYaw(WallNormalYaw + Math.PI);
}

/// <summary>
/// Ordinary Ratchet ledge candidate query, transcribed from the LEVEL0 loaded
/// overlay 0x20bed0..0x20c6fc. Coordinates use native Z up. Collision callbacks
/// provide facts; the native probe order, material/angle gates and hang target
/// remain independent of Godot.
/// </summary>
public static class Rac1LedgeProbe
{
    public const double Radius = 0.45f;
    public const double ForwardProbeIncrement = 0.3f * 0.25f;
    public const double TopRayHigh = 1.5f + 0.15f;
    public const double TopRayLow = 1.15f;
    public const double MaximumTopAbovePlayer = 1.5f;
    public const double MinimumTopAboveSupport = 1.8f;
    public const double MaximumTopAngle = 0.3490658402442932f;
    public const double MaximumFacingError = 1.1344640254974365f;
    public const double HangHeightBelowTop = 1.43f;
    public const double WallSampleDepth = 0.7f;
    public const double ClearanceProbeIncrement = 0.03f;
    public const double MaximumClearanceSearch = 2.4f;

    /// <summary>
    /// The ordinary actor gate preceding the geometric query at 0x20bf30..
    /// 0x20bff4. The air-entry flag is the raw boolean at P+0x41e; it must
    /// not be inferred solely from an animation semantic or source action age.
    /// </summary>
    public static bool AdmitsOrdinaryRatchetCandidate(
        int nativeAction,
        int nativeMovementDomain,
        bool nativeAirEntryGuardIsSet,
        double requestedVertical,
        double authoredAnimationTime,
        int collisionMode = 0,
        int grabCooldownTicks = 0)
    {
        if (!double.IsFinite(requestedVertical) || !double.IsFinite(authoredAnimationTime))
            return false;
        if (collisionMode == 2 || grabCooldownTicks != 0)
            return false;
        bool admitted = nativeMovementDomain == 2 || nativeAction is 8 or 0x1a or 0x1b ||
            nativeMovementDomain == 4 &&
                (nativeAirEntryGuardIsSet || requestedVertical < 3.5f * (1f / 60f));
        return admitted && !(nativeAction == 0x0e && authoredAnimationTime < 37f);
    }

    public static bool TryFindCandidate(
        in Rac1LedgeProbeInput input,
        Func<Rac1TraversalRay, Rac1TraversalRayHit?> raycast,
        Func<Rac1TraversalBodyQuery, bool> overlaps,
        out Rac1LedgeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(raycast);
        ArgumentNullException.ThrowIfNull(overlaps);
        Validate(input);
        candidate = default;

        double forwardX = Math.Cos(input.FacingYaw);
        double forwardY = Math.Sin(input.FacingYaw);
        int ticks = input.PredictionTicks;
        double gravityTicks = ticks * (ticks + 1d) / 2d;
        var prediction = new Rac1NativeVector3(
            input.Position.X + input.PreviousMotion.X * ticks,
            input.Position.Y + input.PreviousMotion.Y * ticks,
            input.Position.Z + input.PreviousMotion.Z * ticks -
                input.PredictionGravityPerTick * gravityTicks);

        Rac1TraversalRayHit? topHit = null;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            double reach = Radius + ForwardProbeIncrement * attempt;
            double x = prediction.X + forwardX * reach;
            double y = prediction.Y + forwardY * reach;
            var hit = raycast(new Rac1TraversalRay(
                new(x, y, prediction.Z + TopRayHigh),
                new(x, y, prediction.Z + TopRayLow),
                Rac1TraversalRayMask.Solid));
            if (hit is { } value &&
                value.SurfaceClass is not (0 or 9 or 12) &&
                SurfaceAngle(value.Normal) < MaximumTopAngle)
            {
                topHit = value;
                break;
            }
        }
        if (topHit is not { } top || !top.TraversalAllowed ||
            top.Position.Z > input.Position.Z + MaximumTopAbovePlayer)
            return false;

        // The path to just above the top must be clear before the body-clearance
        // query. This rejects grabbing through walls or ceilings.
        double aboveTop = top.Position.Z + 0.05f;
        if (raycast(new Rac1TraversalRay(
            new(input.Position.X, input.Position.Y, aboveTop),
            new(top.Position.X, top.Position.Y, aboveTop),
            Rac1TraversalRayMask.Solid)) is not null)
            return false;
        bool bodyBlocked = overlaps(new Rac1TraversalBodyQuery(
            new(top.Position.X, top.Position.Y, top.Position.Z + 0.6f),
            Radius,
            0.6f));

        if (top.Position.Z - input.LastSupportHeight < MinimumTopAboveSupport)
            return false;

        double towardTopYaw = Math.Atan2(
            top.Position.Y - input.Position.Y,
            top.Position.X - input.Position.X);
        Rac1TraversalRayHit? wallHit = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            double height = top.Position.Z - WallSampleDepth * (1d - attempt / 5d);
            var hit = raycast(new Rac1TraversalRay(
                new(input.Position.X, input.Position.Y, height),
                new(top.Position.X + Math.Cos(towardTopYaw) * Radius * 2d,
                    top.Position.Y + Math.Sin(towardTopYaw) * Radius * 2d,
                    height),
                Rac1TraversalRayMask.Support));
            if (hit is not null)
            {
                wallHit = hit;
                break;
            }
        }
        if (wallHit is not { } wall)
            return false;

        double wallYaw = Math.Atan2(wall.Normal.Y, wall.Normal.X);
        if (Math.Abs(WrapYaw(input.FacingYaw - wallYaw - Math.PI)) >= MaximumFacingError)
            return false;

        // Search outward from the top until the one-unit vertical clearance
        // segment no longer intersects the edge. The previous half-step is
        // retained in the final hang target in native code.
        double wallX = Math.Cos(wallYaw);
        double wallY = Math.Sin(wallYaw);
        for (double offset = 0d; offset < MaximumClearanceSearch; offset += ClearanceProbeIncrement)
        {
            double x = top.Position.X + wallX * offset;
            double y = top.Position.Y + wallY * offset;
            if (raycast(new Rac1TraversalRay(
                new(x, y, top.Position.Z + 0.5f),
                new(x, y, top.Position.Z - 0.5f),
                Rac1TraversalRayMask.Solid)) is not null)
                continue;

            double hangOffset = offset - ClearanceProbeIncrement * 0.5f + Radius;
            candidate = new Rac1LedgeCandidate(
                top.Position,
                new(top.Position.X + wallX * hangOffset,
                    top.Position.Y + wallY * hangOffset,
                    top.Position.Z - HangHeightBelowTop),
                wallYaw,
                bodyBlocked);
            return true;
        }
        return false;
    }

    public static double WrapYaw(double yaw)
    {
        double wrapped = yaw % (Math.PI * 2d);
        if (wrapped > Math.PI)
            wrapped -= Math.PI * 2d;
        if (wrapped < -Math.PI)
            wrapped += Math.PI * 2d;
        return wrapped;
    }

    private static double SurfaceAngle(Rac1NativeVector3 normal) =>
        Math.Atan2(Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y), normal.Z);

    private static void Validate(in Rac1LedgeProbeInput input)
    {
        if (!double.IsFinite(input.Position.Length) ||
            !double.IsFinite(input.PreviousMotion.Length) ||
            !double.IsFinite(input.FacingYaw) ||
            !double.IsFinite(input.LastSupportHeight) ||
            !double.IsFinite(input.PredictionGravityPerTick) ||
            input.PredictionGravityPerTick < 0d || input.PredictionTicks is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(input));
    }
}

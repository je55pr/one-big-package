namespace OBP.RAC1.Player;

/// <summary>
/// Action-0x25 motion after the native Swingshot projectile attaches to a
/// class-758 pull target. Veldin 0x219a0c..0x219d74 and 0x22c5c8..0x22c7e0.
/// Starting this session does not claim to implement that projectile lifecycle.
/// </summary>
public sealed class Rac1TraversalEquipmentPull
{
    public const int Action = 0x25;
    public const int EntrySequence = 48;
    public const int EntryBlendTicks = 10;
    public const double SecondsPerUpdate = 1d / 60d;
    public const double MaximumStep = 27d * SecondsPerUpdate;
    public const double AccelerationStep = 80d * SecondsPerUpdate * SecondsPerUpdate;
    public const double BrakingStep = 42d * SecondsPerUpdate * SecondsPerUpdate;
    public const double ArrivalDistance = 1.9f;
    public const double AttachmentSideOffset = 0.57f;
    public const double NearArrivalTicks = 20;
    public const double VerticalCorrectionTicks = 30;
    public const double VerticalCorrectionStep = 29d * SecondsPerUpdate * SecondsPerUpdate;
    public const double AutomaticReleaseStep = 4d * SecondsPerUpdate;
    public const double MaximumReleaseScalar = 7d * SecondsPerUpdate;

    /// <summary>P+0x960: displacement magnitude per 60 Hz update.</summary>
    public double Speed { get; private set; }
    /// <summary>P+0x96c: source remaining-time estimate, in updates.</summary>
    public double RemainingTicks { get; private set; } = 60;
    /// <summary>P+0x2088 == 1.</summary>
    public bool IsBraking { get; private set; }
    /// <summary>Sticky P+0x974, also consumed by source pitch adjustment.</summary>
    public bool NearArrival { get; private set; }

    public void Reset()
    {
        Speed = 0;
        RemainingTicks = 60;
        IsBraking = false;
        NearArrival = false;
    }

    /// <summary>
    /// Emit one pre-contact request. Caller supplies current native facing after
    /// its source yaw update; native pitch/rotation presentation is separate.
    /// Contact length is P+0x160, excluding separately applied external carry.
    /// </summary>
    public Rac1NativeVector3 Step(Rac1NativeVector3 position, Rac1NativeVector3 anchor,
        double contactMotionLength, double nativeFacingYaw)
    {
        Validate(position);
        Validate(anchor);
        if (!double.IsFinite(contactMotionLength) || contactMotionLength < 0)
            throw new ArgumentOutOfRangeException(nameof(contactMotionLength));
        if (!double.IsFinite(nativeFacingYaw))
            throw new ArgumentOutOfRangeException(nameof(nativeFacingYaw));
        double distance = Difference(anchor, position).Length;
        RemainingTicks = Speed > 0 ? distance / contactMotionLength : 60;
        Speed = IsBraking
            ? Math.Max(0, Speed - BrakingStep)
            : Math.Min(MaximumStep, Speed + AccelerationStep);

        // 0x2595a0 writes the larger quadratic root first, including when
        // a < 0. Source tests it only after a nonnegative discriminant.
        double discriminant = Speed * Speed - 2 * BrakingStep * (distance - ArrivalDistance);
        if (discriminant >= 0)
        {
            double largerRoot = (Speed + Math.Sqrt(discriminant)) / BrakingStep;
            if (largerRoot > 0)
            {
                RemainingTicks = largerRoot;
                IsBraking = true;
            }
        }
        if (RemainingTicks < NearArrivalTicks)
            NearArrival = true;

        // Native cos/sin(yaw-pi/2) * -0.57 offsets the tether attachment
        // sideways from P+0x80 before the 3D set-length helper.
        var attachment = new Rac1NativeVector3(
            position.X - Math.Sin(nativeFacingYaw) * AttachmentSideOffset,
            position.Y + Math.Cos(nativeFacingYaw) * AttachmentSideOffset,
            position.Z);
        var direction = Difference(anchor, attachment);
        double scale = direction.Length == 0 ? 0 : Speed / direction.Length;
        double vertical = direction.Z * scale;
        if (RemainingTicks < VerticalCorrectionTicks)
            vertical -= VerticalCorrectionStep * (VerticalCorrectionTicks - RemainingTicks);
        return new(direction.X * scale, direction.Y * scale, vertical);
    }

    /// <summary>
    /// Evaluate native action update after motion/contact. Source use release or
    /// a braking speed strictly below 4*dt completes the pull. The host can carry
    /// full XYZ contact momentum into action 6 via its own collision result.
    /// </summary>
    public Rac1TraversalEquipmentPullExit? EvaluateExit(bool useHeld, double nativeSupportDistance)
    {
        if (!double.IsFinite(nativeSupportDistance) || nativeSupportDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeSupportDistance));
        if (useHeld && (!IsBraking || Speed >= AutomaticReleaseStep))
            return null;
        return new(
            nativeSupportDistance < (double)0.3f ? 0 : 6,
            Math.Min(Speed, MaximumReleaseScalar),
            !useHeld && RemainingTicks > 25,
            VerticalCorrectionStep,
            BrakingStep);
    }

    private static Rac1NativeVector3 Difference(Rac1NativeVector3 left, Rac1NativeVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static void Validate(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) ||
            !double.IsFinite(vector.Z) || !double.IsFinite(vector.Length))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}

/// <summary>
/// Pull exit preserves action-6 full XYZ contact motion. Fall fields retain
/// their source offsets because their combined recurrence has a separate owner.
/// </summary>
public readonly record struct Rac1TraversalEquipmentPullExit(
    int NativeAction,
    double ScalarSpeed,
    bool PrematureRelease,
    double NativeFallField940,
    double NativeFallField944);

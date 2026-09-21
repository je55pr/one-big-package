using OBP.Runtime.Camera;

namespace OBP.RAC1.Camera;

/// <summary>
/// Pure recovered recurrence stages for the ordinary R&amp;C1 type-0 camera.
/// These methods intentionally stop where the retail producer is still unknown.
/// </summary>
public static class Rac1CameraRecurrence
{
    public const uint DirectionMask = 0x0000A000;
    public const uint NegativeDirectionBit = 0x00008000;
    public const double HeadingStepIncrement = 0.0020000000949949026d;
    public const double HeadingStepClamp = 0.03999999910593033d;
    public const double HeadingReleaseDivisor = 1.5d;

    public const double FollowZAcceleration = 0.007499999832361937d;
    public const double FollowZDamping = 0.17499999701976776d;
    public const double EyeHeightAcceleration = 0.004000000189989805d;
    public const double EyeHeightDamping = 0.20000000298023224d;
    public const double ProfileTransitionAcceleration = 0.003000000026077032d;

    public const double ObstructionPullInStep = 0.07500000298023224d;
    public const double ObstructionInnerRadiusFloor = 0.20000000298023224d;
    public const double ObstructionFinalRadiusFloor = 1.5d;
    public const int ObstructionContactTimerTicks = 2180;
    public const int ObstructionMinimumRadiusTimerTicks = 2000;
    public const double ObstructionSideThreshold = 0.75d;
    public const double ObstructionLateralStepRadians = 0.01745329238474369d;

    public readonly record struct HeadingResult(
        double ControlHeading,
        double HeadingStep);

    public readonly record struct ScalarResult(
        double Value,
        double Velocity);

    public readonly record struct ObstructionRecoveryResult(
        double RadialCorrection,
        int ReleaseTimerTicks);

    public readonly record struct RadiusGuardResult(
        double RadialCorrection,
        int ReleaseTimerTicks,
        bool WasClamped);

    /// <summary>
    /// Replays the verified I+0x1a0 heading-step stage. Invocation cadence is
    /// deliberately left to the caller because this is only one camera producer.
    /// </summary>
    public static HeadingResult AdvanceDirectionalHeading(
        double controlHeading,
        double headingStep,
        uint directionFlags)
    {
        ValidateFinite(controlHeading, nameof(controlHeading));
        ValidateFinite(headingStep, nameof(headingStep));

        if ((directionFlags & DirectionMask) != 0)
        {
            double sign = (directionFlags & NegativeDirectionBit) != 0 ? -1d : 1d;
            headingStep = Math.Clamp(
                headingStep + (sign * HeadingStepIncrement),
                -HeadingStepClamp,
                HeadingStepClamp);
        }
        else
        {
            headingStep /= HeadingReleaseDivisor;
        }

        return new HeadingResult(
            WrapPi(controlHeading - headingStep),
            headingStep);
    }

    /// <summary>
    /// Native damped-step helper: accelerate toward target, damp stored velocity,
    /// then clamp the step so it cannot overshoot the remaining delta.
    /// </summary>
    public static ScalarResult AdvanceDampedScalar(
        double current,
        double velocity,
        double target,
        double acceleration,
        double damping)
    {
        ValidateFinite(current, nameof(current));
        ValidateFinite(velocity, nameof(velocity));
        ValidateFinite(target, nameof(target));
        ValidateFinite(acceleration, nameof(acceleration));
        ValidateFinite(damping, nameof(damping));

        double delta = target - current;
        double nextVelocity =
            velocity + (acceleration * delta) - (damping * velocity);

        if ((delta >= 0d && nextVelocity > delta) ||
            (delta <= 0d && nextVelocity < delta))
        {
            nextVelocity = delta;
        }

        return new ScalarResult(current + nextVelocity, nextVelocity);
    }

    public static ScalarResult AdvanceOrdinaryFollowZ(
        double current,
        double velocity,
        double target) =>
        AdvanceDampedScalar(
            current,
            velocity,
            target,
            FollowZAcceleration,
            FollowZDamping);

    public static ScalarResult AdvanceEyeHeight(
        double current,
        double velocity,
        double preferredHeight) =>
        AdvanceDampedScalar(
            current,
            velocity,
            preferredHeight,
            EyeHeightAcceleration,
            EyeHeightDamping);

    /// <summary>
    /// Verified active-contact operation on the routine's radial working value.
    /// The surrounding contact solver remains intentionally outside this helper.
    /// </summary>
    public static double PullInWorkingRadius(
        double workingRadius,
        double frameScale = 1d)
    {
        ValidateFinite(workingRadius, nameof(workingRadius));
        ValidateFinite(frameScale, nameof(frameScale));
        if (frameScale < 0d)
            throw new ArgumentOutOfRangeException(nameof(frameScale));

        return Math.Max(
            ObstructionInnerRadiusFloor,
            workingRadius - (ObstructionPullInStep * frameScale));
    }

    public static int ClassifyLateralSide(double lateralDot)
    {
        ValidateFinite(lateralDot, nameof(lateralDot));
        if (lateralDot > ObstructionSideThreshold) return 1;
        if (lateralDot < -ObstructionSideThreshold) return -1;
        return 0;
    }

    public static double LateralCorrectionRadians(int side) =>
        side switch
        {
            -1 => -ObstructionLateralStepRadians,
            0 => 0d,
            1 => ObstructionLateralStepRadians,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };

    /// <summary>
    /// Unit-time clear-line release. Native code decrements the signed timer,
    /// divides remaining ticks by 2000 and recursively eases the current
    /// correction toward zero.
    /// </summary>
    public static ObstructionRecoveryResult AdvanceClearLineRecovery(
        double radialCorrection,
        int releaseTimerTicks,
        int elapsedTicks = 1)
    {
        ValidateFinite(radialCorrection, nameof(radialCorrection));
        if (releaseTimerTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(releaseTimerTicks));
        if (elapsedTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks));

        int remaining = Math.Max(0, releaseTimerTicks - elapsedTicks);
        if (remaining == 0 || radialCorrection <= 0d)
            return new ObstructionRecoveryResult(0d, remaining);

        double t = remaining / (double)ObstructionMinimumRadiusTimerTicks;
        return new ObstructionRecoveryResult(
            CosineEase(0d, radialCorrection, t),
            remaining);
    }

    /// <summary>
    /// Final ordinary guard proven after obstruction solving. It constrains
    /// preferredRadius - correction to at least 1.5 and arms 2000 ticks on clamp.
    /// </summary>
    public static RadiusGuardResult EnforceFinalEffectiveRadiusFloor(
        double preferredRadius,
        double radialCorrection,
        int releaseTimerTicks)
    {
        ValidateFinite(preferredRadius, nameof(preferredRadius));
        ValidateFinite(radialCorrection, nameof(radialCorrection));
        if (preferredRadius < ObstructionFinalRadiusFloor)
            throw new ArgumentOutOfRangeException(nameof(preferredRadius));
        if (radialCorrection < 0d)
            throw new ArgumentOutOfRangeException(nameof(radialCorrection));
        if (releaseTimerTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(releaseTimerTicks));

        double maximumCorrection = preferredRadius - ObstructionFinalRadiusFloor;
        if (radialCorrection <= maximumCorrection)
        {
            return new RadiusGuardResult(
                radialCorrection,
                releaseTimerTicks,
                false);
        }

        return new RadiusGuardResult(
            maximumCorrection,
            ObstructionMinimumRadiusTimerTicks,
            true);
    }

    public static double CosineEase(double a, double b, double t)
    {
        ValidateFinite(a, nameof(a));
        ValidateFinite(b, nameof(b));
        ValidateFinite(t, nameof(t));
        return a + ((b - a) * 0.5d * (1d - Math.Cos(Math.PI * t)));
    }

    public static double WrapPi(double angle)
    {
        ValidateFinite(angle, nameof(angle));
        double wrapped = (angle + Math.PI) % Math.Tau;
        if (wrapped < 0d) wrapped += Math.Tau;
        return wrapped - Math.PI;
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

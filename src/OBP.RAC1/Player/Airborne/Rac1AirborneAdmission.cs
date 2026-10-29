namespace OBP.RAC1.Player;

/// <summary>
/// Source selectors at 0x221df8 and 0x229660. Buffer membership is supplied as
/// already decoded native input-history facts; this class does not invent a
/// wall-clock input grace period or a generic second-Cross double jump.
/// </summary>
public static class Rac1AirborneAdmission
{
    public const float NativePi = 3.1415927410125732f;
    public const float NativeHalfPi = 1.5707963705062866f;
    public const float NativeQuarterPi = 0.7853981852531433f;
    public const float LongJumpMinimumMagnitude = 0.7f;
    public const float LongJumpMinimumForwardStep = 0.9f * (1f / 60f);

    public readonly record struct CrouchJumpInput(
        int CurrentAction,
        int CrouchActionAge,
        bool CrouchHeld,
        bool CrossBufferedNineUpdates,
        bool DirectionalChordBuffered,
        int FlipDirection,
        int ActivePackId,
        bool ClankAvailable,
        bool PackDisabled,
        float ForwardClearance,
        float ForwardHitNormalAngle,
        bool ForwardHitCrateFamily,
        float ConditionedMagnitude,
        float ForwardRequestStep,
        bool ThrusterLongJumpGate);

    public readonly record struct Selection(Rac1AirborneJumpKind? Kind, bool ConsumesJumpInput);

    /// <summary>
    /// 0 right, 1 left, 2 forward, 3 back. Native ties at the strict 45-degree
    /// sector boundaries remain forward. Input/facing angles must already be
    /// in the native [-pi,pi] interval, as in the traced caller.
    /// </summary>
    public static int ClassifyFlipDirection(float inputYaw, float facingYaw)
    {
        ValidateAngle(inputYaw, nameof(inputYaw));
        ValidateAngle(facingYaw, nameof(facingYaw));
        float delta = inputYaw - facingYaw;
        if (delta >= NativePi)
            delta = (delta - NativePi) - NativePi;
        if (delta < -NativePi)
            delta = (delta + NativePi) + NativePi;
        if (AngularDistance(delta, NativePi) < NativeQuarterPi)
            return 3;
        if (AngularDistance(delta, -NativeHalfPi) < NativeQuarterPi)
            return 1;
        if (AngularDistance(delta, NativeHalfPi) < NativeQuarterPi)
            return 0;
        return 2;
    }

    public static int DirectionalChordWindow(int flipDirection)
    {
        ValidateDirection(flipDirection);
        return flipDirection == 2 ? 0 : 12;
    }

    public static bool CanEnterDirectionalFlip(bool crouchHeld, int flipDirection, bool directionalChordBuffered) =>
        crouchHeld && DirectionalChordWindow(flipDirection) > 0 && directionalChordBuffered;

    /// <summary>
    /// ForwardClearance=P+0x248 is the forward obstruction query, not ground
    /// clearance P+0x2dc. ForwardRequestStep=P+0x168 is the nonnegative forward
    /// projection of the original controlled request, not vertical velocity.
    /// </summary>
    public static Selection SelectCrouchJump(CrouchJumpInput input)
    {
        ValidateDirection(input.FlipDirection);
        if (input.CrouchActionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        ValidateNonnegative(input.ForwardClearance, nameof(input));
        ValidateNonnegative(input.ForwardHitNormalAngle, nameof(input));
        ValidateNonnegative(input.ForwardRequestStep, nameof(input));
        if (!float.IsFinite(input.ConditionedMagnitude) || input.ConditionedMagnitude < 0 || input.ConditionedMagnitude > 1)
            throw new ArgumentOutOfRangeException(nameof(input));
        if (input.CurrentAction == 0x0e)
            return default;
        if (CanEnterDirectionalFlip(input.CrouchHeld, input.FlipDirection, input.DirectionalChordBuffered))
            return new(Rac1AirborneJumpKind.DirectionalFlip, true);
        if (input.CrouchActionAge <= 6 || !input.CrossBufferedNineUpdates)
            return default;

        bool sufficientInput = input.ConditionedMagnitude > LongJumpMinimumMagnitude &&
            input.ForwardRequestStep > LongJumpMinimumForwardStep;
        if (input.ActivePackId == 2 && input.ClankAvailable && !input.PackDisabled && sufficientInput &&
            (input.ForwardClearance > 2.5f || input.ForwardHitNormalAngle < NativeQuarterPi))
            return new(Rac1AirborneJumpKind.HeliPackLong, true);
        if (input.ActivePackId == 3 && !input.PackDisabled && sufficientInput &&
            (input.ForwardClearance > 2.5f || input.ForwardHitCrateFamily))
        {
            // The native branch reports success without entering an action
            // when P+0x1b8 blocks this attempted long jump.
            return input.ThrusterLongJumpGate
                ? new(null, true)
                : new(Rac1AirborneJumpKind.ThrusterPackLong, true);
        }
        if (input.ActivePackId == 3 && !input.PackDisabled)
            return new(Rac1AirborneJumpKind.ThrusterPackHigh, true);
        if (input.ActivePackId == 2 && input.ClankAvailable && !input.PackDisabled)
            return new(Rac1AirborneJumpKind.HeliPackHigh, true);
        return new(Rac1AirborneJumpKind.Stationary, true);
    }

    private static float AngularDistance(float a, float b)
    {
        float value = MathF.Abs(a - b);
        return value < NativePi ? value : (NativePi + NativePi) - value;
    }

    private static void ValidateAngle(float angle, string name)
    {
        if (!float.IsFinite(angle) || angle < -NativePi || angle > NativePi)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateNonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateDirection(int value)
    {
        if (value is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

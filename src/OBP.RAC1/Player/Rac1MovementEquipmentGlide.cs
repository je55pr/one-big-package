namespace OBP.RAC1.Player;

/// <summary>
/// Regular-axis action-8 glide motion recovered from Veldin 0x21e8cc..0x21ead0.
/// Callers own equipment/admission, facing, contact resolution and interruption.
/// Values are native displacement per 60 Hz update. This is not a gravity fit.
/// </summary>
public static class Rac1MovementEquipmentGlide
{
    public const int NativeAction = 0x08;
    public const int NativeSequence = 19;
    public const int EntryBlend = -2;
    public const int EntryFrame = 0;
    public const float SecondsPerUpdate = 1f / 60f;
    public const float SquaredSecondsPerUpdate = 1f / 3600f;
    public const float AccelerationPerUpdate = 15f * SquaredSecondsPerUpdate;
    public const float DecelerationPerUpdate = 7f * SquaredSecondsPerUpdate;
    public const float EntryMaximumControlledMotion = 4.5f * SecondsPerUpdate;

    public readonly record struct StepResult(float PlanarScalar, float VerticalStep);

    /// <summary>
    /// Entry 0x224520 compares the 3D contact-processed length P+0x160 to
    /// 4.5*dt, then helper 0x213de8 scales P+0x194 and request/contact/carry
    /// vectors by the same ratio. The cap is not a planar-only clamp.
    /// </summary>
    public static float EnterScalar(float currentPlanarScalar, float contactMotion3DLength)
    {
        ValidateScalar(currentPlanarScalar, nameof(currentPlanarScalar));
        ValidateScalar(contactMotion3DLength, nameof(contactMotion3DLength));
        return contactMotion3DLength > EntryMaximumControlledMotion
            ? currentPlanarScalar * (EntryMaximumControlledMotion / contactMotion3DLength)
            : currentPlanarScalar;
    }

    /// <summary>
    /// Native equipment IDs are verified independently from their descriptors:
    /// Heli-Pack 2 and Thruster-Pack 3. Hydro-Pack 4 is not an aerial glide.
    /// The vertical descent depends on pack base speed, not input magnitude.
    /// </summary>
    public static StepResult Step(int nativePackId, float currentPlanarScalar, float conditionedMagnitude)
    {
        ValidateScalar(currentPlanarScalar, nameof(currentPlanarScalar));
        if (!float.IsFinite(conditionedMagnitude) || conditionedMagnitude < 0 || conditionedMagnitude > 1)
            throw new ArgumentOutOfRangeException(nameof(conditionedMagnitude));
        float baseStep = BasePlanarStep(nativePackId);
        float target = baseStep * conditionedMagnitude;
        float delta = target - currentPlanarScalar;
        float amount = currentPlanarScalar < target ? AccelerationPerUpdate : DecelerationPerUpdate;
        float planar = currentPlanarScalar + Math.Clamp(delta, -amount, amount);
        return new StepResult(planar, -(baseStep * 0.72f));
    }

    public static float BasePlanarStep(int nativePackId) => nativePackId switch
    {
        2 => 3f * SecondsPerUpdate,
        3 => 5f * SecondsPerUpdate,
        _ => throw new ArgumentOutOfRangeException(nameof(nativePackId)),
    };

    /// <summary>
    /// The bounded admission at 0x22f648..0x22f830. The caller supplies
    /// the already resolved native age gate (P+0x4a6); other earlier action
    /// transitions must have been processed before invoking this tail.
    /// </summary>
    public static bool CanEnter(
        int nativePackId, bool clankAvailable, bool packDisabled,
        bool crossHeld, int actionAge, int admissionAge, float groundClearance,
        int previousAction, bool recentCrossPress = false)
    {
        if (nativePackId is not (2 or 3))
            return false;
        if (previousAction is 0x0b or 0x11 or 0x12 or 0x1c or 0x3c or 0x69)
            return false;
        // Without a recent Cross edge, the earlier caller skips this tail for
        // ordinary/stretch jumps. High pack jumps and aerial boost are the
        // three explicit native exceptions, not a generic held-Cross rule.
        if (!recentCrossPress && previousAction is not (0x0d or 0x0e or 0x0f))
            return false;
        if (actionAge < 0 || admissionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(actionAge));
        ValidateScalar(groundClearance, nameof(groundClearance));
        float requiredClearance = previousAction switch
        {
            7 or 9 => 1f,
            0x0e => 1.5f,
            _ => 0.6f,
        };
        int minimumTailAge = previousAction switch
        {
            0x10 => 8,
            0x0e when groundClearance > 4 => 17,
            _ => 20,
        };
        return clankAvailable && !packDisabled && crossHeld &&
            actionAge > Math.Max(admissionAge, minimumTailAge) && groundClearance > requiredClearance;
    }

    private static void ValidateScalar(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

namespace OBP.RAC1.Player;

/// <summary>
/// Action-0x0e aerial boost recovered from 0x214108..0x2142d4 and its
/// 0x22f684..0x22f764 admission. This is separate from grounded pack high
/// jumps, directional flips and action-8 glide. It does not assume a pack
/// ownership requirement absent from this admission branch.
/// </summary>
public sealed class Rac1AirborneBoostSession
{
    public const int NativeAction = 0x0e;
    public const int NativeSequence = 22;
    public const int EntryFrame = 3;
    public const int EntryBlendTicks = 7;
    public const float EntryPlaybackMultiplier = 0.8f;
    public const float MaximumPlanarStep = 3.5f * (1f / 60f);
    public const float GravityPerUpdate = 29.7f * (1f / 3600f);
    public const float BoostAccelerationPerUpdate = 150f * (1f / 3600f);

    private bool _ceilingBlocked;

    public Rac1AirborneBoostSession(float contactVerticalStep)
    {
        ValidateFinite(contactVerticalStep, nameof(contactVerticalStep));
        VerticalStep = contactVerticalStep;
    }

    public float VerticalStep { get; private set; }
    public bool Finished { get; private set; }

    public readonly record struct AdmissionInput(
        int CurrentAction, int NativeActionAge, bool CrossBuffered,
        bool BoostAllowed, bool AnimationGuard, bool LandingStarted,
        bool SpecialSurfaceFlag, float GroundClearance,
        bool HasApexed, float CurrentVerticalStep);

    public readonly record struct StepInput(
        int NativeActionAge, float ContactVerticalStep,
        float RiseFromOriginalLaunch, int NativePackId,
        bool ClankAvailable, bool PackDisabled,
        bool HitCeiling = false, bool Landed = false);

    public static int CrossBufferWindow(int nativeActionAge)
    {
        if (nativeActionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeActionAge));
        return Math.Clamp(nativeActionAge - 4, 2, 30);
    }

    public static bool CanEnter(AdmissionInput input)
    {
        if (input.NativeActionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        ValidateFinite(input.GroundClearance, nameof(input));
        ValidateFinite(input.CurrentVerticalStep, nameof(input));
        if (input.CurrentAction is 0x0b or 0x0e or 0x11 or 0x12 or 0x1c or 0x3c or 0x69 ||
            !input.BoostAllowed || input.AnimationGuard || input.LandingStarted ||
            !input.CrossBuffered || input.NativeActionAge <= 14 ||
            (input.SpecialSurfaceFlag && input.GroundClearance < 0.4f))
            return false;
        return !input.HasApexed ||
            (input.GroundClearance > 0.7f && MathF.Abs(input.CurrentVerticalStep) < 5.8f * (1f / 60f));
    }

    /// <summary>
    /// Rise is measured from native P+0x400, retained from the original jump;
    /// action-0x0e does not reset it to the new boost-entry position.
    /// </summary>
    public float Step(StepInput input)
    {
        if (input.NativeActionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        ValidateFinite(input.ContactVerticalStep, nameof(input));
        ValidateFinite(input.RiseFromOriginalLaunch, nameof(input));
        if (Finished)
            return 0;
        if (input.Landed)
        {
            Finished = true;
            VerticalStep = 0;
            return 0;
        }
        if (input.HitCeiling)
        {
            _ceilingBlocked = true;
            VerticalStep = MathF.Min(VerticalStep, 0);
        }
        bool thrusterThresholds = input.NativePackId == 3 && input.ClankAvailable && !input.PackDisabled;
        float target = 7f * (1f / 60f);
        if (thrusterThresholds)
        {
            if (input.RiseFromOriginalLaunch > 2.7f)
                target *= 0.47f;
            else if (input.RiseFromOriginalLaunch > 2.1f)
                target *= 0.7f;
        }
        else
        {
            if (input.RiseFromOriginalLaunch > 2.1f)
                target *= 0.45f;
            else if (input.RiseFromOriginalLaunch > 1.7f)
                target *= 0.7f;
        }
        if (input.NativePackId == 3 && !input.PackDisabled)
            target *= 1.25f;
        if (!_ceilingBlocked && input.NativeActionAge < 8 && VerticalStep < target)
            VerticalStep += Math.Clamp(target - VerticalStep, -BoostAccelerationPerUpdate, BoostAccelerationPerUpdate);
        VerticalStep -= GravityPerUpdate;
        VerticalStep = MathF.Max(VerticalStep, input.ContactVerticalStep - 0.1f);
        VerticalStep = MathF.Max(VerticalStep, -(50f * (1f / 60f)));
        return VerticalStep;
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

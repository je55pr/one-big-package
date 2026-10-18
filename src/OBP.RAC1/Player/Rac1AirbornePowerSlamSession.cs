namespace OBP.RAC1.Player;

/// <summary>
/// Thruster-Pack Power Slam, action 0x22: entry 0x223e5c, motion 0x21a720,
/// update 0x22c8e8. Regular vertical-axis motion only. Damage/lock activation,
/// camera shake, contact execution and animation-rate prediction remain owned
/// by their native producers or explicitly identified host adapters.
/// </summary>
public sealed class Rac1AirbornePowerSlamSession
{
    public const int NativeAction = 0x22;
    public const int NativeSequence = 42;
    public const int EntryFrame = 0;
    public const int EntryBlendTicks = 6;
    public const float PlanarDecayPerUpdate = 30f * (1f / 3600f);
    public const float RiseTargetPerUpdate = 5.7f * (1f / 60f);
    public const float RiseAccelerationPerUpdate = 70f * (1f / 3600f);
    public const float DiveAccelerationPerUpdate = 100f * (1f / 3600f);

    private float _planarX;
    private float _planarY;

    /// <summary>
    /// Entry preserves P+0xe0 and P+0x194. These are current controlled request
    /// and scalar, not a new copy of contact-processed P+0x110.
    /// </summary>
    public Rac1AirbornePowerSlamSession(float requestX, float requestY, float verticalRequest, float planarScalar)
    {
        ValidateFinite(requestX, nameof(requestX));
        ValidateFinite(requestY, nameof(requestY));
        ValidateFinite(verticalRequest, nameof(verticalRequest));
        ValidateFinite(planarScalar, nameof(planarScalar));
        if (planarScalar < 0)
            throw new ArgumentOutOfRangeException(nameof(planarScalar));
        _planarX = requestX;
        _planarY = requestY;
        VerticalRequest = verticalRequest;
        PlanarScalar = planarScalar;
    }

    public float VerticalRequest { get; private set; }
    public float PlanarScalar { get; private set; }
    public bool IsDiving { get; private set; }
    public int ImpactUpdates { get; private set; }
    public bool Finished { get; private set; }

    public readonly record struct AdmissionInput(
        int CurrentAction, int NativeActionAge, int LaunchAge,
        bool HasApexed, int PredictedLandingTicks,
        float ConditionedMagnitude, bool CrouchPressBufferedNineUpdates,
        int ActivePackId, bool PackDisabled);

    public readonly record struct StepInput(
        int NativeActionAge, int UnsupportedUpdates,
        bool NativeAnimationExitRequested = false);

    public readonly record struct StepResult(
        float PlanarX, float PlanarY, float Vertical,
        bool IsDiving, int ImpactUpdates, bool JustImpacted,
        bool RequestsDiveDamageProbe, bool Finished);

    public static bool CanEnter(AdmissionInput input)
    {
        if (input.NativeActionAge < 0 || input.LaunchAge < -1 || input.PredictedLandingTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        ValidateFinite(input.ConditionedMagnitude, nameof(input));
        if (input.ConditionedMagnitude < 0 || input.ConditionedMagnitude > 1)
            throw new ArgumentOutOfRangeException(nameof(input));
        return input.CurrentAction is 7 or 9 or 0x0d or 0x0e or 0x0f &&
            input.NativeActionAge > input.LaunchAge + 8 &&
            (!input.HasApexed || input.PredictedLandingTicks > 10) &&
            input.ConditionedMagnitude < 0.3f && input.CrouchPressBufferedNineUpdates &&
            input.ActivePackId == 3 && !input.PackDisabled;
    }

    public StepResult Step(StepInput input)
    {
        if (input.NativeActionAge < 0 || input.UnsupportedUpdates < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        if (Finished)
            return Result(false, false);
        if (input.NativeAnimationExitRequested)
        {
            Finished = true;
            return Result(false, false);
        }

        PlanarScalar = MathF.Max(0, PlanarScalar - PlanarDecayPerUpdate);
        float length = MathF.Sqrt((_planarX * _planarX) + (_planarY * _planarY));
        if (length > 0)
        {
            float scale = PlanarScalar / length;
            _planarX *= scale;
            _planarY *= scale;
        }
        else
        {
            _planarX = 0;
            _planarY = 0;
        }
        if (input.NativeActionAge < 12)
            VerticalRequest += Math.Clamp(RiseTargetPerUpdate - VerticalRequest,
                -RiseAccelerationPerUpdate, RiseAccelerationPerUpdate);
        else if (input.NativeActionAge > 33)
        {
            VerticalRequest -= DiveAccelerationPerUpdate;
            IsDiving |= VerticalRequest < 0;
        }
        // Between native ages 12 and 33 inclusive the stored vertical request
        // remains unchanged. Do not import ordinary jump gravity into this hold.
        bool damageProbe = IsDiving && input.UnsupportedUpdates != 0;
        bool impacted = IsDiving && input.UnsupportedUpdates == 0;
        bool justImpacted = impacted && ImpactUpdates == 0;
        if (impacted)
            ImpactUpdates = checked(ImpactUpdates + 1);
        return Result(justImpacted, damageProbe);
    }

    private StepResult Result(bool justImpacted, bool damageProbe) =>
        new(_planarX, _planarY, VerticalRequest, IsDiving, ImpactUpdates,
            justImpacted, damageProbe, Finished);

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

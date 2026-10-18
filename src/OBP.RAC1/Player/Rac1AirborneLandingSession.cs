namespace OBP.RAC1.Player;

/// <summary>
/// Flat, ordinary-axis landing ownership from 0x21edb0..0x21ee68,
/// 0x22ee30..0x22ef40, 0x22f224..0x22f384 and 0x2293a8..0x22965c.
/// A landing retains its airborne action until the source selector admits a
/// ground action. Scalar, requested motion and contact carry stay separate.
/// Surface/alternate-axis overrides and input-buffered landing jumps are not
/// synthesized by this bounded contract.
/// </summary>
public sealed class Rac1AirborneLandingSession
{
    public const float LaterLandingRequestScale = 0.4f;
    public const float StopCarryLimit = 5f * (1f / 60f);
    public const float CrouchCarryLimit = 7f * (1f / 60f);
    public const float HeliStopCarryScale = 0.8f;
    public const float IdleCarryDeceleration = 12.6f * (1f / 3600f);
    public const float StopCarryDeceleration = 12f * (1f / 3600f);

    public Rac1AirborneLandingSession(int nativeAction, float scalar,
        Rac1AirbornePlanarMotion.PlanarStep carry = default)
    {
        ValidateAction(nativeAction);
        Validate(scalar);
        Validate(carry.X);
        Validate(carry.Y);
        if (scalar < 0)
            throw new ArgumentOutOfRangeException(nameof(scalar));
        NativeAction = nativeAction;
        Scalar = scalar;
        Carry = carry;
    }

    public int NativeAction { get; }
    public int LandingUpdates { get; private set; } = 1;
    public float Scalar { get; private set; }
    public Rac1AirbornePlanarMotion.PlanarStep Carry { get; private set; }
    public bool Finished { get; private set; }

    public readonly record struct AdmissionInput(
        int NativeAction, int NativeActionAge, int LaunchAge,
        int UnsupportedUpdates, bool HasApexed, float AuthoredAnimationTime);

    public readonly record struct StepInput(
        float FacingYaw, float ConditionedMagnitude, bool CrouchHeld,
        float ForwardRequestStep, Rac1NativeVector3 ContactMotion,
        bool NativeAnimationExitRequested = false, bool ControlLocked = false);

    public readonly record struct Handoff(
        int NextAction, Rac1NativeVector3 ContactCarry,
        float ControlledScalar, int? Sequence, int? EntryFrame, int BlendTicks);

    public readonly record struct StepResult(
        Rac1AirbornePlanarMotion.PlanarStep Request,
        float ControlledScalar, Rac1AirbornePlanarMotion.PlanarStep Carry,
        int LandingUpdates, Handoff? GroundHandoff);

    /// <summary>
    /// P+0x418 starts only after launch, support P+0x30e==0 and apex P+0x41e.
    /// Aerial boost additionally suppresses landing while authored time <35.
    /// The host maps its admitted support/apex facts explicitly to these inputs.
    /// </summary>
    public static bool CanBegin(AdmissionInput input)
    {
        ValidateAction(input.NativeAction);
        Validate(input.AuthoredAnimationTime);
        if (input.NativeActionAge < 0 || input.LaunchAge < -1 || input.UnsupportedUpdates < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        return input.NativeActionAge > input.LaunchAge && input.UnsupportedUpdates == 0 && input.HasApexed &&
            (input.NativeAction != 0x0e || input.AuthoredAnimationTime >= 35f);
    }

    /// <summary>
    /// The counter begins at native P+0x418=1. Motion uses that prior counter,
    /// then the update increments it before selecting the next action. Keep the prior
    /// action/animation while GroundHandoff is null, rather than entering an
    /// ordinary run/idle controller on the first grounded bit.
    /// </summary>
    public StepResult Step(StepInput input)
    {
        Validate(input.FacingYaw);
        Validate(input.ConditionedMagnitude);
        Validate(input.ForwardRequestStep);
        if (input.ConditionedMagnitude < 0 || input.ConditionedMagnitude > 1 || input.ForwardRequestStep < 0 ||
            !double.IsFinite(input.ContactMotion.Length))
            throw new ArgumentOutOfRangeException(nameof(input));
        if (Finished)
            throw new InvalidOperationException("The landing has already handed off its action.");

        float requestedScalar = LandingUpdates >= 2 ? Scalar * LaterLandingRequestScale : Scalar;
        var request = new Rac1AirbornePlanarMotion.PlanarStep(
            MathF.Cos(input.FacingYaw) * requestedScalar, MathF.Sin(input.FacingYaw) * requestedScalar);
        float acceleration = (NativeAction == 0x10 ? 44f : 20f) * (1f / 3600f);
        float deceleration = (NativeAction == 0x10 ? 50f : 11f) * (1f / 3600f);
        if (LandingUpdates != 1)
        {
            float target = LandingTargetMaximum(NativeAction) * input.ConditionedMagnitude;
            float amount = Scalar < target ? acceleration : deceleration;
            Scalar += Math.Clamp(target - Scalar, -amount, amount);
        }
        float carryLength = Carry.Magnitude;
        if (carryLength > 0.001f)
        {
            float nextLength = MathF.Max(0, carryLength - deceleration);
            float scale = nextLength / carryLength;
            Carry = new(Carry.X * scale, Carry.Y * scale);
            request = new(request.X + Carry.X, request.Y + Carry.Y);
        }
        else
            Carry = default;

        LandingUpdates = checked(LandingUpdates + 1);
        Handoff? handoff = SelectHandoff(input);
        var result = new StepResult(request, Scalar, Carry, LandingUpdates, handoff);
        Finished = handoff.HasValue;
        return result;
    }

    private Handoff? SelectHandoff(StepInput input)
    {
        if (NativeAction == 0x0a)
            return new(3, Scale(input.ContactMotion, HeliStopCarryScale), Scalar, 5, 0, -2);
        if (LandingUpdates <= 1)
            return null;
        if (input.CrouchHeld)
        {
            double planarLength = Math.Sqrt(input.ContactMotion.X * input.ContactMotion.X +
                input.ContactMotion.Y * input.ContactMotion.Y);
            float scalar = MathF.Min((float)planarLength, CrouchCarryLimit);
            var carry = Limit(input.ContactMotion, CrouchCarryLimit);
            return new(4, carry, scalar, 13, 2, -2);
        }
        if (input.ConditionedMagnitude > 0.3f && !input.ControlLocked)
            return new(2, input.ContactMotion, Scalar, 4, null, LandingUpdates > 5 ? 14 : 8);
        if (NativeAction == 0x10 || input.ForwardRequestStep > 3f * (1f / 60f) && LandingUpdates > 3)
        {
            var carry = Limit(input.ContactMotion, StopCarryLimit);
            return new(3, carry, Scalar, 5, 0, -1);
        }
        if (input.NativeAnimationExitRequested)
            return new(0, input.ContactMotion, Scalar, 0, 0, -2);
        return LandingUpdates > 12 ? new Handoff(0, input.ContactMotion, Scalar, null, null, 0) : null;
    }

    /// <summary>
    /// Ground actions0/3 clear controlled request and add the independently
    /// braked contact carry (0x217e0c..0x217fc8). Native P+0x16c supplies the
    /// stop-action slope term; a flat ordinary surface supplies zero.
    /// </summary>
    public static Rac1AirbornePlanarMotion.PlanarStep BrakeGroundCarry(
        int nativeGroundAction, Rac1AirbornePlanarMotion.PlanarStep carry, float nativeSlopeTerm = 0)
    {
        Validate(carry.X);
        Validate(carry.Y);
        Validate(nativeSlopeTerm);
        if (nativeGroundAction is not (0 or 3) || nativeSlopeTerm < -1 || nativeSlopeTerm > 1)
            throw new ArgumentOutOfRangeException(nameof(nativeGroundAction));
        float length = carry.Magnitude;
        if (length <= 0.001f)
            return default;
        float amount = nativeGroundAction == 3
            ? StopCarryDeceleration * MathF.Min(1f + nativeSlopeTerm, 1f) : IdleCarryDeceleration;
        float scale = MathF.Max(0, length - amount) / length;
        return new(carry.X * scale, carry.Y * scale);
    }

    private static float LandingTargetMaximum(int nativeAction) => nativeAction switch
    {
        0x10 or 0x11 => 5.7f * (1f / 60f),
        _ => Rac1AirbornePlanarMotion.MaximumTargetStep(nativeAction),
    };

    private static Rac1NativeVector3 Scale(Rac1NativeVector3 value, double amount) =>
        new(value.X * amount, value.Y * amount, value.Z * amount);

    private static Rac1NativeVector3 Limit(Rac1NativeVector3 value, double maximum) =>
        value.Length > maximum ? Scale(value, maximum / value.Length) : value;

    private static void ValidateAction(int nativeAction)
    {
        if (nativeAction is not (7 or 9 or 0x0a or 0x0b or 0x0d or 0x0e or 0x0f or 0x10 or 0x11 or 0x1c or 0x69))
            throw new ArgumentOutOfRangeException(nameof(nativeAction));
    }

    private static void Validate(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}

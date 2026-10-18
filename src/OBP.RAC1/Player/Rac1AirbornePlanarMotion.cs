namespace OBP.RAC1.Player;

/// <summary>
/// Regular-axis planar laws from 0x213e68..0x214108 and the action-specific
/// branches at 0x214658..0x214eb8. External support carry, ice flags and the
/// contact-dependent 2.1-speed override are not part of this bounded adapter.
/// </summary>
public static class Rac1AirbornePlanarMotion
{
    public readonly record struct PlanarStep(float X, float Y)
    {
        public float Magnitude => MathF.Sqrt((X * X) + (Y * Y));
    }

    public static float MaximumTargetStep(int nativeAction) => nativeAction switch
    {
        7 or 9 or 0x0a or 0x0b or 0x1c or 0x69 => 5.7f * (1f / 60f),
        0x0d => 1.9f * (1f / 60f),
        0x0e => 3.5f * (1f / 60f),
        0x0f => 3.3f * (1f / 60f),
        _ => throw new ArgumentOutOfRangeException(nameof(nativeAction)),
    };

    /// <summary>
    /// General convergence, after the caller has resolved the desired native
    /// target vector and base amount. Opposed input gains up to 3 times the
    /// amount; either zero vector uses 1.5. Vertical displacement is untouched.
    /// This proves why a single fixed acceleration cannot cover every turn.
    /// </summary>
    public static PlanarStep Converge(PlanarStep current, PlanarStep target, float baseAmount)
    {
        Validate(current.X, nameof(current));
        Validate(current.Y, nameof(current));
        Validate(target.X, nameof(target));
        Validate(target.Y, nameof(target));
        Validate(baseAmount, nameof(baseAmount));
        if (baseAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(baseAmount));
        float targetLength = target.Magnitude;
        float currentLength = current.Magnitude;
        float negativeCosine = 0;
        if (targetLength != 0 && currentLength != 0)
        {
            float dot = (current.X * target.X) + (current.Y * target.Y);
            negativeCosine = (-dot / targetLength) / currentLength;
        }
        float gain = Math.Clamp((negativeCosine + 1f) * 1.5f, 1f, 3f);
        float amount = baseAmount * gain;
        float dx = target.X - current.X;
        float dy = target.Y - current.Y;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        if (amount < distance)
        {
            float scale = amount / distance;
            dx *= scale;
            dy *= scale;
        }
        return new(current.X + dx, current.Y + dy);
    }

    /// <summary>
    /// The ordinary branch's base increment. P+0x30a is the native wall-contact
    /// counter. It is not an arbitrary Godot slide-contact count.
    /// </summary>
    public static float OrdinaryBaseAmount(float targetScalar, bool nativeWallContact)
    {
        Validate(targetScalar, nameof(targetScalar));
        if (targetScalar < 0)
            throw new ArgumentOutOfRangeException(nameof(targetScalar));
        float slowThreshold = (5.7f * (1f / 60f)) * 0.2f;
        return targetScalar < slowThreshold
            ? (nativeWallContact ? 6f : 2f) * (1f / 3600f)
            : 20f * (1f / 3600f);
    }

    /// <summary>
    /// Thruster-Pack stretch jump targets an entry-clearance-dependent speed,
    /// then changes to 3.5 after native age 54. Heading remains action-owned.
    /// The root host must not turn this into a generic air-control vector.
    /// </summary>
    public static PlanarStep StepThrusterLong(
        float currentScalar, int nativeActionAge, float groundClearanceAtEntry, float nativeFacingYaw)
    {
        Validate(currentScalar, nameof(currentScalar));
        Validate(groundClearanceAtEntry, nameof(groundClearanceAtEntry));
        Validate(nativeFacingYaw, nameof(nativeFacingYaw));
        if (currentScalar < 0 || nativeActionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(currentScalar));
        float target = nativeActionAge < 55
            ? (groundClearanceAtEntry > 1.4f ? 5.7f : 11.5f) * (1f / 60f)
            : 3.5f * (1f / 60f);
        float amount = (currentScalar < target ? 44f : 45f) * (1f / 3600f);
        float scalar = currentScalar + Math.Clamp(target - currentScalar, -amount, amount);
        return new(MathF.Cos(nativeFacingYaw) * scalar, MathF.Sin(nativeFacingYaw) * scalar);
    }

    private static void Validate(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}

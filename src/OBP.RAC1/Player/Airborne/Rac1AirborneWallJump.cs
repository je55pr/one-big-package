namespace OBP.RAC1.Player;

/// <summary>
/// Regular-axis action 0x11 planar ownership at 0x214b04..0x214c40. Its target
/// is the cached outward wall-normal heading, not live analogue steering.
/// Native body yaw smoothing is separate from this movement request.
/// </summary>
public static class Rac1AirborneWallJump
{
    public const float OutwardStep = 4.9f * (1f / 60f);
    public const int LaunchAge = 9;
    private const float FacingErrorGain = 0.042f;
    private const float FacingVelocityDamping = 0.2f;
    private const float FacingMaximumStep = 11.868238f * (1f / 60f);

    public readonly record struct FacingStep(float Yaw, float Velocity);

    /// <summary>
    /// 0x214b30 calls 0x211e30 with direction -1 strictly after age 9.
    /// The regular-axis 0x25bb38/0x25bc98 helpers retain that directed turn,
    /// damp/cap its velocity and snap within one percent of the turn cap.
    /// This is independent of analogue input and the outward launch request.
    /// </summary>
    public static FacingStep StepFacing(float yaw, float velocity, float cachedWallNormalYaw)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(velocity) || !float.IsFinite(cachedWallNormalYaw))
            throw new ArgumentOutOfRangeException(nameof(yaw));
        float error = DirectedError(cachedWallNormalYaw, yaw);
        velocity += FacingErrorGain * error - FacingVelocityDamping * velocity;
        velocity = Math.Clamp(velocity, -FacingMaximumStep, FacingMaximumStep);
        // 0x25b798 caps both signs by absolute error. Direction -1 selects
        // the target route; it does not discard inherited positive yaw speed.
        velocity = Math.Clamp(velocity, -MathF.Abs(error), MathF.Abs(error));
        yaw = Wrap(yaw + velocity);
        return MathF.Abs(DirectedError(cachedWallNormalYaw, yaw)) < FacingMaximumStep * 0.01f
            ? new(cachedWallNormalYaw, 0) : new(yaw, velocity);
    }

    private static float DirectedError(float target, float yaw)
    {
        float error = Wrap(target - yaw);
        if (error < 0) return error;
        return MathF.Abs(error) <= 0.0017453292f ? 0 : error - MathF.Tau;
    }

    private static float Wrap(float yaw)
    {
        yaw %= MathF.Tau;
        if (yaw > MathF.PI) yaw -= MathF.Tau;
        if (yaw < -MathF.PI) yaw += MathF.Tau;
        return yaw;
    }

    public static Rac1AirbornePlanarMotion.PlanarStep Step(
        int nativeActionAge, float cachedWallNormalYaw,
        Rac1AirbornePlanarMotion.PlanarStep inheritedRequest)
    {
        if (nativeActionAge < 0 || !float.IsFinite(cachedWallNormalYaw) ||
            !float.IsFinite(inheritedRequest.X) || !float.IsFinite(inheritedRequest.Y))
            throw new ArgumentOutOfRangeException(nameof(nativeActionAge));
        return nativeActionAge < LaunchAge ? inheritedRequest :
            new(MathF.Cos(cachedWallNormalYaw) * OutwardStep, MathF.Sin(cachedWallNormalYaw) * OutwardStep);
    }
}

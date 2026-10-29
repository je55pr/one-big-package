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

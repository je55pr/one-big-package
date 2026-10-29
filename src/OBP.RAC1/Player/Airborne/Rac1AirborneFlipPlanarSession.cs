namespace OBP.RAC1.Player;

/// <summary>
/// Directional-flip planar ownership at 0x214958..0x214b04. Entry keeps both
/// the new input heading and a ten-update-old input heading; replacing either
/// with the presented body heading changes the recovered trajectory.
/// </summary>
public sealed class Rac1AirborneFlipPlanarSession
{
    private readonly float _entryInputYaw;
    private readonly float _historicalInputYaw;
    private readonly float _inheritedPlanarScalar;
    private readonly float _controlledScalar;
    private float _directionalScalar;

    public Rac1AirborneFlipPlanarSession(
        float entryInputYaw, float tenUpdatesEarlierInputYaw,
        float contactPlanarScalar, int flipDirection)
    {
        if (!float.IsFinite(entryInputYaw) || !float.IsFinite(tenUpdatesEarlierInputYaw) ||
            !float.IsFinite(contactPlanarScalar) || contactPlanarScalar < 0 || flipDirection is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(contactPlanarScalar));
        _entryInputYaw = entryInputYaw;
        FlipDirection = flipDirection;
        _historicalInputYaw = tenUpdatesEarlierInputYaw;
        _inheritedPlanarScalar = contactPlanarScalar <= 1.5f * (1f / 60f)
            ? 0 : contactPlanarScalar;
        // Entry P+0x454 is retained for request projection even for back flips.
        // Motion writes zero to P+0x194 for back, otherwise copies P+0x454.
        _controlledScalar = flipDirection == 3 ? 0 : _inheritedPlanarScalar;
    }

    public float FacingTargetYaw => _historicalInputYaw;
    public int FlipDirection { get; }
    public float ControlledScalar => _controlledScalar;

    public Rac1AirbornePlanarMotion.PlanarStep Step(bool hasApexed, int predictedLandingTicks)
    {
        if (predictedLandingTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(predictedLandingTicks));
        float target = hasApexed && predictedLandingTicks < 12 ? 0 : 4.2f * (1f / 60f);
        float amount = 8f * (1f / 3600f);
        _directionalScalar += Math.Clamp(target - _directionalScalar, -amount, amount);
        // 0x214a84 calls absolute wrapped angular distance, followed by SIN
        // at 0x214a8c. A cosine projection reverses aligned/side inheritance.
        float angle = MathF.Abs(_historicalInputYaw - _entryInputYaw);
        if (angle >= Rac1AirborneAdmission.NativePi)
            angle = (Rac1AirborneAdmission.NativePi + Rac1AirborneAdmission.NativePi) - angle;
        float inheritedProjection = MathF.Sin(angle) * _inheritedPlanarScalar;
        float x = (MathF.Cos(_entryInputYaw) * _directionalScalar) +
            (MathF.Cos(_historicalInputYaw) * inheritedProjection);
        float y = (MathF.Sin(_entryInputYaw) * _directionalScalar) +
            (MathF.Sin(_historicalInputYaw) * inheritedProjection);
        return new(x, y);
    }
}

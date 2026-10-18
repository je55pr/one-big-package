namespace OBP.RAC1.Player;

/// <summary>
/// Native ordinary Ratchet contact-query parameters. These describe the inputs
/// to the retail radial/axial solver, not a Godot capsule or a claim that its
/// collision primitive has been reproduced.
/// </summary>
public readonly record struct Rac1CollisionProfile(
    float HorizontalRadius,
    float UpperContactHeight,
    float LowerContactHeight)
{
    public const float OrdinaryHorizontalRadius = 0.45f;
    public const float OrdinaryUpperContactHeight = 0.8f;
    public const float OrdinaryLowerContactHeight = 0.7f;
    public const float MinimumAxialQuerySpan = 0.05f;
    public const float ContactHeightApproachPerUpdate = 0.02f;
    public const int MaximumNativeCorrectionPasses = 8;

    public static Rac1CollisionProfile Ordinary { get; } = new(
        OrdinaryHorizontalRadius,
        OrdinaryUpperContactHeight,
        OrdinaryLowerContactHeight);

    /// <summary>
    /// 0x213548..0x213580 passes max(P+0x220 - P+0x224, .05) to
    /// 0x1f19a0 after moving the query origin up by P+0x224.
    /// </summary>
    public float AxialQuerySpan =>
        Math.Max(UpperContactHeight - LowerContactHeight, MinimumAxialQuerySpan);

    /// <summary>
    /// 0x2115cc..0x211610 approaches both active heights by .02 times the
    /// native update scalar. Ordinary 60 Hz updates have scalar one.
    /// </summary>
    public Rac1CollisionProfile ApproachHeights(float upperTarget, float lowerTarget)
    {
        if (!float.IsFinite(upperTarget) || !float.IsFinite(lowerTarget))
            throw new ArgumentOutOfRangeException(nameof(upperTarget));

        return this with
        {
            UpperContactHeight = Approach(UpperContactHeight, upperTarget),
            LowerContactHeight = Approach(LowerContactHeight, lowerTarget),
        };
    }

    private static float Approach(float value, float target) =>
        value + Math.Clamp(target - value,
            -ContactHeightApproachPerUpdate,
            ContactHeightApproachPerUpdate);
}

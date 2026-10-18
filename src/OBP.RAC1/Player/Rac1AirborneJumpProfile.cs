namespace OBP.RAC1.Player;

/// <summary>Native action identities, not a guessed mapping of physical buttons.</summary>
public enum Rac1AirborneJumpKind
{
    Stationary = 0x07,
    Moving = 0x09,
    HeliPackLong = 0x0a,
    DirectionalFlip = 0x0b,
    ThrusterPackHigh = 0x0d,
    HeliPackHigh = 0x0f,
    ThrusterPackLong = 0x10,
    WallJump = 0x11,
    LedgeJump = 0x1c,
    MudEscape = 0x69,
}

/// <summary>
/// Entry data recovered from 0x224590..0x225788. Launch height is the impulse
/// parameter sqrt(2*h*g), not a guarantee about world-space apex after contact.
/// Timings describe native action age, whose adapter ordering remains explicit.
/// </summary>
public sealed record Rac1AirborneJumpProfile(
    Rac1AirborneJumpKind Kind,
    float MinimumHeight,
    float MaximumHeight,
    int LaunchAge,
    int HeightGrowthTicks,
    float GravityCoefficient,
    int EntrySequence,
    int EntryFrame,
    int EntryBlendTicks,
    int GlideAdmissionAge,
    int ScriptStartAge = 0,
    int ScriptEndAge = 0,
    float EnvironmentDepth = 0)
{
    public const float SecondsPerUpdate = 1f / 60f;
    public const float SquaredSecondsPerUpdate = 1f / 3600f;
    public float GravityPerUpdate => GravityCoefficient * SquaredSecondsPerUpdate;

    /// <summary>
    /// FastEntry means the actual native predecessor predicate: previous
    /// domain 4 for a flip; for Thruster-Pack long jump, previous domain 4 or
    /// previous crouch action 4 aged less than 7. It is not a current button.
    /// FlipDirection is the result
    /// of 0x221df8 (0 right, 1 left, 2 forward, 3 back).
    /// AlternateHeliTiming represents the nonzero boot timing selector tested
    /// at 0x225524, not a frame-rate-dependent host adjustment.
    /// </summary>
    public static Rac1AirborneJumpProfile Create(
        Rac1AirborneJumpKind kind, bool fastEntry = false,
        int flipDirection = 2, bool alternateHeliTiming = false, float environmentDepth = 0)
    {
        if (flipDirection is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(flipDirection));
        if (!float.IsFinite(environmentDepth) || environmentDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(environmentDepth));
        return kind switch
        {
            Rac1AirborneJumpKind.Stationary => new(kind, 1.47f, 2.62f, 5, 15, 29.7f, 7, 0, 5, 30),
            Rac1AirborneJumpKind.Moving => new(kind, 1f, 2.7f, 5, 15, 29.7f, 8, 0, 5, 30),
            Rac1AirborneJumpKind.HeliPackLong => new(kind, 1.9f, 1.95f, 5, 14, 11f, 18, 0, 5, 0),
            Rac1AirborneJumpKind.DirectionalFlip => new(kind, 3.2f, 3.25f,
                fastEntry ? 1 : 5, 14, 29.7f,
                28 + (flipDirection == 2 ? 0 : flipDirection),
                fastEntry ? 1 : 0, fastEntry ? 4 : 5, 30),
            Rac1AirborneJumpKind.ThrusterPackHigh => new(kind, 0.1f, 0.2f, 9, 14, 29.7f,
                17, 0, 5, 0, 9, 44),
            Rac1AirborneJumpKind.HeliPackHigh => new(kind, 1.9f, 1.95f, 9, 5, 29.7f,
                21, 0, 5, 85, alternateHeliTiming ? 34 : 35, alternateHeliTiming ? 70 : 71),
            Rac1AirborneJumpKind.ThrusterPackLong => new(kind, 0.4f, 0.9f,
                fastEntry ? 4 : 1, 1, 8.5f, 38, 0, fastEntry ? 8 : 5, 0),
            Rac1AirborneJumpKind.WallJump => new(kind, 3.3f, 3.35f, 9, 12, 29f, 35, 0, 7, 0),
            Rac1AirborneJumpKind.LedgeJump => new(kind, 2f, 2.05f, 10, 1, 25f, 34, 0, 4, 1),
            Rac1AirborneJumpKind.MudEscape => new(kind, environmentDepth + 1.7f,
                environmentDepth + 1.75f, 5, 19, 29.7f, 7, 0, 5, 0, EnvironmentDepth: environmentDepth),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

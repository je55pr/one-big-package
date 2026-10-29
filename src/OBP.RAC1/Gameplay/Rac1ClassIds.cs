namespace OBP.RAC1.Gameplay;

// These IDs must remain compile-time constants for the C# constant patterns
// used by native damage, replay admission and class-family checks.
#pragma warning disable S2339 // Public constants are intentional; static properties cannot be used in patterns.
/// <summary>
/// Known native Moby class identities for R&C1 only. Do not reuse these IDs
/// for other source games, where an identical number can name another object.
/// Unknown or provisional classes are intentionally not catalogued here.
/// </summary>
public static class Rac1ClassIds
{
    public const int WoodenCrate = 500;
    public const int NanotechCrate = 501;
    public const int MetalCrate = 502;
    public const int AmmoCrate = 511;
    public const int VeldinToad = 749;
    public const int VeldinFlameRobot = 1440;
}
#pragma warning restore S2339

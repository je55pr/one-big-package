namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed event predicate for the Going Commando crate-family state machine.
/// Loaded Oozla code queries one owned event with mask 0x05830001, rejects the
/// exact 0x01000000 event, and class 500 enters its break transition when the
/// event scalar at +0x2C is greater than zero.
/// </summary>
public static class GcCrateInteraction
{
    public const uint QueriedEventMask = 0x05830001;
    public const uint IgnoredExactEventFlags = 0x01000000;
    public const byte BreakTransitionState = 3;

    public static bool ShouldBreakClass500(uint eventFlags, float eventScalar)
    {
        return (eventFlags & QueriedEventMask) != 0
            && eventFlags != IgnoredExactEventFlags
            && eventScalar > 0f;
    }

    /// <summary>State-3 route used by normal class 500 after the break helper.</summary>
    public static GcClass500PostBreakRoute PostBreakRoute(byte authoredPvarC8) =>
        authoredPvarC8 == 0 ? GcClass500PostBreakRoute.Deactivate : GcClass500PostBreakRoute.State6;
}

public enum GcClass500PostBreakRoute
{
    Deactivate,
    State6,
}

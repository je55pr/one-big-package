namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed collision-damage predicate for the Going Commando crate-family state machine.
/// Loaded Oozla code queries one owned damage record with mask 0x05830001, rejects
/// exact damageFlags 0x01000000, and class 500 enters its break transition when
/// damageHp at +0x2C is greater than zero.
/// </summary>
public static class GcCrateInteraction
{
    public const uint QueriedDamageMask = 0x05830001;
    public const uint IgnoredExactDamageFlags = 0x01000000;

    // Compatibility names retained for the existing host/debug harness.
    public const uint QueriedEventMask = QueriedDamageMask;
    public const uint IgnoredExactEventFlags = IgnoredExactDamageFlags;
    public const byte BreakTransitionState = 3;

    public static bool ShouldBreakClass500(uint damageFlags, float damageHp)
    {
        return (damageFlags & QueriedDamageMask) != 0
            && damageFlags != IgnoredExactDamageFlags
            && damageHp > 0f;
    }

    public static bool ShouldBreakClass500(in GcCollisionDamage damage) =>
        ShouldBreakClass500(damage.DamageFlags, damage.DamageHp);

    /// <summary>State-3 route used by normal class 500 after the break helper.</summary>
    public static GcClass500PostBreakRoute PostBreakRoute(byte authoredPvarC8) =>
        authoredPvarC8 == 0 ? GcClass500PostBreakRoute.Deactivate : GcClass500PostBreakRoute.State6;
}

public enum GcClass500PostBreakRoute
{
    Deactivate,
    State6,
}

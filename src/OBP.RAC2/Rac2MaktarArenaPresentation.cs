namespace OBP.RAC2;

/// <summary>
/// Explicit source-owned admission boundary for Maktar's unhosted arena
/// dispensers. Their native class and six placements remain in LEVEL2's
/// gameplay lump; no replacement model or non-native collision is generated.
/// </summary>
public static class Rac2MaktarArenaPresentation
{
    public const int NativeLevel = 2;
    public const int DispenserClass = 2581;

    /// <summary>
    /// Class 2581 has five joints and four source animations. Importing all
    /// instances as permanently posed, welded STATIC Mobies draws their
    /// articulated launcher arms across the arena. Until a native stateful
    /// dispenser actor owns pose/visibility/spawning, omit that class from
    /// static presentation only. The original source identity is preserved.
    /// All other native classes and levels retain normal presentation.
    /// </summary>
    public static bool AdmitStaticMoby(int level, int nativeClass) =>
        level != NativeLevel || nativeClass != DispenserClass;
}

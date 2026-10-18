namespace OBP.RAC1.Player;

/// <summary>
/// Bounded source-owned water vocabulary and oxygen counter. It does not infer
/// a water volume from a floor triangle, or replace the native drowning guards.
/// </summary>
public static class Rac1TraversalEquipmentWater
{
    public const int UnderwaterDomain = 0x11;
    public const int SurfaceSwimDomain = 0x12;
    public const int UnderwaterMovingAction = 0x33;
    public const int UnderwaterIdleAction = 0x34;
    public const int HydroPackAction = 0x35;
    public const int SurfaceMovingAction = 0x36;
    public const int SurfaceIdleAction = 0x37;
    public const int WadeAction = 0x72;
    public const int MaximumOxygen = 10_000;
    public const int OxygenConsumptionPerUpdate = MaximumOxygen / (15 * 60);

    /// <summary>
    /// 0x227778..0x2278e4: oxygen-mask ownership suppresses consumption;
    /// leaving domain 0x11 restores the counter except while drowning/dead.
    /// Exhaustion by itself is not the complete native death predicate.
    /// </summary>
    public static int StepOxygen(int oxygen, int nativeDomain, int nativeAction, bool ownsOxygenMask)
    {
        if (oxygen is < 0 or > MaximumOxygen)
            throw new ArgumentOutOfRangeException(nameof(oxygen));
        if (nativeDomain == UnderwaterDomain)
            return ownsOxygenMask ? oxygen : Math.Max(0, oxygen - OxygenConsumptionPerUpdate);
        return nativeAction is 0x6a or 0x76 ? oxygen : MaximumOxygen;
    }
}

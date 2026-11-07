namespace OBP.RAC2.Gameplay;

/// <summary>Oozla 0x392550..0x392638: recent fragment pressure decays by elapsed ticks / 4 and resets after 240 ticks.</summary>
public sealed class Rac2CrateBreakBudget
{
    public int Recent { get; private set; }
    private long _lastTick;
    public Rac2CrateBreakBudget Copy() => new() { Recent = Recent, _lastTick = _lastTick };
    public int Begin(long tick, int freeSlots, int nativeClass = 500)
    {
        long elapsed = Math.Max(0, tick - _lastTick);
        Recent = elapsed > 240 ? 0 : Math.Max(0, Recent - (int)(elapsed >> 2));
        _lastTick = tick;
        int pieces = freeSlots > 250 && Recent < 5 ? 4 : freeSlots > 200 && Recent < 10 ? 3 :
            freeSlots > 150 && Recent < 15 ? 2 : freeSlots > 100 && Recent < 20 ? 1 : 0;
        Recent += pieces;
        // Class 505 takes no detachable-part branch at 0x39263C..0x392670.
        if (nativeClass == Rac2ExplosiveCrateSession.NativeClass) return 0;
        // 0x3926A8..C0: Nanotech has contiguous parts, unlike wood/ammo.
        if (Rac2CrateSupply.IsNanotech(nativeClass)) return pieces > 0 ? (1 << (pieces - 1)) - 1 : 0;
        return pieces >= 3 ? 0x25 : pieces == 2 ? 5 : pieces == 1 ? 1 : 0;
    }
}

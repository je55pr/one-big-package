namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source class-1012 Kerwan HUD selectors (PVar +0x98 / +0x9c).
/// Native lv3_2dcbc8 maps 1..4 to 0xbce, 0xbcf, 0xbcb, 0xbcd.
/// The updater flips the active route side before selecting the HUD slot.
/// </summary>
public readonly record struct Rac1TaxiNativeHud(int SideZeroSelector, int SideOneSelector)
{
    public int? MessageForCurrentSide(int currentSide)
    {
        if (currentSide is not (0 or 1)) return null;
        int selector = currentSide == 0 ? SideOneSelector : SideZeroSelector;
        return selector switch
        {
            1 => 0x0bce,
            2 => 0x0bcf,
            3 => 0x0bcb,
            4 => 0x0bcd,
            _ => null,
        };
    }
}

namespace OBP.RAC1.Presentation;

/// <summary>
/// Engine-neutral timing/geometry from native FUN_00223e28. Presentation
/// only; no save state, world input, or authored gameplay admission.
/// </summary>
public static class Rac1RetailPanelBlend
{
    // GS ALPHA=(Cs-Cd)*As/128+Cd for 0x44, with the CLUT's
    // 0..128 alpha expanded to 0..255 before rendering on Godot.
    // Original NTSC-U ELF SCUS_971.99 at virtual address 0x001601B0:
    // RGBAQ packet 0x80100808. The alpha byte 0x80 is fully opaque
    // under GS (Cs-Cd)*As/128+Cd, not an 0x80/255 half-transparent wash.
    // FUN_002196b8 draws it before the 0xE99E HUD sprite/effect stack.
    public const uint NativePanelClearRgba = 0x80100808;
    public const byte ClearRed = 0x08;
    public const byte ClearGreen = 0x08;
    public const byte ClearBlue = 0x10;
    public const byte ClearAlpha = 0x80;

    public const float StreakRgbScale = 96f / 128f;
    public const float StreakAlphaScale = 80f / 128f;

    public static int InnerSizeAdjustment(int projectedExtent) =>
        projectedExtent >= 0x97 ? 0 : projectedExtent >= 0x4c ? -1 : -2;

    public readonly record struct Flash(int Time, bool Active)
    {
        // The native effect update advances by 2 each game tick and
        // shuts off after 0x100. Its idle trigger is 1/2000 each tick.
        public Flash Step(bool randomTrigger)
        {
            if (!Active) return randomTrigger ? new Flash(0, true) : this;
            int next = Time + 2;
            return new Flash(next, next < 0x100);
        }

        // GS ALPHA 0x68 uses FIX, not the source texture alpha:
        // (Cs-0)*FIX/128+Cd; with 0x80 max after doubling.
        public float AdditiveGain => !Active ? 0f :
            Math.Clamp(2 * (128 - Math.Clamp(Time - 128, 0, 128)), 0, 128) / 128f;
    }
}

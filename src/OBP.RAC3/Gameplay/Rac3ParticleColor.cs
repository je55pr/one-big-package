namespace OBP.RAC3.Gameplay;

/// <summary>Native particle colour packing: low 24 bits are RGB, the top byte is opacity.</summary>
internal static class Rac3ParticleColor
{
    private const uint RgbMask = 0x00ffffff;
    private const int AlphaShift = 24;

    /// <summary>Combines the RGB bits of <paramref name="rgb"/> with <paramref name="alpha"/> in the top byte.</summary>
    internal static uint Pack(uint rgb, uint alpha) => rgb & RgbMask | alpha << AlphaShift;

    /// <summary>Opacity byte of a packed colour.</summary>
    internal static uint Alpha(uint color) => color >> AlphaShift;
}

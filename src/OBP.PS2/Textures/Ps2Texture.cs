namespace OBP.PS2.Textures;

/// <summary>
/// PS2 8-bit paletted texture decode for the R&amp;C games. RC2/RC3 pixels are
/// linear; only the CLUT is fixed up: a GS reorder (swap index bits 3 and 4 when
/// they differ) and an alpha scale (0..128 → 0..255). Translated from
/// <c>reference-ts/packages/ps2-texture</c>.
/// </summary>
public static class Ps2Texture
{
    public sealed record Paletted8(int Width, int Height, ReadOnlyMemory<byte> Pixels, uint[] Palette);

    public sealed record Decoded(int Width, int Height, byte[] Rgba);

    public static int MapPaletteIndex(int index) =>
        ((index & 16) >> 1) != (index & 8) ? index ^ 0b00011000 : index;

    public static uint[] SwizzlePalette(uint[] palette)
    {
        var outp = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            outp[i] = palette[MapPaletteIndex(i)];
        }

        return outp;
    }

    public static void MultiplyAlphas(uint[] palette)
    {
        for (int i = 0; i < palette.Length; i++)
        {
            uint c = palette[i];
            uint a = (c >> 24) & 0xff;
            a = a < 0x80 ? a * 2 : 255;
            palette[i] = (c & 0x00ffffff) | (a << 24);
        }
    }

    public static Decoded DecodePaletted8(Paletted8 texture, bool reorderPalette = true, bool scaleAlpha = true)
    {
        int w = texture.Width, h = texture.Height;
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(texture), $"Invalid PS2 texture size {w}x{h}.");
        }

        var pixels = texture.Pixels.Span;
        if (pixels.Length < w * h)
        {
            throw new InvalidDataException($"PS2 texture pixel buffer is {pixels.Length} bytes, need {w * h}.");
        }

        var palette = new uint[256];
        Array.Copy(texture.Palette, palette, System.Math.Min(256, texture.Palette.Length));
        if (scaleAlpha)
        {
            MultiplyAlphas(palette);
        }

        if (reorderPalette)
        {
            palette = SwizzlePalette(palette);
        }

        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            uint c = palette[pixels[i]];
            rgba[i * 4] = (byte)(c & 0xff);
            rgba[i * 4 + 1] = (byte)((c >> 8) & 0xff);
            rgba[i * 4 + 2] = (byte)((c >> 16) & 0xff);
            rgba[i * 4 + 3] = (byte)((c >> 24) & 0xff);
        }

        return new Decoded(w, h, rgba);
    }
}

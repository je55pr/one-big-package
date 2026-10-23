namespace OBP.RAC1.Presentation;

/// <summary>
/// Source font_print (FUN_001f62b0) placement for the single-item large-font
/// Veldin pause root. FUN_L00_00278e10 centers the measured glyph advance,
/// places the text baseline at half the panel height minus eight, and draws
/// each 16x16 glyph at its source y offset. This is native pixel geometry;
/// output-target scaling belongs to the Godot presentation adapter.
/// </summary>
public static class Rac1MenuRootTextLayout
{
    public const int GlyphCellSize = 16;
    public const int NativeLineSize = 14;

    public readonly record struct GlyphQuad(byte Code, int X, int Y, byte U, byte V);
    public sealed record Result(int Left, int AnchorY, int MeasuredWidth,
        IReadOnlyList<GlyphQuad> Quads);

    public static Result Layout(ReadOnlySpan<byte> text,
        IReadOnlyList<Rac1BitmapFont.Glyph> font,
        int panelWidth, int panelHeight)
    {
        if (panelWidth <= 0 || panelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(panelWidth),
                "Native menu text requires positive panel dimensions.");
        if (font.Count < Rac1BitmapFont.GlyphCount)
            throw new InvalidDataException("Native menu font table is incomplete.");

        // FUN_001f6200 accumulates signed 8-bit glyph advances, including
        // spaces; text-list menus use localized zero-terminated bytes.
        int width = 0;
        foreach (byte code in text)
        {
            if (code == 0) break;
            // The root's supported localized labels contain printable
            // ASCII; non-ASCII/control drawing uses special native glyph
            // palette and accent overlays, not this single-pass adapter.
            if (code < 0x20 || code >= 0x80)
                throw new InvalidDataException("Unsupported source root text control or accented glyph.");
            width = checked(width + font[code].Advance);
        }

        int left = (panelWidth >> 1) - (width >> 1);
        // One entry: row height = panel height / (1 + 1);
        // source font parameter 14, and callback's final minus one.
        int anchorY = panelHeight / 2 - NativeLineSize / 2 - 1;
        var quads = new List<GlyphQuad>(text.Length);
        int pen = left;
        foreach (byte code in text)
        {
            if (code == 0) break;
            var glyph = font[code];
            if (code > ' ' && glyph.Advance != 0)
                quads.Add(new GlyphQuad(code, pen, anchorY + glyph.Top, glyph.U, glyph.V));
            pen = checked(pen + glyph.Advance);
        }
        return new Result(left, anchorY, width, quads);
    }
}

using Godot;
using OBP.IO;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

/// <summary>
/// Godot resources generated in memory when the player's R&C1 ISO is attached.
/// Native decoding belongs to OBP.RAC1; no exported/imported retail files or
/// external tools are required, and detaching the source clears this cache.
/// </summary>
public static class Rac1RetailUiAssets
{
    private static readonly Dictionary<string, FontFile> Fonts = new();
    private static readonly Dictionary<(ushort, int), Texture2D?> Sprites = new();
    private static Rac1UiAssets? _assets;
    public static bool IsLoaded => _assets is not null;

    public static void Load(IRandomAccessReader disc)
    {
        var assets = Rac1UiAssets.Read(disc);
        var fonts = assets.Fonts.ToDictionary(pair => pair.Key, pair => CreateFont(pair.Value));
        Clear();
        _assets = assets;
        foreach (var pair in fonts) Fonts.Add(pair.Key, pair.Value);
        GD.Print($"[retail-ui] generated from R&C1 disc: {assets.Hud.FrameCount} sprite frames, {Fonts.Count} bitmap fonts, {assets.Languages[0].Count} English strings");
    }

    public static void Clear()
    {
        _assets = null;
        Fonts.Clear();
        Sprites.Clear();
    }

    public static string Text(int nativeTextId, string fallback) =>
        nativeTextId >= 0 ? _assets?.Text((uint)nativeTextId) ?? fallback : fallback;

    public static FontFile? Font(string style = "regular")
    {
        if (style is not ("regular" or "small" or "large"))
            throw new ArgumentOutOfRangeException(nameof(style));
        return Fonts.GetValueOrDefault(style);
    }

    public static Texture2D? Sprite(ushort nativeAnimationId, int frame = 0)
    {
        var key = (nativeAnimationId, frame);
        if (Sprites.TryGetValue(key, out var existing))
            return existing;
        if (_assets?.Hud.DecodeFrame(nativeAnimationId, frame) is not { } texture) return Sprites[key] = null;
        using var image = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
        return Sprites[key] = ImageTexture.CreateFromImage(image);
    }

    private static FontFile CreateFont(Rac1BitmapFont source)
    {
        const int size = 16, baseline = 14;
        Vector2I cacheSize = new(size, 0);
        var font = new FontFile
        {
            FontName = $"R&C1 {source.Style}",
            FixedSize = size,
            FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled,
            Antialiasing = TextServer.FontAntialiasing.None,
            GenerateMipmaps = false,
            ModulateColorGlyphs = true,
            Fallbacks = new Godot.Collections.Array<Godot.Font> { ThemeDB.FallbackFont },
        };
        // Host baseline/line spacing, retaining the former BMFont adapter's
        // 16px cells, native top offsets and character advances.
        font.SetCacheAscent(0, size, baseline);
        font.SetCacheDescent(0, size, 4);
        using var image = Image.CreateFromData(source.Atlas.Width, source.Atlas.Height, false, Image.Format.Rgba8, source.Atlas.Rgba);
        font.SetTextureImage(0, cacheSize, 0, image);
        foreach (var glyph in source.Glyphs.Where(g => g.Printable))
        {
            font.SetGlyphAdvance(0, size, glyph.Code, new(glyph.Advance, 0));
            // Native space has A's UVs; it advances without drawing a cell.
            bool space = glyph.Code == ' ';
            font.SetGlyphOffset(0, cacheSize, glyph.Code, new(0, glyph.Top - baseline));
            font.SetGlyphSize(0, cacheSize, glyph.Code, space ? Vector2.Zero : new(size, size));
            font.SetGlyphUVRect(0, cacheSize, glyph.Code, space ? default : new(glyph.U, glyph.V, size, size));
            font.SetGlyphTextureIdx(0, cacheSize, glyph.Code, space ? -1 : 0);
        }
        return font;
    }
}

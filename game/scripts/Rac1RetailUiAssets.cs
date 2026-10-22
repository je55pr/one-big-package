using Godot;
using System.IO;
using System.Text.Json;

namespace OneBigPackage;

/// <summary>
/// Loads optional, user-extracted R&C1 bitmap fonts and sprite frames.
/// No retail assets are distributed with OBP. The PNG/BMFont cache is created
/// locally from the player's own NTSC-U disc with tools/rac1-ui-assets.py.
/// </summary>
public static class Rac1RetailUiAssets
{
    private const string Cache = "res://retail-ui-cache";
    private static readonly Dictionary<string, FontFile?> Fonts = new();
    private static readonly Dictionary<string, Texture2D?> Sprites = new();
    private static Dictionary<int, string>? _englishText;

    public static string Text(int nativeTextId, string fallback)
    {
        _englishText ??= LoadEnglishText();
        return _englishText.TryGetValue(nativeTextId, out string? text) ? text : fallback;
    }

    private static Dictionary<int, string> LoadEnglishText()
    {
        var result = new Dictionary<int, string>();
        string path = ProjectSettings.GlobalizePath($"{Cache}/strings-language-0.json");
        if (!File.Exists(path)) return result;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonProperty item in document.RootElement.EnumerateObject())
        {
            if (int.TryParse(item.Name, out int id) &&
                item.Value.TryGetProperty("latin1", out JsonElement value))
                result[id] = value.GetString() ?? string.Empty;
        }
        return result;
    }

    public static FontFile? Font(string style = "regular")
    {
        if (Fonts.TryGetValue(style, out var existing))
            return existing;

        if (style is not ("regular" or "small" or "large"))
            throw new ArgumentOutOfRangeException(nameof(style));

        string path = ProjectSettings.GlobalizePath($"{Cache}/fonts/{style}.fnt");
        if (!File.Exists(path))
            return Fonts[style] = null;

        var font = new FontFile();
        if (font.LoadBitmapFont(path) != Error.Ok)
        {
            GD.PushWarning($"Could not load R&C1 bitmap font from {path}");
            font.Dispose();
            return Fonts[style] = null;
        }
        return Fonts[style] = font;
    }

    public static Texture2D? Sprite(ushort nativeAnimationId, int frame = 0)
    {
        string key = $"{nativeAnimationId:x4}-{frame}";
        if (Sprites.TryGetValue(key, out var existing))
            return existing;
        string path = ProjectSettings.GlobalizePath($"{Cache}/sprites/{key}.png");
        if (!File.Exists(path))
            return Sprites[key] = null;

        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
            return Sprites[key] = null;
        return Sprites[key] = ImageTexture.CreateFromImage(image);
    }
}

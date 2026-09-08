using Godot;

namespace OneBigPackage;

/// <summary>
/// One place that builds the application-wide <see cref="Theme"/>. Installed as
/// an autoload (<see cref="UiBootstrap"/>) so every <see cref="Control"/> —
/// the source manager, world browser, HUDs and the title screen — picks up the
/// same fonts, colours and button/panel styling without each screen restyling
/// itself. Purely cosmetic; no layout anchors or sizes are decided here.
/// </summary>
public static class UiTheme
{
    // Warm amber lifted from the logo's "ONE BIG PACKAGE" hazard plate.
    public static readonly Color Accent = new(0.95f, 0.72f, 0.05f);
    public static readonly Color Text = new(0.91f, 0.92f, 0.95f);
    public static readonly Color TextDim = new(0.62f, 0.66f, 0.74f);
    public static readonly Color Panel = new(0.055f, 0.065f, 0.085f, 0.96f);
    public static readonly Color PanelEdge = new(0.20f, 0.23f, 0.30f);
    public static readonly Color Backdrop = new(0.028f, 0.035f, 0.052f);

    public static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = 15 };

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "RichTextLabel", Text);

        // Header type variations the existing screens already ask for.
        AddHeader(theme, "HeaderLarge", 30, Text);
        AddHeader(theme, "HeaderMedium", 21, Text);
        AddHeader(theme, "HeaderSmall", 16, Accent);

        StyleButtons(theme);
        StylePanels(theme);
        return theme;
    }

    private static void AddHeader(Theme theme, string variation, int size, Color color)
    {
        theme.AddType(variation);
        theme.SetTypeVariation(variation, "Label");
        theme.SetFontSize("font_size", variation, size);
        theme.SetColor("font_color", variation, color);
    }

    private static void StyleButtons(Theme theme)
    {
        StyleBoxFlat Box(Color fill, Color border) => new()
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 7,
            ContentMarginBottom = 7,
        };

        var normal = Box(new Color(0.11f, 0.13f, 0.17f), PanelEdge);
        var hover = Box(new Color(0.16f, 0.19f, 0.24f), Accent);
        var pressed = Box(new Color(0.09f, 0.10f, 0.13f), Accent);
        var disabled = Box(new Color(0.09f, 0.10f, 0.12f), new Color(0.15f, 0.17f, 0.21f));

        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        theme.SetStylebox("disabled", "Button", disabled);
        theme.SetStylebox("focus", "Button", hover);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Accent);
        theme.SetColor("font_disabled_color", "Button", TextDim);
    }

    private static void StylePanels(Theme theme)
    {
        var panel = new StyleBoxFlat
        {
            BgColor = Panel,
            BorderColor = PanelEdge,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 16,
            ContentMarginBottom = 16,
        };
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "Panel", panel);
    }
}

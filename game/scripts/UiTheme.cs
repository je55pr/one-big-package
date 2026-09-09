using Godot;

namespace OneBigPackage;

/// <summary>
/// Application-wide OBP menu theme. The palette and shapes are original OBP
/// presentation work: dark PS2-era menu surfaces, a warm package accent and
/// three lightweight trilogy identifiers. Layout remains owned by each screen.
/// </summary>
public static class UiTheme
{
    public static readonly Color Accent = new(0.96f, 0.72f, 0.06f);
    public static readonly Color Text = new(0.93f, 0.94f, 0.97f);
    public static readonly Color TextDim = new(0.64f, 0.68f, 0.76f);
    public static readonly Color Panel = new(0.052f, 0.064f, 0.09f, 0.97f);
    public static readonly Color PanelStrong = new(0.075f, 0.092f, 0.13f, 0.99f);
    public static readonly Color PanelEdge = new(0.20f, 0.24f, 0.32f);
    public static readonly Color Backdrop = new(0.022f, 0.03f, 0.05f);
    public static readonly Color Ready = new(0.48f, 0.91f, 0.62f);
    public static readonly Color Warning = new(1f, 0.62f, 0.34f);
    public static readonly Color GameOne = new(0.40f, 0.78f, 1f);
    public static readonly Color GameTwo = new(0.96f, 0.72f, 0.06f);
    public static readonly Color GameThree = new(0.96f, 0.40f, 0.42f);

    public static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = 16 };

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "RichTextLabel", Text);
        theme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.55f));
        theme.SetConstant("shadow_offset_x", "Label", 1);
        theme.SetConstant("shadow_offset_y", "Label", 2);

        AddHeader(theme, "HeaderLarge", 34, Text);
        AddHeader(theme, "HeaderMedium", 22, Text);
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

    private static StyleBoxFlat Box(Color fill, Color border, int borderWidth = 1, int radius = 4)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 9,
            ContentMarginBottom = 9,
        };
    }

    private static void StyleButtons(Theme theme)
    {
        var normal = Box(new Color(0.10f, 0.125f, 0.17f), PanelEdge);
        var hover = Box(new Color(0.15f, 0.18f, 0.24f), new Color(0.64f, 0.52f, 0.18f), 2);
        var pressed = Box(new Color(0.075f, 0.09f, 0.12f), Accent, 2);
        var disabled = Box(new Color(0.065f, 0.075f, 0.095f), new Color(0.13f, 0.15f, 0.19f));
        var focus = Box(new Color(0, 0, 0, 0), Accent, 3);

        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        theme.SetStylebox("disabled", "Button", disabled);
        theme.SetStylebox("focus", "Button", focus);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_focus_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Accent);
        theme.SetColor("font_disabled_color", "Button", new Color(0.43f, 0.46f, 0.53f));
        theme.SetFontSize("font_size", "Button", 16);
    }

    private static void StylePanels(Theme theme)
    {
        var panel = Box(Panel, PanelEdge, 1, 6);
        panel.ContentMarginLeft = 18;
        panel.ContentMarginRight = 18;
        panel.ContentMarginTop = 16;
        panel.ContentMarginBottom = 16;
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "Panel", panel);

        theme.AddType("MenuCard");
        theme.SetTypeVariation("MenuCard", "PanelContainer");
        var card = Box(PanelStrong, new Color(0.24f, 0.29f, 0.39f), 1, 5);
        card.ContentMarginLeft = 20;
        card.ContentMarginRight = 20;
        card.ContentMarginTop = 17;
        card.ContentMarginBottom = 17;
        theme.SetStylebox("panel", "MenuCard", card);
    }
}

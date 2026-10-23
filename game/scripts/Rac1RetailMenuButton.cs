using Godot;

namespace OneBigPackage;

/// <summary>
/// Focus/accept behaviour for the R&C1 pause panel. The dark surface and
/// its original E99E/FX layers are rendered by Rac1RetailPanelSurface,
/// without custom Godot rectangles or unsupported selection markers.
/// </summary>
public sealed partial class Rac1RetailMenuButton : Button
{
    public bool RetailPauseRoot { get; set; }

    public Rac1RetailMenuButton()
    {
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeFontOverride("font", Rac1RetailUiSkin.Font("regular"));
        AddThemeFontSizeOverride("font_size", 20);
        AddThemeColorOverride("font_color", Rac1RetailUiSkin.NativeNormal);
        AddThemeColorOverride("font_focus_color", Rac1RetailUiSkin.NativeSelected);
        AddThemeColorOverride("font_hover_color", Rac1RetailUiSkin.NativeSelected);
        AddThemeColorOverride("font_pressed_color", Rac1RetailUiSkin.NativeSelected);
        AddThemeColorOverride("font_disabled_color", new Color(.375f, .375f, .375f));
        AddThemeStyleboxOverride("normal", MakeFallbackStyle(new Color(.040f, .048f, .075f),
            new Color(.35f, .4f, .42f)));
        AddThemeStyleboxOverride("hover", MakeFallbackStyle(new Color(.052f, .062f, .083f),
            new Color(.55f, .56f, .46f)));
        AddThemeStyleboxOverride("pressed", MakeFallbackStyle(new Color(.052f, .062f, .083f),
            new Color(.55f, .56f, .46f)));
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        AddThemeStyleboxOverride("disabled", MakeFallbackStyle(new Color(.035f, .040f, .055f),
            new Color(.24f, .27f, .29f)));
        SetProcess(true);
    }

    public override void _Ready()
    {
        MouseFilter = UiPointerModality.ControllerWasLastInput ?
            MouseFilterEnum.Ignore : MouseFilterEnum.Stop;

        if (RetailPauseRoot)
        {
            AddThemeFontOverride("font", Rac1RetailUiSkin.Font("large"));
            AddThemeFontSizeOverride("font_size", 25);
        }

        if (Rac1RetailUiAssets.IsLoaded)
        {
            // No Godot panel underneath the native composite. Draw the
            // shader surface behind the engine-managed focused label.
            var empty = new StyleBoxEmpty();
            foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                AddThemeStyleboxOverride(state, empty);
            AddChild(new Rac1RetailPanelSurface());
        }
    }

    public override void _Process(double delta)
    {
        var filter = UiPointerModality.ControllerWasLastInput ?
            MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        if (MouseFilter != filter) MouseFilter = filter;
    }

    private static StyleBoxFlat MakeFallbackStyle(Color fill, Color border) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        ContentMarginLeft = 9,
        ContentMarginRight = 9,
        ContentMarginTop = 4,
        ContentMarginBottom = 4,
    };
}

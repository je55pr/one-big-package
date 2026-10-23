using Godot;

namespace OneBigPackage;

/// <summary>
/// Source-backed E99E/FX layers and native menu text colours. The 2002
/// renderer's geometry and PS2 GS alpha combine still require capture-level
/// reconstruction; the root-only placement is calibrated to a retail image.
/// </summary>
public sealed partial class Rac1RetailMenuButton : Button
{
    public bool RetailPauseRoot { get; set; }
    private double _fraction;
    private bool _flashActive;
    private int _flashTime;
    private readonly RandomNumberGenerator _random = new();

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
        // In the PS2 pause menu there is no pointer hover state.
        // Prevent a stale hidden cursor from highlighting a second item.
        MouseFilter = UiPointerModality.ControllerWasLastInput ?
            MouseFilterEnum.Ignore : MouseFilterEnum.Stop;

        if (!RetailPauseRoot) return;
        AddThemeFontOverride("font", Rac1RetailUiSkin.Font("large"));
        AddThemeFontSizeOverride("font_size", 25);
        // No Godot filled backgrounds or focus frame in the six-choice root.
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            AddThemeStyleboxOverride(state, empty);
        AddThemeColorOverride("font_color", Rac1RetailUiSkin.NativeNormal);
        AddThemeColorOverride("font_focus_color", Rac1RetailUiSkin.NativeSelected);
        AddThemeColorOverride("font_hover_color", Rac1RetailUiSkin.NativeSelected);
    }

    private static StyleBoxFlat MakeFallbackStyle(Color fill, Color border) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = 2, BorderWidthTop = 2,
        BorderWidthRight = 2, BorderWidthBottom = 2,
        ContentMarginLeft = 9, ContentMarginRight = 9,
        ContentMarginTop = 4, ContentMarginBottom = 4,
    };

    public override void _Process(double delta)
    {
        var filter = UiPointerModality.ControllerWasLastInput ?
            MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        if (MouseFilter != filter) MouseFilter = filter;

        // Native FUN_00223e28: an inactive panel triggers a 1-in-2000 flash
        // per 60Hz update; active time increments by two until 0x100.
        _fraction += Math.Min(.2, Math.Max(0, delta)) * 60.0;
        int ticks = Math.Min(12, (int)_fraction);
        _fraction -= ticks;
        for (int i = 0; i < ticks; i++)
        {
            if (_flashActive)
            {
                _flashTime += 2;
                if (_flashTime >= 0x100) _flashActive = false;
            }
            else if (_random.RandiRange(0, 1999) == 0)
            {
                _flashActive = true;
                _flashTime = 0;
            }
        }
        if (_flashActive) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Rac1RetailUiAssets.IsLoaded) return;
        Rect2 rect = new(0, 0, Size.X, Size.Y);
        var basePanel = Rac1RetailUiAssets.Sprite(0xE99E, 7);
        if (basePanel is not null)
            DrawTextureRect(basePanel, rect, false, Colors.White);

        // Source GS alpha equations are not yet reproduced by Godot's
        // standard texture blending. Low-amplitude FX preserve the retail
        // dark-striped panel instead of producing an incorrect grey gradient.
        var streak = Rac1RetailUiAssets.Effect(0x1C);
        if (streak is not null)
            DrawTextureRect(streak, rect, false, new Color(.75f, .75f, .75f, .020f));

        var surface = Rac1RetailUiAssets.Effect(0x19);
        if (surface is not null)
            DrawTextureRectRegion(surface, rect.Grow(-1),
                new Rect2(1, 1, .0f + Math.Min(62, surface.GetWidth() - 1),
                    Math.Min(62, surface.GetHeight() - 1)),
                new Color(.75f, .75f, .75f, .022f));

        if (_flashActive && Rac1RetailUiAssets.Effect(0x1A) is { } flash)
        {
            float alpha = Math.Min(128, 2 * Math.Max(0, 128 - Math.Max(0, _flashTime - 128))) / 128f;
            float u = _random.RandiRange(0, Math.Min(199, Math.Max(0, flash.GetWidth() - 2)));
            float v = _random.RandiRange(0, Math.Min(199, Math.Max(0, flash.GetHeight() - 2)));
            DrawTextureRectRegion(flash, rect,
                new Rect2(u, v, Math.Min(rect.Size.X, flash.GetWidth() - u),
                    Math.Min(rect.Size.Y, flash.GetHeight() - v)),
                new Color(1, 1, 1, alpha));
        }

        if (RetailPauseRoot)
        {
            // Screenshot-calibrated outline, not a recovered GS packet.
            // The exact palette/margins of the original panel Moby are pending.
            DrawRect(rect.Grow(-1), new Color(.64f, .63f, .47f, .92f), false, 2);
            return;
        }

        // Native FUN_0021d4a8 draws the red check marker only for text-list
        // flags 0x200/0x800 (unlock/language selection). The Help/Options
        // page flag values have NOT been recovered; don't display this
        // sprite on every focused option. Selected font colour is evidenced.
    }
}

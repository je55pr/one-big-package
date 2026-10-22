using Godot;

namespace OneBigPackage;

/// <summary>
/// R&C1-source animated menu panel. The original uses the E99E sprite
/// family plus FX #0x19/#0x1A/#0x1C for texture, noise and highlight.
/// UI-scale/alpha here are host approximations, not claimed PS2 pixel parity.
/// </summary>
public sealed partial class Rac1RetailMenuButton : Button
{
    private double _seconds;
    public Rac1RetailMenuButton()
    {
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeFontOverride("font", Rac1RetailUiSkin.Font("regular"));
        AddThemeFontSizeOverride("font_size", 20);
        AddThemeColorOverride("font_color", Rac1RetailUiSkin.Cream);
        AddThemeColorOverride("font_focus_color", Colors.White);
        AddThemeColorOverride("font_hover_color", Colors.White);
        AddThemeColorOverride("font_disabled_color", new Color(.6f, .63f, .71f));
        AddThemeStyleboxOverride("normal", Style(new Color(.045f, .06f, .11f, .94f), new Color(.35f, .43f, .50f)));
        AddThemeStyleboxOverride("hover", Style(new Color(.075f, .087f, .14f, .96f), new Color(.8f, .76f, .48f)));
        AddThemeStyleboxOverride("pressed", Style(new Color(.12f, .10f, .055f, 1), new Color(1, .92f, .46f)));
        AddThemeStyleboxOverride("focus", Style(new Color(0, 0, 0, 0), new Color(.99f, .81f, .25f)));
        AddThemeStyleboxOverride("disabled", Style(new Color(.045f, .05f, .075f, .8f), new Color(.2f, .22f, .3f)));
        SetProcess(true);
    }

    private static StyleBoxFlat Style(Color fill, Color border) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = 2,
        BorderWidthRight = 2,
        BorderWidthTop = 2,
        BorderWidthBottom = 2,
        ContentMarginLeft = 9,
        ContentMarginRight = 9,
        ContentMarginTop = 4,
        ContentMarginBottom = 4,
    };

    public override void _Process(double delta)
    {
        _seconds += Math.Max(0, delta);
        if (HasFocus() || IsHovered()) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Rac1RetailUiAssets.IsLoaded) return;
        // Source sprite: retail menu panel underlay.
        var basePanel = Rac1RetailUiAssets.Sprite(0xE99E, 7);
        if (basePanel is not null)
            DrawTextureRect(basePanel, new Rect2(3, 3, Size.X - 6, Size.Y - 6),
                false, new Color(.72f, .82f, 1f, .26f));

        var surface = Rac1RetailUiAssets.Effect(0x19);
        if (surface is not null)
            DrawTextureRect(surface, new Rect2(5, 5, Size.X - 10, Size.Y - 10),
                false, new Color(.9f, .94f, 1f, .10f));

        bool selected = HasFocus() || IsHovered();
        if (!selected) return;
        // Selected marker: the original E99E frame 1.
        var marker = Rac1RetailUiAssets.Sprite(0xE99E, 1);
        if (marker is not null)
            DrawTextureRect(marker, new Rect2(2, Size.Y * .5f - 13f, 24, 26), false,
                new Color(1f, 1f, .85f, .95f));
        float pulse = Rac1RetailUiSkin.SelectionPulse(Time.GetTicksMsec());
        // Native FX 0x1A uses a scrolling/randomised UV offset and fades after
        // activation. This repeats during selection as a readable host fallback.
        var flash = Rac1RetailUiAssets.Effect(0x1A);
        if (flash is not null)
        {
            var extent = new Rect2(5, 4, Size.X - 10, Size.Y - 8);
            float u = (float)(_seconds * 34 % Math.Max(1, flash.GetWidth() - 2));
            float v = (float)(_seconds * 25 % Math.Max(1, flash.GetHeight() - 2));
            var src = new Rect2(u, v, Math.Min(flash.GetWidth() - u, Math.Max(4f, extent.Size.X / 5)),
                Math.Min(flash.GetHeight() - v, Math.Max(4f, extent.Size.Y)));
            DrawTextureRectRegion(flash, extent, src, new Color(.95f, .85f, .32f, .11f + .16f * pulse));
        }
        var streak = Rac1RetailUiAssets.Effect(0x1C);
        if (streak is not null)
            DrawTextureRect(streak, new Rect2(4, 4, Size.X - 8, Size.Y - 8), false,
                new Color(1f, .84f, .35f, .09f + .13f * pulse));
    }
}

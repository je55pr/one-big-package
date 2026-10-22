using Godot;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

/// <summary>
/// Common retail-sourced art/font presentation; rectangle tinting and scaling
/// are labelled Godot fallbacks until frame UVs and timing are screenshot-matched.
/// </summary>
public static class Rac1RetailUiSkin
{
    public static readonly Color Cream = new(1f, 0.92f, 0.69f);
    public static readonly Color Ink = new(0.024f, 0.035f, 0.085f, 0.94f);
    public static readonly Color Outline = new(0.39f, 0.62f, 0.77f, 0.9f);

    public static string NativeText(int id, string fallback) => Rac1RetailUiAssets.Text(id, fallback);
    public static Font Font(string style = "regular") => Rac1RetailUiAssets.Font(style) ?? ThemeDB.FallbackFont;

    public static float SelectionPulse(ulong milliseconds) => 0.72f + 0.28f *
        (float)Math.Sin(milliseconds * Math.PI / 500.0);

    public static void Frame(Control canvas, Rect2 rect, float alpha = 1f)
    {
        // The original game uses stretchable sprite 0x7580, edges from frame 1.
        // Render readable fill underneath so absent assets still yield a UI.
        canvas.DrawRect(rect, new Color(Ink.R, Ink.G, Ink.B, Ink.A * alpha));
        var middle = Rac1RetailUiAssets.Sprite(0x7580, 0);
        if (middle is not null)
            canvas.DrawTextureRect(middle, rect.Grow(-4), false, new Color(1, 1, 1, alpha));
        canvas.DrawRect(rect, new Color(Outline.R, Outline.G, Outline.B, alpha), false, 2);
        var edge = Rac1RetailUiAssets.Sprite(0x7580, 1);
        if (edge is not null)
        {
            float width = Math.Min(18, rect.Size.X / 5);
            canvas.DrawTextureRect(edge, new Rect2(rect.Position.X, rect.Position.Y + 4,
                width, rect.Size.Y - 8), false, new Color(1, 1, 1, alpha));
        }
    }

    public static void Icon(Control canvas, ushort animation, int frame, Rect2 rect, Color? tint = null)
    {
        if (Rac1RetailUiAssets.Sprite(animation, frame) is { } texture)
            canvas.DrawTextureRect(texture, rect, false, tint ?? Colors.White);
    }

    public static Label Heading(string text, int size = 24) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        ThemeTypeVariation = "HeaderMedium",
    };
}

/// <summary>Draws source art as a single non-interactive panel behind controls.</summary>
public sealed partial class Rac1RetailFrame : Control
{
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => Rac1RetailUiSkin.Frame(this, new Rect2(Vector2.Zero, Size));
}

/// <summary>
/// Native HelpDesk-shaped renderer with an explicit 60Hz presentation clock.
/// Context events supply messages; this component never reads gameplay state.
/// </summary>
public sealed partial class Rac1HelpDeskView : Control
{
    public readonly Rac1HelpDeskPresentation State = new();
    public bool ExpandOnPrompt { get; set; }
    private double _fraction;
    private Label _text = null!;

    public override void _Ready()
    {
        Name = "RetailHelpDesk";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(350, 105);
        _text = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 25,
            OffsetTop = 12,
            OffsetRight = -25,
            OffsetBottom = -12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _text.AddThemeFontOverride("font", Rac1RetailUiSkin.Font("small"));
        AddChild(_text);
        SetProcess(true);
        UpdateView();
    }

    public void Prompt(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) State.Dismiss();
        else
        {
            State.ShowPrompt(message);
            if (ExpandOnPrompt) State.Expand();
        }
        UpdateView();
    }

    public void Open() { State.Expand(); UpdateView(); }
    public void Dismiss() { State.Dismiss(); UpdateView(); }

    public override void _Process(double delta)
    {
        _fraction += delta * 60;
        int ticks = Math.Min(12, (int)_fraction);
        if (ticks > 0)
        {
            _fraction -= ticks;
            State.Advance(ticks);
        }
        UpdateView();
    }

    private void UpdateView()
    {
        Visible = State.Current != Rac1HelpDeskPresentation.Phase.Hidden;
        if (_text is null) return;
        _text.Text = State.Message;
        _text.Visible = State.Current is Rac1HelpDeskPresentation.Phase.FadeIn or
            Rac1HelpDeskPresentation.Phase.Text or Rac1HelpDeskPresentation.Phase.FadeOut;
        _text.Modulate = new Color(1f, 0.90f, 0.71f, State.Opacity);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Visible) return;
        float extent = Math.Max(0.18f, State.Expansion);
        float width = Mathf.Lerp(64, Size.X, extent);
        float height = Mathf.Lerp(64, Size.Y, extent);
        var rect = new Rect2((Size - new Vector2(width, height)) * .5f, new Vector2(width, height));
        Rac1RetailUiSkin.Frame(this, rect, 0.90f);
        if (State.Current is Rac1HelpDeskPresentation.Phase.Prompt or Rac1HelpDeskPresentation.Phase.Opening)
            Rac1RetailUiSkin.Icon(this, 0x7544, 0, new Rect2((Size.X - 24) / 2, (Size.Y - 24) / 2, 24, 24));
    }
}

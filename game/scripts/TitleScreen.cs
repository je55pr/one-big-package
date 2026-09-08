using Godot;

namespace OneBigPackage;

/// <summary>
/// The gamey front door shown on a plain interactive launch (no <c>--test-scene</c>,
/// no source args): the OBP logo over a dark graded backdrop with a "press any
/// key" prompt. Any key / click / gamepad button fades it out and calls
/// <see cref="Dismissed"/>, which hands off to the normal Game Sources screen.
/// Deterministic harnesses never construct this — see <see cref="SourceManagerBootstrap"/>.
/// </summary>
public sealed partial class TitleScreen : CanvasLayer
{
    /// <summary>Raised once, after the fade-out, when the player dismisses the title.</summary>
    public event System.Action? Dismissed;

    private Control _root = null!;
    private TextureRect _logo = null!;
    private Label _prompt = null!;
    private bool _leaving;

    public override void _Ready()
    {
        Layer = 100;
        Name = "TitleScreen";
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);

        _root.AddChild(new TextureRect
        {
            Texture = VerticalBackdrop(),
            AnchorRight = 1,
            AnchorBottom = 1,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _root.AddChild(new TextureRect
        {
            Texture = Vignette(),
            AnchorRight = 1,
            AnchorBottom = 1,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(centre);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 26);
        centre.AddChild(column);

        _logo = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/branding/obp-logo.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(880, 528),
            PivotOffset = new Vector2(440, 264),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        column.AddChild(_logo);

        _prompt = new Label
        {
            Text = "PRESS ANY KEY",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _prompt.AddThemeFontSizeOverride("font_size", 17);
        _prompt.AddThemeColorOverride("font_color", UiTheme.Accent);
        column.AddChild(_prompt);

        AddFooter("One Big Package  ·  production showcase build", left: true);
        AddFooter("Ratchet & Clank™ trilogy  ·  reconstructed runtime", left: false);

        PlayIntro();
    }

    private void PlayIntro()
    {
        _logo.Position += new Vector2(0, 22);
        var intro = CreateTween().SetParallel();
        intro.TweenProperty(_logo, "modulate:a", 1.0, 0.7).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        intro.TweenProperty(_logo, "position:y", _logo.Position.Y - 22, 0.7).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        intro.Chain().TweenProperty(_prompt, "modulate:a", 1.0, 0.4);

        // Perpetual "breathe" on the logo and a pulse on the prompt.
        var breathe = CreateTween().SetLoops();
        breathe.TweenProperty(_logo, "scale", new Vector2(1.015f, 1.015f), 3.4).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        breathe.TweenProperty(_logo, "scale", Vector2.One, 3.4).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        var pulse = CreateTween().SetLoops();
        pulse.TweenProperty(_prompt, "modulate:a", 0.35, 1.05).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        pulse.TweenProperty(_prompt, "modulate:a", 1.0, 1.05).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_leaving)
        {
            return;
        }

        bool go = @event switch
        {
            InputEventKey { Pressed: true, Echo: false } => true,
            InputEventMouseButton { Pressed: true } => true,
            InputEventJoypadButton { Pressed: true } => true,
            _ => false,
        };
        if (!go)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if (@event is InputEventKey { Keycode: Key.Escape })
        {
            GetTree().Quit();
            return;
        }

        Leave();
    }

    private void Leave()
    {
        _leaving = true;
        _prompt.Text = "LOADING…";
        var outro = CreateTween();
        outro.TweenProperty(_root, "modulate:a", 0.0, 0.35).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        outro.TweenCallback(Callable.From(() =>
        {
            Dismissed?.Invoke();
            QueueFree();
        }));
    }

    private void AddFooter(string text, bool left)
    {
        var label = new Label
        {
            Text = text,
            Modulate = UiTheme.TextDim,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
        };
        label.SetAnchorsPreset(left ? Control.LayoutPreset.BottomLeft : Control.LayoutPreset.BottomRight);
        label.OffsetLeft = left ? 28 : -420;
        label.OffsetRight = left ? 420 : -28;
        label.OffsetTop = -40;
        label.OffsetBottom = -18;
        _root.AddChild(label);
    }

    private static Texture2D VerticalBackdrop()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.045f, 0.06f, 0.10f));
        gradient.SetColor(1, new Color(0.015f, 0.02f, 0.035f));
        gradient.AddPoint(0.55f, new Color(0.07f, 0.085f, 0.13f));
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 16,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(0, 1),
        };
    }

    private static Texture2D Vignette()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0, 0, 0, 0));
        gradient.SetColor(1, new Color(0, 0, 0, 0.55f));
        gradient.AddPoint(0.6f, new Color(0, 0, 0, 0));
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 1.0f),
        };
    }
}

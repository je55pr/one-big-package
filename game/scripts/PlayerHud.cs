using Godot;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

/// <summary>
/// Non-interactive Godot renderer for the engine-neutral HUD snapshot.
/// It never queries gameplay systems and hides fields that the snapshot does not own.
/// </summary>
public partial class PlayerHud : Control
{
    private PanelContainer _healthPanel = null!;
    private PanelContainer _boltsPanel = null!;
    private PanelContainer _weaponPanel = null!;
    private PanelContainer _promptPanel = null!;

    private Label _healthKicker = null!;
    private Label _healthValue = null!;
    private Label _healthState = null!;
    private Label _healthDelta = null!;
    private Label _boltsKicker = null!;
    private Label _boltsValue = null!;
    private Label _boltsDelta = null!;
    private Label _weaponName = null!;
    private Label _ammo = null!;
    private Label _ammoDelta = null!;
    private Label _promptAction = null!;
    private Label _promptMessage = null!;
    private ProgressBar _promptProgress = null!;

    private readonly HudTransientFeedbackAnimator _feedback = new();
    private long _epoch = -1;
    private long _revision = -1;
    private bool _hasHealth;
    private bool _hasBolts;
    private bool _hasWeapon;
    private bool _hasPrompt;
    private string? _boltsResourceKey;

    public override void _Ready()
    {
        Name = "PlayerHud";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        BuildLayout();
        Visible = false;
    }

    public void Render(HudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Epoch < _epoch ||
            (snapshot.Epoch == _epoch && snapshot.Revision < _revision))
        {
            return;
        }

        _epoch = snapshot.Epoch;
        _revision = snapshot.Revision;
        _feedback.Accept(snapshot);

        RenderHealth(snapshot.Health);
        RenderBolts(snapshot.Bolts);
        RenderWeapon(snapshot.CurrentWeapon);
        RenderPrompt(snapshot.ContextPrompt);
        ApplyTransientFeedback();
        UpdateVisibility();
    }

    public override void _Process(double delta)
    {
        _feedback.Advance(delta);
        ApplyTransientFeedback();
        UpdateVisibility();
    }

    private void BuildLayout()
    {
        var margin = Passive(new MarginContainer
        {
            Name = "SafeMargin",
            AnchorRight = 1f,
            AnchorBottom = 1f,
        });
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var outer = Passive(new VBoxContainer
        {
            Name = "HudLayout",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        });
        outer.AddThemeConstantOverride("separation", 12);
        margin.AddChild(outer);

        var top = Passive(new HBoxContainer
        {
            Name = "TopRow",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        outer.AddChild(top);
        top.AddChild(Passive(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }));
        _weaponPanel = BuildWeaponPanel();
        top.AddChild(_weaponPanel);

        outer.AddChild(Passive(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }));

        var promptRow = Passive(new HBoxContainer
        {
            Name = "PromptRow",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        promptRow.AddChild(Passive(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }));
        _promptPanel = BuildPromptPanel();
        promptRow.AddChild(_promptPanel);
        promptRow.AddChild(Passive(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }));
        outer.AddChild(promptRow);

        var bottom = Passive(new HBoxContainer
        {
            Name = "BottomRow",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        var stats = Passive(new VBoxContainer { Name = "PlayerStats" });
        stats.AddThemeConstantOverride("separation", 8);
        _healthPanel = BuildHealthPanel();
        _boltsPanel = BuildBoltsPanel();
        stats.AddChild(_healthPanel);
        stats.AddChild(_boltsPanel);
        bottom.AddChild(stats);
        bottom.AddChild(Passive(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }));
        outer.AddChild(bottom);
    }

    private PanelContainer BuildHealthPanel()
    {
        var panel = Card("Health", 250);
        var box = Stack(panel);

        _healthKicker = Kicker("NANOTECH");
        _healthValue = ValueLabel();
        _healthState = SecondaryLabel();
        _healthDelta = SecondaryLabel();
        _healthDelta.Visible = false;
        box.AddChild(_healthKicker);
        box.AddChild(_healthValue);
        box.AddChild(_healthState);
        box.AddChild(_healthDelta);
        return panel;
    }

    private PanelContainer BuildBoltsPanel()
    {
        var panel = Card("Bolts", 250);
        var box = Stack(panel);
        _boltsKicker = Kicker("BOLTS");
        box.AddChild(_boltsKicker);
        _boltsValue = ValueLabel();
        _boltsDelta = SecondaryLabel();
        _boltsDelta.Visible = false;
        box.AddChild(_boltsValue);
        box.AddChild(_boltsDelta);
        return panel;
    }

    private PanelContainer BuildWeaponPanel()
    {
        var panel = Card("Weapon", 260);
        var box = Stack(panel);
        box.AddChild(Kicker("EQUIPPED"));
        _weaponName = ValueLabel();
        _ammo = SecondaryLabel();
        _ammoDelta = SecondaryLabel();
        _ammoDelta.Visible = false;
        box.AddChild(_weaponName);
        box.AddChild(_ammo);
        box.AddChild(_ammoDelta);
        return panel;
    }

    private PanelContainer BuildPromptPanel()
    {
        var panel = Card("ContextPrompt", 360);
        var box = Stack(panel);
        _promptAction = Kicker(string.Empty);
        _promptAction.HorizontalAlignment = HorizontalAlignment.Center;
        _promptMessage = SecondaryLabel();
        _promptMessage.HorizontalAlignment = HorizontalAlignment.Center;
        _promptMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _promptProgress = Passive(new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 7),
        });
        box.AddChild(_promptAction);
        box.AddChild(_promptMessage);
        box.AddChild(_promptProgress);
        return panel;
    }

    private void RenderHealth(HudHealth? health)
    {
        _hasHealth = health is not null;
        _healthPanel.Visible = _hasHealth;
        if (health is null)
        {
            return;
        }

        _healthKicker.Text = SemanticLabel(health.UnitKey);
        _healthValue.Text = health.Capacity is { } capacity
            ? $"{health.Current:N0} / {capacity:N0}"
            : $"{health.Current:N0}";
        _healthState.Visible = health.LifeState is not null;
        _healthState.Text = health.LifeState switch
        {
            HudLifeState.Dead => "DOWN",
            HudLifeState.Alive => "ACTIVE",
            _ => string.Empty,
        };
        _healthState.Modulate = health.LifeState == HudLifeState.Dead
            ? UiTheme.Warning
            : UiTheme.TextDim;
    }

    private void RenderBolts(HudCurrency? bolts)
    {
        _hasBolts = bolts is not null;
        _boltsResourceKey = bolts?.CurrencyKey;
        _boltsPanel.Visible = _hasBolts;
        _boltsValue.Visible = _hasBolts;
        if (bolts is null)
        {
            return;
        }

        _boltsKicker.Text = SemanticLabel(bolts.CurrencyKey);
        _boltsValue.Text = $"{bolts.Balance:N0}";
    }

    private void RenderWeapon(HudWeapon? weapon)
    {
        _hasWeapon = weapon is not null;
        _weaponPanel.Visible = _hasWeapon;
        if (weapon is null)
        {
            return;
        }

        _weaponName.Text = WeaponName(weapon);
        _ammo.Visible = weapon.Ammo is not null;
        if (weapon.Ammo is { } ammo)
        {
            _ammo.Text = ammo.Capacity is { } capacity
                ? $"AMMO  {ammo.Current:N0} / {capacity:N0}"
                : $"AMMO  {ammo.Current:N0}";
        }
    }

    private void RenderPrompt(HudContextPrompt? prompt)
    {
        _hasPrompt = prompt is not null;
        if (prompt is null)
        {
            return;
        }

        _promptAction.Text = SemanticLabel(prompt.ActionId);
        _promptMessage.Text = DisplayText(prompt.MessageKey);
        _promptProgress.Visible = prompt.Progress is not null;
        if (prompt.Progress is { } progress)
        {
            _promptProgress.Value = Math.Clamp(progress, 0d, 1d);
        }
    }

    private void ApplyTransientFeedback()
    {
        HudTransientFeedbackState feedback = _feedback.Current;

        _healthPanel.Visible = _hasHealth;
        _healthPanel.Modulate = _hasHealth
            ? FeedbackTint(
                feedback.HealthPulse,
                feedback.HealthDelta is < 0 ? UiTheme.Warning : UiTheme.Ready,
                0.30d)
            : Colors.White;
        _healthDelta.Visible = _hasHealth &&
            feedback.HealthPulse > 0d &&
            feedback.HealthDelta is not null;
        if (feedback.HealthDelta is { } healthDelta)
        {
            _healthDelta.Text = $"NANOTECH {SignedDelta(healthDelta)}";
            _healthDelta.Modulate = healthDelta < 0 ? UiTheme.Warning : UiTheme.Ready;
        }

        bool boltFeedback =
            feedback.PickupPulse > 0d &&
            feedback.PickupDelta is not null &&
            (feedback.PickupResourceKey == _boltsResourceKey ||
             feedback.PickupResourceKey == "bolts");
        _boltsPanel.Visible = _hasBolts || boltFeedback;
        _boltsPanel.Modulate = boltFeedback
            ? FeedbackTint(feedback.PickupPulse, UiTheme.Accent, 0.28d)
            : Colors.White;
        _boltsDelta.Visible = boltFeedback;
        if (boltFeedback && feedback.PickupDelta is { } boltDelta)
        {
            if (!_hasBolts)
            {
                _boltsKicker.Text = SemanticLabel(feedback.PickupResourceKey ?? "bolts");
                _boltsValue.Visible = false;
            }
            _boltsDelta.Text = SignedDelta(boltDelta);
            _boltsDelta.Modulate = boltDelta < 0 ? UiTheme.Warning : UiTheme.Accent;
        }

        _weaponPanel.Visible = _hasWeapon;
        _weaponPanel.Modulate = _hasWeapon
            ? FeedbackTint(feedback.WeaponPulse, UiTheme.Accent, 0.20d)
            : Colors.White;
        _ammoDelta.Visible = _hasWeapon &&
            feedback.WeaponPulse > 0d &&
            feedback.AmmoDelta is not null;
        if (feedback.AmmoDelta is { } ammoDelta)
        {
            _ammoDelta.Text = $"AMMO {SignedDelta(ammoDelta)}";
            _ammoDelta.Modulate = ammoDelta < 0 ? UiTheme.Warning : UiTheme.Ready;
        }

        Color promptTint = FeedbackTint(
            feedback.PromptPulse,
            UiTheme.Accent,
            0.15d);
        promptTint.A = (float)Math.Clamp(feedback.PromptOpacity, 0d, 1d);
        _promptPanel.Modulate = promptTint;
        _promptPanel.Visible = feedback.PromptOpacity > 0.001d;
    }

    private void UpdateVisibility()
    {
        Visible = _healthPanel.Visible ||
                  _boltsPanel.Visible ||
                  _weaponPanel.Visible ||
                  _promptPanel.Visible;
    }

    private static string SignedDelta(int delta) =>
        delta > 0 ? $"+{delta:N0}" : $"{delta:N0}";

    private static Color FeedbackTint(double intensity, Color accent, double strength) =>
        Colors.White.Lerp(
            accent,
            (float)Math.Clamp(intensity * strength, 0d, 1d));

    private static PanelContainer Card(string name, float minimumWidth)
    {
        return Passive(new PanelContainer
        {
            Name = name,
            ThemeTypeVariation = "HudCard",
            CustomMinimumSize = new Vector2(minimumWidth, 0),
            Visible = false,
        });
    }

    private static VBoxContainer Stack(PanelContainer panel)
    {
        var box = Passive(new VBoxContainer());
        box.AddThemeConstantOverride("separation", 3);
        panel.AddChild(box);
        return box;
    }

    private static Label Kicker(string text) => Passive(new Label
    {
        Text = text,
        ThemeTypeVariation = "HudKicker",
    });

    private static Label ValueLabel() => Passive(new Label
    {
        ThemeTypeVariation = "HudValue",
    });

    private static Label SecondaryLabel() => Passive(new Label
    {
        ThemeTypeVariation = "HudSecondary",
    });

    private static T Passive<T>(T control) where T : Control
    {
        control.MouseFilter = MouseFilterEnum.Ignore;
        return control;
    }

    private static string WeaponName(HudWeapon weapon)
    {
        string key = weapon.NameKey ?? weapon.PresentationKey;
        return key switch
        {
            "weapon.wrench" or "rac1.weapon.wrench" => "Wrench",
            "weapon.bomb-glove" or "rac1.weapon.bomb-glove" => "Bomb Glove",
            _ => DisplayText(key),
        };
    }

    private static string SemanticLabel(string key) => key switch
    {
        "nanotech" => "NANOTECH",
        "bolts" => "BOLTS",
        _ => DisplayText(key).ToUpperInvariant(),
    };

    private static string DisplayText(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (key.Any(char.IsWhiteSpace))
        {
            return key;
        }

        int dot = key.LastIndexOf('.');
        string text = dot >= 0 ? key[(dot + 1)..] : key;
        text = text.Replace('-', ' ').Replace('_', ' ');
        return string.Join(' ', text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }
}

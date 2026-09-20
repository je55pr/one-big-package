using Godot;
using OBP.RAC1.Progression;

namespace OneBigPackage;

/// <summary>
/// Godot-only presentation for the recovered R&C1 planet-map state.
/// Unlock order and travel admission stay in OBP.RAC1.
/// </summary>
public partial class Rac1PlanetTravelUi : CanvasLayer
{
    public event Action<int>? TravelRequested;
    public event Action? BackRequested;

    private readonly List<Button> _focusOrder = [];
    private Label _hint = null!;
    private Button _back = null!;
    private Button? _firstTravelButton;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel") &&
            !@event.IsActionPressed("obp_planet_map"))
            return;

        GetViewport().SetInputAsHandled();
        BackRequested?.Invoke();
    }
    public void Populate(Rac1PlanetMapSnapshot map)
    {
        Name = "Rac1PlanetTravel";
        Layer = 20;
        AddChild(new ColorRect
        {
            Color = UiTheme.Backdrop,
            AnchorRight = 1,
            AnchorBottom = 1,
        });

        var margin = new MarginContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        margin.AddThemeConstantOverride("margin_left", 64);
        margin.AddThemeConstantOverride("margin_top", 44);
        margin.AddThemeConstantOverride("margin_right", 64);
        margin.AddThemeConstantOverride("margin_bottom", 44);
        AddChild(margin);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 14);
        margin.AddChild(outer);

        outer.AddChild(new Label
        {
            Text = "RATCHET & CLANK  //  GALACTIC MAP",
            ThemeTypeVariation = "HeaderSmall",
        });
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 14);
        outer.AddChild(titleRow);

        _back = new Button
        {
            Text = "<  RETURN",
            CustomMinimumSize = new Vector2(150, 48),
            FocusMode = Control.FocusModeEnum.All,
        };
        _back.Pressed += () => BackRequested?.Invoke();
        titleRow.AddChild(_back);
        _focusOrder.Add(_back);

        titleRow.AddChild(new Label
        {
            Text = "SELECT DESTINATION",
            ThemeTypeVariation = "HeaderLarge",
            VerticalAlignment = VerticalAlignment.Center,
        });

        string currentName = DestinationName(map.CurrentLevel);
        outer.AddChild(new Label
        {
            Text = $"CURRENT WORLD   {currentName.ToUpperInvariant()}",
            ThemeTypeVariation = "HeaderMedium",
            Modulate = UiTheme.Accent,
        });

        outer.AddChild(new Label
        {
            Text = "Only destinations discovered by the recovered R&C1 campaign state appear below.",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        outer.AddChild(scroll);

        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(list);

        int actionable = 0;
        foreach (int destinationId in map.UnlockedDestinations)
        {
            string name = DestinationName(destinationId);
            bool isCurrent = destinationId == map.CurrentLevel;
            var button = new Button
            {
                Text = isCurrent
                    ? $"    {name.ToUpperInvariant()}    // CURRENT"
                    : $"    {name.ToUpperInvariant()}",
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 54),
                FocusMode = Control.FocusModeEnum.All,
                Disabled = isCurrent,
            };

            if (!isCurrent)
            {
                int selected = destinationId;
                button.Pressed += () =>
                {
                    _hint.Text = $"TRAVELLING TO {name.ToUpperInvariant()}...";
                    _hint.Modulate = UiTheme.Accent;
                    TravelRequested?.Invoke(selected);
                };
                _focusOrder.Add(button);
                _firstTravelButton ??= button;
                actionable++;
            }

            list.AddChild(button);
        }

        if (actionable == 0)
        {
            var empty = new Label
            {
                Text = "No other destinations have been discovered yet.",
                Modulate = UiTheme.TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(0, 70),
                VerticalAlignment = VerticalAlignment.Center,
            };
            list.AddChild(empty);
        }

        _hint = new Label
        {
            Text = "ARROWS / D-PAD  Navigate    |    ENTER / CONFIRM  Travel    |    M / START / CANCEL  Return",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 40),
        };
        outer.AddChild(_hint);

        WireFocus();
        Callable.From(FocusInitialControl).CallDeferred();
    }
    public void SetHint(string text, bool error = false)
    {
        if (_hint is null || !IsInstanceValid(_hint))
            return;

        _hint.Text = text;
        _hint.Modulate = error ? new Color(1f, 0.55f, 0.5f) : UiTheme.Ready;
    }

    private static string DestinationName(int destinationId) =>
        Rac1CampaignDestinationIdentity.ResolveDisplayName(destinationId)
        ?? $"Level {destinationId}";

    private void FocusInitialControl()
    {
        if (_firstTravelButton is { } first && IsInstanceValid(first))
            first.GrabFocus();
        else if (IsInstanceValid(_back))
            _back.GrabFocus();
    }

    private void WireFocus()
    {
        var enabled = _focusOrder
            .Where(button => IsInstanceValid(button) && !button.Disabled)
            .ToList();
        for (int index = 0; index < enabled.Count; index++)
        {
            Button current = enabled[index];
            Button previous = enabled[(index - 1 + enabled.Count) % enabled.Count];
            Button next = enabled[(index + 1) % enabled.Count];
            current.FocusNeighborTop = current.GetPathTo(previous);
            current.FocusPrevious = current.GetPathTo(previous);
            current.FocusNeighborBottom = current.GetPathTo(next);
            current.FocusNext = current.GetPathTo(next);
        }
    }
}

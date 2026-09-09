using Godot;
using OBP.Core;
using OBP.PS2;
using OBP.Runtime;

namespace OneBigPackage;

public partial class DestinationSelectorUi : CanvasLayer
{
    public event Action<ObpDestination>? DestinationChosen;
    public event Action? BackRequested;

    private readonly List<Button> _focusOrder = new();
    private Label _hint = null!;
    private Button _back = null!;
    private Button? _currentDestinationButton;
    private Button? _firstDestinationButton;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        BackRequested?.Invoke();
    }

    public void Populate(
        ObpSourceLibrary sources,
        IReadOnlyList<ObpSourceDefinition> definitions,
        IReadOnlyList<IObpWorldProvider> providers,
        string? currentDestinationId)
    {
        Name = "DestinationSelector";
        AddChild(new ColorRect { Color = UiTheme.Backdrop, AnchorRight = 1, AnchorBottom = 1 });

        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        AddChild(margin);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        margin.AddChild(outer);
        outer.AddChild(new Label { Text = "ONE BIG PACKAGE  //  WORLD SELECT", ThemeTypeVariation = "HeaderSmall" });

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 14);
        outer.AddChild(titleRow);
        _back = new Button { Text = "<  GAME SOURCES", CustomMinimumSize = new Vector2(170, 48), FocusMode = Control.FocusModeEnum.All };
        _back.Pressed += () => BackRequested?.Invoke();
        titleRow.AddChild(_back);
        _focusOrder.Add(_back);
        titleRow.AddChild(new Label { Text = "CHOOSE A WORLD", ThemeTypeVariation = "HeaderLarge", VerticalAlignment = VerticalAlignment.Center });

        outer.AddChild(new Label
        {
            Text = "Pick a destination from any connected game. Every available world enters through OBP's shared runtime.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = UiTheme.TextDim,
        });

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(list);

        int gameIndex = 0;
        foreach (var definition in definitions)
        {
            BuildGameSection(list, sources, definition, providers.FirstOrDefault(p => p.Game == definition.Game), currentDestinationId, gameIndex++);
        }

        _hint = new Label
        {
            Text = "ARROWS / D-PAD  Navigate    |    ENTER / CONFIRM  Travel    |    ESC / CANCEL  Game Sources",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 38),
        };
        outer.AddChild(_hint);

        WireFocus();
        Callable.From(FocusInitialControl).CallDeferred();
    }

    public void SetHint(string text, bool error = false)
    {
        if (_hint is null || !IsInstanceValid(_hint)) return;
        _hint.Text = text;
        _hint.Modulate = error ? new Color(1f, 0.55f, 0.5f) : UiTheme.Ready;
    }

    private void BuildGameSection(
        VBoxContainer parent,
        ObpSourceLibrary sources,
        ObpSourceDefinition definition,
        IObpWorldProvider? provider,
        string? currentDestinationId,
        int gameIndex)
    {
        var panel = new PanelContainer { ThemeTypeVariation = "MenuCard" };
        parent.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        panel.AddChild(content);

        var source = sources.Get(definition.Game);
        int availableCount = provider?.Catalogue.Destinations.Count(d => provider.CanLoad(d)) ?? 0;
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        content.AddChild(header);
        header.AddChild(new Label
        {
            Text = gameIndex switch { 0 => "I", 1 => "II", _ => "III" },
            ThemeTypeVariation = "HeaderMedium",
            CustomMinimumSize = new Vector2(38, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = gameIndex switch { 0 => UiTheme.GameOne, 1 => UiTheme.GameTwo, _ => UiTheme.GameThree },
        });
        header.AddChild(new Label { Text = definition.DisplayName.ToUpperInvariant(), ThemeTypeVariation = "HeaderMedium" });
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        string readiness = source is null
            ? "SOURCE NEEDED"
            : provider is null
                ? "SOURCE READY  |  WORLDS PENDING"
                : $"{availableCount} WORLD{(availableCount == 1 ? "" : "S")} READY";
        header.AddChild(new Label
        {
            Text = readiness,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = source is not null && provider is not null ? UiTheme.Ready : UiTheme.TextDim,
        });

        if (source is null)
        {
            content.AddChild(Status("Connect this game on Game Sources to unlock its destinations."));
            return;
        }
        if (provider is null)
        {
            content.AddChild(Status("Source connected. Its production world provider is not available yet."));
            return;
        }

        foreach (var destination in provider.Catalogue.Destinations
                     .OrderBy(d => SortKind(d.Kind)))
        {
            bool current = destination.DestinationId == currentDestinationId;
            string menuLabel = MenuDestinationLabel(destination);
            var button = new Button
            {
                Text = current
                    ? $">  {menuLabel}    |    CURRENT"
                    : $"    {menuLabel}",
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 50),
                FocusMode = Control.FocusModeEnum.All,
                Disabled = !provider.CanLoad(destination),
                TooltipText = TechnicalTooltip(destination),
            };
            if (current) button.Modulate = UiTheme.Accent;

            var selected = destination;
            button.Pressed += () =>
            {
                _hint.Text = $"TRAVELLING TO {selected.DisplayName.ToUpperInvariant()}...";
                _hint.Modulate = UiTheme.Accent;
                DestinationChosen?.Invoke(selected);
            };
            content.AddChild(button);
            _focusOrder.Add(button);
            if (!button.Disabled && _firstDestinationButton is null) _firstDestinationButton = button;
            if (!button.Disabled && current) _currentDestinationButton = button;
        }
    }

    private static Label Status(string text) => new()
    {
        Text = text,
        Modulate = UiTheme.TextDim,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(0, 36),
    };

    private static string MenuDestinationLabel(ObpDestination destination)
    {
        string label = destination.DisplayName.Trim();
        if (label.StartsWith("R&C1 ", StringComparison.OrdinalIgnoreCase)) label = label[5..];
        if (label.StartsWith("UYA ", StringComparison.OrdinalIgnoreCase)) label = label[4..];
        label = label.Replace("LEVEL", "LEVEL ", StringComparison.OrdinalIgnoreCase)
                     .Replace("TABLE", "TABLE ", StringComparison.OrdinalIgnoreCase);
        while (label.Contains("  ", StringComparison.Ordinal)) label = label.Replace("  ", " ", StringComparison.Ordinal);
        label = label.ToUpperInvariant();

        return destination.Kind == ObpDestinationKind.Unresolved
            ? label
            : $"{label}    |    {KindLabel(destination.Kind).ToUpperInvariant()}";
    }

    private static string TechnicalTooltip(ObpDestination destination)
    {
        string engine = destination.NativeEngineId is { Length: > 0 } ? destination.NativeEngineId : "n/a";
        return $"{destination.DestinationId}\nNative destination: {destination.NativeDestinationId}\nEngine id: {engine}\n{destination.NativeContainer ?? "Native container unknown"}";
    }

    private void FocusInitialControl()
    {
        if (_currentDestinationButton is { } current && IsInstanceValid(current)) current.GrabFocus();
        else if (_firstDestinationButton is { } first && IsInstanceValid(first)) first.GrabFocus();
        else if (IsInstanceValid(_back)) _back.GrabFocus();
    }

    private void WireFocus()
    {
        var enabled = _focusOrder.Where(button => IsInstanceValid(button) && !button.Disabled).ToList();
        for (int i = 0; i < enabled.Count; i++)
        {
            var current = enabled[i];
            var previous = enabled[(i - 1 + enabled.Count) % enabled.Count];
            var next = enabled[(i + 1) % enabled.Count];
            current.FocusNeighborTop = current.GetPathTo(previous);
            current.FocusPrevious = current.GetPathTo(previous);
            current.FocusNeighborBottom = current.GetPathTo(next);
            current.FocusNext = current.GetPathTo(next);
        }
    }

    private static int SortKind(ObpDestinationKind kind) => kind switch
    {
        ObpDestinationKind.Planet => 0,
        ObpDestinationKind.Hub => 1,
        ObpDestinationKind.Vendor => 2,
        ObpDestinationKind.SpaceCombat => 3,
        ObpDestinationKind.Scene => 4,
        ObpDestinationKind.Unresolved => 5,
        _ => 9,
    };

    private static string KindLabel(ObpDestinationKind kind) => kind switch
    {
        ObpDestinationKind.Planet => "planet",
        ObpDestinationKind.Hub => "hub",
        ObpDestinationKind.Vendor => "vendor",
        ObpDestinationKind.SpaceCombat => "space",
        ObpDestinationKind.Scene => "scene",
        ObpDestinationKind.Unresolved => "unknown",
        _ => kind.ToString(),
    };
}

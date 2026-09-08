using Godot;
using OBP.Core;
using OBP.PS2;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Game-neutral debug world browser. It knows only source definitions,
/// attached-source availability and <see cref="IObpWorldProvider"/> contracts;
/// native GC catalogue types do not leak into this UI.
/// </summary>
public partial class DestinationSelectorUi : CanvasLayer
{
    public event Action<ObpDestination>? DestinationChosen;
    public event Action? BackRequested;

    private Label _hint = null!;

    public void Populate(
        ObpSourceLibrary sources,
        IReadOnlyList<ObpSourceDefinition> definitions,
        IReadOnlyList<IObpWorldProvider> providers,
        string? currentDestinationId)
    {
        Name = "DestinationSelector";

        var dim = new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.06f, 0.97f),
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(dim);

        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        AddChild(margin);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        margin.AddChild(outer);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 12);
        outer.AddChild(titleRow);

        var back = new Button { Text = "← Game Sources", CustomMinimumSize = new Vector2(130, 0) };
        back.Pressed += () => BackRequested?.Invoke();
        titleRow.AddChild(back);
        titleRow.AddChild(new Label { Text = "One Big Package — Worlds", ThemeTypeVariation = "HeaderLarge" });

        outer.AddChild(new Label
        {
            Text = "Destinations come from game-specific providers, but every provider hands the same neutral RuntimeWorld to Godot. This is a debug browser, not the final galaxy map.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.7f, 0.74f, 0.82f),
        });

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(list);

        foreach (var definition in definitions)
        {
            list.AddChild(new HSeparator());
            var source = sources.Get(definition.Game);
            var provider = providers.FirstOrDefault(p => p.Game == definition.Game);

            string headerSuffix = source is null
                ? "source not attached"
                : provider is null
                    ? "source attached · runtime importer pending"
                    : $"{source.DiscSerial} · {provider.BuildId}";
            list.AddChild(new Label
            {
                Text = $"{definition.DisplayName}    ·    {headerSuffix}",
                Modulate = source is not null && provider is not null
                    ? new Color(0.65f, 0.85f, 1f)
                    : new Color(0.55f, 0.58f, 0.64f),
            });

            if (source is null)
            {
                list.AddChild(Status("  Attach this retail source on Game Sources to browse its worlds."));
                continue;
            }

            if (provider is null)
            {
                list.AddChild(Status("  Source ready. Its production world provider has not landed yet."));
                continue;
            }

            foreach (var destination in provider.Catalogue.Destinations
                         .OrderBy(d => SortKind(d.Kind))
                         .ThenBy(d => d.NativeDestinationId, StringComparer.Ordinal))
            {
                string native = destination.NativeEngineId is { Length: > 0 }
                                && destination.NativeEngineId != destination.NativeDestinationId.Replace("LEVEL", "", StringComparison.OrdinalIgnoreCase)
                    ? $"{destination.NativeDestinationId} · engine {destination.NativeEngineId}"
                    : destination.NativeDestinationId;
                var button = new Button
                {
                    Text = $"  {destination.DisplayName}    ·    {KindLabel(destination.Kind)}    ·    {native}",
                    Alignment = HorizontalAlignment.Left,
                    TooltipText = $"{destination.DestinationId}\n{destination.NativeContainer ?? "native container unknown"}",
                    Disabled = !provider.CanLoad(destination),
                };
                if (destination.DestinationId == currentDestinationId)
                {
                    button.Modulate = new Color(1f, 0.9f, 0.5f);
                }

                var selected = destination;
                button.Pressed += () =>
                {
                    _hint.Text = $"Loading {selected.DisplayName}…";
                    DestinationChosen?.Invoke(selected);
                };
                list.AddChild(button);
            }
        }

        _hint = new Label
        {
            Text = "Esc returns to Game Sources.",
            Modulate = new Color(0.62f, 0.84f, 0.66f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        outer.AddChild(_hint);
    }

    public void SetHint(string text, bool error = false)
    {
        if (_hint is null || !IsInstanceValid(_hint))
        {
            return;
        }

        _hint.Text = text;
        _hint.Modulate = error ? new Color(1f, 0.55f, 0.5f) : new Color(0.62f, 0.84f, 0.66f);
    }

    private static Label Status(string text) => new()
    {
        Text = text,
        Modulate = new Color(0.52f, 0.55f, 0.62f),
    };

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
        ObpDestinationKind.Unresolved => "unresolved",
        _ => kind.ToString(),
    };
}

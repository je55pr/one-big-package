using Godot;
using OBP.RAC2;

namespace OneBigPackage;

/// <summary>
/// Phase 3: the crude Going Commando planet / location selector shown after a
/// supported disc is verified. A scrolling list of every level the importer has
/// been probed against, from <see cref="GcPlanetCatalogue"/> — walkable planets
/// first, then the space arenas / stubs. Picking one emits
/// <see cref="PlanetChosen"/>; the host tears down the current world and loads
/// the next through the same generic path.
///
/// This is a debug selector, not the final OBP star map.
/// </summary>
public partial class PlanetSelectorUi : CanvasLayer
{
    [Signal]
    public delegate void PlanetChosenEventHandler(int levelId);

    private Label _hint = null!;

    public void Populate(string buildLine, int? currentLevelId)
    {
        Name = "PlanetSelector";

        var dim = new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.06f, 0.96f),
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

        outer.AddChild(new Label { Text = "One Big Package — Going Commando", ThemeTypeVariation = "HeaderLarge" });
        outer.AddChild(new Label { Text = buildLine, Modulate = new Color(0.65f, 0.8f, 0.95f) });
        outer.AddChild(new Label
        {
            Text = "Pick a planet. It is reconstructed straight from the retail disc. Esc returns to Game Sources.",
            Modulate = new Color(0.7f, 0.7f, 0.72f),
        });

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);

        AddGroup(list, "Planets", GcLevelKind.Planet, currentLevelId);
        AddGroup(list, "Hubs & vendor", null, currentLevelId, e => e.Kind is GcLevelKind.Hub or GcLevelKind.Vendor);
        AddGroup(list, "Space arenas & stubs", null, currentLevelId,
            e => e.Kind is GcLevelKind.SpaceCombat or GcLevelKind.Scene or GcLevelKind.Unresolved);

        _hint = new Label { Modulate = new Color(0.6f, 0.85f, 0.6f) };
        outer.AddChild(_hint);
    }

    public void SetHint(string text)
    {
        if (_hint is not null && IsInstanceValid(_hint))
        {
            _hint.Text = text;
            _hint.Modulate = text.StartsWith('⚠') ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 0.85f, 0.6f);
        }
    }

    private void AddGroup(
        VBoxContainer list,
        string title,
        GcLevelKind? kind,
        int? currentLevelId,
        System.Func<GcPlanetCatalogue.Entry, bool>? predicate = null)
    {
        predicate ??= e => e.Kind == kind;
        var entries = GcPlanetCatalogue.All.Where(predicate).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        list.AddChild(new HSeparator());
        list.AddChild(new Label { Text = title, Modulate = new Color(0.55f, 0.6f, 0.7f) });

        foreach (var e in entries)
        {
            string tag = e.LevelId == 21 ? "id30" : $"L{e.LevelId}";
            var button = new Button
            {
                Text = $"  {e.DisplayName}    ·  {tag}",
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"{e.ContainerFile}  ·  planet-name help id {e.PlanetNameStringId}",
            };
            if (e.LevelId == currentLevelId)
            {
                button.Modulate = new Color(1f, 0.9f, 0.5f);
            }

            int id = e.LevelId;
            button.Pressed += () =>
            {
                if (_hint is not null)
                {
                    _hint.Text = $"Loading {e.DisplayName}…";
                }

                EmitSignal(SignalName.PlanetChosen, id);
            };
            list.AddChild(button);
        }
    }
}

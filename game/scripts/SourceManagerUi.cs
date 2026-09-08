using Godot;
using OBP.Core;
using OBP.PS2;

namespace OneBigPackage;

/// <summary>
/// Lightweight application-level source screen. It owns no retail bytes and no
/// importer logic; it displays the current <see cref="ObpSourceLibrary"/> and
/// asks the host to attach/change/forget a source. World availability is passed
/// in as a set of source games so this UI does not depend on any game importer.
/// </summary>
public partial class SourceManagerUi : CanvasLayer
{
    public event Action<ObpSourceGame>? SelectRequested;
    public event Action<ObpSourceGame>? ForgetRequested;
    public event Action? BrowseWorldsRequested;

    private readonly Dictionary<ObpSourceGame, Label> _statusLabels = new();
    private readonly Dictionary<ObpSourceGame, Button> _selectButtons = new();
    private readonly Dictionary<ObpSourceGame, Button> _forgetButtons = new();
    private Label _message = null!;
    private Button _browseWorlds = null!;
    private IReadOnlySet<ObpSourceGame> _worldProviderGames = new HashSet<ObpSourceGame>();

    public void Populate(ObpSourceLibrary library, IReadOnlySet<ObpSourceGame> worldProviderGames)
    {
        Name = "SourceManager";
        _worldProviderGames = worldProviderGames;

        var dim = new ColorRect
        {
            Color = new Color(0.025f, 0.035f, 0.055f, 0.98f),
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(dim);

        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        margin.AddThemeConstantOverride("margin_left", 44);
        margin.AddThemeConstantOverride("margin_top", 30);
        margin.AddThemeConstantOverride("margin_right", 44);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        AddChild(margin);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        margin.AddChild(outer);

        outer.AddChild(new Label { Text = "One Big Package — Game Sources", ThemeTypeVariation = "HeaderLarge" });
        outer.AddChild(new Label
        {
            Text = "Attach your supported retail PS2 disc images. OBP remembers only their local paths and re-identifies them when it starts; the game data stays where it is.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.72f, 0.76f, 0.84f),
        });

        foreach (var definition in library.Definitions)
        {
            outer.AddChild(new HSeparator());
            BuildRow(outer, definition);
        }

        outer.AddChild(new HSeparator());
        _browseWorlds = new Button
        {
            Text = "Browse available worlds →",
            TooltipText = "Shows every destination exposed by a production world provider for an attached source.",
        };
        _browseWorlds.Pressed += () => BrowseWorldsRequested?.Invoke();
        outer.AddChild(_browseWorlds);

        _message = new Label
        {
            Text = "Select any or all three sources. Esc quits.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 48),
            Modulate = new Color(0.65f, 0.82f, 0.68f),
        };
        outer.AddChild(_message);

        Refresh(library);
    }

    public void Refresh(ObpSourceLibrary library)
    {
        foreach (var definition in library.Definitions)
        {
            var attached = library.Get(definition.Game);
            var status = _statusLabels[definition.Game];
            var select = _selectButtons[definition.Game];
            var forget = _forgetButtons[definition.Game];
            bool hasWorldProvider = _worldProviderGames.Contains(definition.Game);

            if (attached is null)
            {
                string capability = hasWorldProvider ? " · world provider ready" : " · importer pending";
                status.Text = $"Not attached · expects {definition.Authority.Serial} · {definition.Authority.BuildId}{capability}";
                status.Modulate = new Color(0.65f, 0.68f, 0.74f);
                select.Text = "Select ISO…";
                forget.Disabled = true;
            }
            else
            {
                string filename = System.IO.Path.GetFileName(attached.Path);
                string capability = hasWorldProvider ? " · worlds available" : " · source ready / importer pending";
                status.Text = $"✓ {attached.DiscSerial} · {attached.Identity.BuildId} · size ✓{capability}\n{filename}";
                status.TooltipText = attached.Path;
                status.Modulate = new Color(0.63f, 0.92f, 0.68f);
                select.Text = "Change…";
                forget.Disabled = false;
            }
        }

        _browseWorlds.Disabled = !library.Attached.Values.Any(source => _worldProviderGames.Contains(source.Game));
    }

    public void SetStatus(string text, bool error = false)
    {
        if (_message is null || !IsInstanceValid(_message))
        {
            return;
        }

        _message.Text = text;
        _message.Modulate = error ? new Color(1f, 0.52f, 0.48f) : new Color(0.65f, 0.9f, 0.68f);
    }

    private void BuildRow(VBoxContainer outer, ObpSourceDefinition definition)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        outer.AddChild(row);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 3);
        row.AddChild(info);

        info.AddChild(new Label { Text = definition.DisplayName, ThemeTypeVariation = "HeaderMedium" });
        var status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 42),
        };
        info.AddChild(status);
        _statusLabels[definition.Game] = status;

        var select = new Button { CustomMinimumSize = new Vector2(120, 0) };
        var forget = new Button { Text = "Forget", CustomMinimumSize = new Vector2(76, 0) };
        ObpSourceGame game = definition.Game;
        select.Pressed += () => SelectRequested?.Invoke(game);
        forget.Pressed += () => ForgetRequested?.Invoke(game);
        row.AddChild(select);
        row.AddChild(forget);
        _selectButtons[game] = select;
        _forgetButtons[game] = forget;
    }
}

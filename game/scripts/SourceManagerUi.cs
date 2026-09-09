using Godot;
using OBP.Core;
using OBP.PS2;

namespace OneBigPackage;

public partial class SourceManagerUi : CanvasLayer
{
    public event Action<ObpSourceGame>? SelectRequested;
    public event Action<ObpSourceGame>? ForgetRequested;
    public event Action? BrowseWorldsRequested;

    private readonly Dictionary<ObpSourceGame, Label> _statusLabels = new();
    private readonly Dictionary<ObpSourceGame, Button> _selectButtons = new();
    private readonly Dictionary<ObpSourceGame, Button> _forgetButtons = new();
    private readonly List<Button> _focusOrder = new();
    private Label _summary = null!;
    private Label _message = null!;
    private Button _browseWorlds = null!;
    private IReadOnlySet<ObpSourceGame> _worldProviderGames = new HashSet<ObpSourceGame>();

    public void Populate(ObpSourceLibrary library, IReadOnlySet<ObpSourceGame> worldProviderGames)
    {
        Name = "SourceManager";
        _worldProviderGames = worldProviderGames;

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
        outer.AddChild(new Label { Text = "ONE BIG PACKAGE  //  SOURCE BAY", ThemeTypeVariation = "HeaderSmall" });

        var heading = new HBoxContainer();
        heading.AddChild(new Label { Text = "GAME SOURCES", ThemeTypeVariation = "HeaderLarge" });
        heading.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _summary = new Label { VerticalAlignment = VerticalAlignment.Center, Modulate = UiTheme.TextDim };
        heading.AddChild(_summary);
        outer.AddChild(heading);

        outer.AddChild(new Label
        {
            Text = "Connect the trilogy you own, then jump straight into any world OBP can currently run. Retail data stays on this machine.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = UiTheme.TextDim,
        });

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var cards = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(cards);

        int index = 0;
        foreach (var definition in library.Definitions)
        {
            BuildCard(cards, definition, index++);
        }

        _browseWorlds = new Button
        {
            Text = "ENTER WORLDS  ›",
            CustomMinimumSize = new Vector2(0, 52),
            TooltipText = "Browse destinations exposed by ready world providers.",
            FocusMode = Control.FocusModeEnum.All,
        };
        _browseWorlds.Pressed += () => BrowseWorldsRequested?.Invoke();
        _focusOrder.Add(_browseWorlds);
        outer.AddChild(_browseWorlds);

        _message = new Label
        {
            Text = "ARROWS / D-PAD  Navigate    •    ENTER / CONFIRM  Select    •    ESC  Quit",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 42),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiTheme.TextDim,
        };
        outer.AddChild(_message);

        Refresh(library);
        Callable.From(FocusInitialControl).CallDeferred();
    }

    public void Refresh(ObpSourceLibrary library)
    {
        int attachedCount = 0;
        int playableCount = 0;
        foreach (var definition in library.Definitions)
        {
            var attached = library.Get(definition.Game);
            bool hasWorldProvider = _worldProviderGames.Contains(definition.Game);
            var status = _statusLabels[definition.Game];
            var select = _selectButtons[definition.Game];
            var forget = _forgetButtons[definition.Game];

            if (attached is null)
            {
                status.Text = hasWorldProvider
                    ? $"SOURCE NEEDED  •  WORLDS READY\nExpected {definition.Authority.Serial}  •  {definition.Authority.BuildId}"
                    : $"SOURCE NEEDED  •  RUNTIME PENDING\nExpected {definition.Authority.Serial}  •  {definition.Authority.BuildId}";
                status.Modulate = UiTheme.TextDim;
                select.Text = "ATTACH DISC IMAGE";
                forget.Disabled = true;
            }
            else
            {
                attachedCount++;
                if (hasWorldProvider) playableCount++;
                string filename = System.IO.Path.GetFileName(attached.Path);
                status.Text = hasWorldProvider
                    ? $"READY  •  WORLDS ONLINE\n{attached.DiscSerial}  •  {attached.Identity.BuildId}  •  {filename}"
                    : $"ATTACHED  •  RUNTIME PENDING\n{attached.DiscSerial}  •  {attached.Identity.BuildId}  •  {filename}";
                status.TooltipText = attached.Path;
                status.Modulate = hasWorldProvider ? UiTheme.Ready : UiTheme.Warning;
                select.Text = "CHANGE SOURCE";
                forget.Disabled = false;
            }
        }

        _summary.Text = $"{attachedCount}/3 SOURCES  •  {playableCount} READY TO PLAY";
        _browseWorlds.Disabled = playableCount == 0;
        _browseWorlds.Text = playableCount > 0 ? $"ENTER WORLDS  ›   {playableCount} GAME{(playableCount == 1 ? "" : "S")} READY" : "ENTER WORLDS  •  ATTACH A READY SOURCE FIRST";
        WireFocus();
    }

    public void SetStatus(string text, bool error = false)
    {
        if (_message is null || !IsInstanceValid(_message)) return;
        _message.Text = text;
        _message.Modulate = error ? new Color(1f, 0.52f, 0.48f) : UiTheme.Ready;
    }

    private void BuildCard(VBoxContainer parent, ObpSourceDefinition definition, int index)
    {
        var panel = new PanelContainer { ThemeTypeVariation = "MenuCard", CustomMinimumSize = new Vector2(0, 126) };
        parent.AddChild(panel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        panel.AddChild(row);

        var badge = new Label
        {
            Text = index switch { 0 => "I", 1 => "II", _ => "III" },
            ThemeTypeVariation = "HeaderLarge",
            CustomMinimumSize = new Vector2(58, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = index switch { 0 => UiTheme.GameOne, 1 => UiTheme.GameTwo, _ => UiTheme.GameThree },
        };
        row.AddChild(badge);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 5);
        row.AddChild(info);
        info.AddChild(new Label { Text = definition.DisplayName.ToUpperInvariant(), ThemeTypeVariation = "HeaderMedium" });
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 52) };
        info.AddChild(status);
        _statusLabels[definition.Game] = status;

        var actions = new VBoxContainer { CustomMinimumSize = new Vector2(176, 0) };
        actions.AddThemeConstantOverride("separation", 7);
        row.AddChild(actions);
        var select = new Button { CustomMinimumSize = new Vector2(176, 44), FocusMode = Control.FocusModeEnum.All };
        var forget = new Button { Text = "FORGET", CustomMinimumSize = new Vector2(176, 38), FocusMode = Control.FocusModeEnum.All };
        ObpSourceGame game = definition.Game;
        select.Pressed += () => SelectRequested?.Invoke(game);
        forget.Pressed += () => ForgetRequested?.Invoke(game);
        actions.AddChild(select);
        actions.AddChild(forget);
        _selectButtons[game] = select;
        _forgetButtons[game] = forget;
        _focusOrder.Add(select);
        _focusOrder.Add(forget);
    }

    private void FocusInitialControl()
    {
        if (IsInstanceValid(_browseWorlds) && !_browseWorlds.Disabled) _browseWorlds.GrabFocus();
        else _focusOrder.FirstOrDefault(button => IsInstanceValid(button) && !button.Disabled)?.GrabFocus();
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
}

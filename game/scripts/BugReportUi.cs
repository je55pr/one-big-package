using Godot;

namespace OBP.Game;

/// <summary>OBP reporting overlay, independent of source-game pause presentation.</summary>
public partial class BugReportUi : CanvasLayer
{
    public bool InboxMode { get; init; }
    public string DefaultTitle { get; init; } = "Gameplay bug";
    public event Action<string, string>? SubmitRequested;
    public event Action? CloseRequested;
    public event Action<int>? SelectionChanged;
    public event Action? ReplayRequested;
    public event Action? ExportRequested;
    public event Action? OpenFolderRequested;
    public event Action? ImportRequested;
    public event Action? DeleteRequested;
    private LineEdit? _title, _description;
    private Label _status = null!, _details = null!;
    private ItemList? _list;
    private Button _primary = null!;
    private readonly List<Button> _buttons = [];
    private readonly List<Button> _reportButtons = [];
    private Control? _busyReturnFocus;
    private bool _busy;
    internal bool Busy => _busy;
    internal string Status => _status.Text;

    public override void _Ready()
    {
        Name = InboxMode ? "BugReportInbox" : "BugReportForm";
        Layer = 310;
        ProcessMode = ProcessModeEnum.Always;
        var shade = new ColorRect
        {
            Color = new(0, 0, 0, .78f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(shade);
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new(580, 0), ThemeTypeVariation = "MenuCard" };
        centre.AddChild(panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);
        column.AddChild(new Label { Text = InboxMode ? "BUG REPORTS" : "REPORT BUG", ThemeTypeVariation = "HeaderLarge" });
        column.AddChild(new Label
        {
            Text = InboxMode ? "Saved locally · select a report to replay or export" : "Moment marked. Add a description, or just save.",
        });
        if (InboxMode) BuildInbox(column); else BuildForm(column);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(520, 38), MaxLinesVisible = 3 };
        column.AddChild(_status);
        column.AddChild(new Label
        {
            Text = InboxMode ? "Cross / Enter / Double-click  Replay     Circle / Esc  Back" : "Cross / Enter  Select     Circle / Esc  Back",
            Modulate = new(.7f, .76f, .8f),
        });
        MenuFocus.WireVertical((_title is null ? Array.Empty<Control>() : new Control[] { _title, _description! })
            .Concat(_list is null ? [] : new Control[] { _list }).Concat(_buttons));
        if (InboxMode) WireInboxFocus();
        Callable.From(() => MenuFocus.Restore(_list?.ItemCount > 0 ? _list : _primary, _buttons)).CallDeferred();
    }

    private void BuildForm(VBoxContainer column)
    {
        column.AddChild(new Label { Text = "Title" });
        _title = new LineEdit { Name = "ReportTitle", Text = DefaultTitle, MaxLength = 120 };
        column.AddChild(_title);
        column.AddChild(new Label { Text = "What happened? (optional)" });
        _description = new LineEdit { Name = "ReportDescription", PlaceholderText = "A short description helps identify the problem", MaxLength = 2000 };
        column.AddChild(_description);
        var row = new HBoxContainer();
        column.AddChild(row);
        _primary = AddButton(row, "SAVE REPORT", Submit);
        AddButton(row, "CANCEL", () => CloseRequested?.Invoke());
        _title.TextSubmitted += _ => Submit();
        _description.TextSubmitted += _ => Submit();
    }

    private void BuildInbox(VBoxContainer column)
    {
        _list = new ItemList
        {
            Name = "ReportList",
            CustomMinimumSize = new(520, 110),
            SelectMode = ItemList.SelectModeEnum.Single,
            MaxColumns = 1,
            AutoWidth = false,
            FixedColumnWidth = 500,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _list.Resized += () => _list.FixedColumnWidth = Mathf.Max(1, (int)_list.Size.X - 24);
        _list.ItemSelected += index => SelectionChanged?.Invoke((int)index);
        _list.ItemActivated += _ => { if (!_busy) ReplayRequested?.Invoke(); };
        column.AddChild(_list);
        _details = new Label { CustomMinimumSize = new(520, 42), AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 3 };
        column.AddChild(_details);
        var actions = new HBoxContainer();
        column.AddChild(actions);
        _primary = AddButton(actions, "REPLAY SELECTED", () => ReplayRequested?.Invoke());
        _reportButtons.Add(_primary);
        _reportButtons.Add(AddButton(actions, "EXPORT SELECTED ZIP", () => ExportRequested?.Invoke()));
        var row = new HBoxContainer();
        column.AddChild(row);
        AddButton(row, "OPEN FOLDER", () => OpenFolderRequested?.Invoke());
        AddButton(row, "IMPORT ZIP", () => ImportRequested?.Invoke());
        _reportButtons.Add(AddButton(row, "DELETE SESSION…", () => DeleteRequested?.Invoke()));
        AddButton(row, "BACK", () => CloseRequested?.Invoke());
    }

    private void WireInboxFocus()
    {
        // ItemList keeps its native row selection/scrolling. Directional focus
        // beyond the list follows the two visible action rows; Tab/R1 follows
        // the original sequential focus chain.
        _list!.FocusNeighborTop = _list.GetPathTo(_buttons[^1]);
        _list.FocusNeighborRight = _list.FocusNeighborBottom = _list.GetPathTo(_primary);
        _list.FocusNeighborLeft = _list.GetPathTo(_list);
        foreach (var row in new[] { _buttons.Take(2).ToArray(), _buttons.Skip(2).ToArray() })
            for (int i = 0; i < row.Length; i++)
            {
                row[i].FocusNeighborLeft = row[i].GetPathTo(row[(i + row.Length - 1) % row.Length]);
                row[i].FocusNeighborRight = row[i].GetPathTo(row[(i + 1) % row.Length]);
            }
        for (int i = 0; i < 2; i++)
        {
            _buttons[i].FocusNeighborTop = _buttons[i].GetPathTo(_list);
            _buttons[i].FocusNeighborBottom = _buttons[i].GetPathTo(_buttons[2 + i * 2]);
        }
        for (int i = 2; i < _buttons.Count; i++)
        {
            _buttons[i].FocusNeighborTop = _buttons[i].GetPathTo(_buttons[(i - 2) / 2]);
            _buttons[i].FocusNeighborBottom = _buttons[i].GetPathTo(_list);
        }
    }

    private Button AddButton(Node parent, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.Pressed += () => { if (!_busy) pressed(); };
        parent.AddChild(button);
        _buttons.Add(button);
        return button;
    }

    private void Submit()
    {
        if (!_busy) SubmitRequested?.Invoke(string.IsNullOrWhiteSpace(_title!.Text) ? DefaultTitle : _title.Text, _description!.Text);
    }

    public void SetBusy(bool busy, string status)
    {
        if (busy && !_busy) _busyReturnFocus = GetViewport().GuiGetFocusOwner();
        _busy = busy;
        UpdateActionAvailability();
        if (_list is not null) _list.ProcessMode = busy ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        if (_title is not null) _title.Editable = !busy;
        if (_description is not null) _description.Editable = !busy;
        _status.Text = status;
        if (!busy) MenuFocus.Restore(_busyReturnFocus, _buttons);
    }

    public void SetStatus(string text) => _status.Text = text;
    public void SetReports(IEnumerable<string> titles)
    {
        _list!.Clear();
        foreach (string title in titles)
        {
            int index = _list.AddItem(title);
            _list.SetItemTooltip(index, title);
        }
        UpdateActionAvailability();
        if (_list.ItemCount > 0) { _list.Select(0); SelectionChanged?.Invoke(0); }
        else _details.Text = "No saved reports yet. Press F8 during a recorded game, or Square while paused.";
    }
    private void UpdateActionAvailability()
    {
        foreach (var button in _buttons)
            button.Disabled = _busy || _list?.ItemCount == 0 && _reportButtons.Contains(button);
    }
    public void SetDetails(string text) { _details.Text = text; _details.TooltipText = text; }

    public override void _Input(InputEvent input)
    {
        if (GetTree().Root.GetEmbeddedSubwindows().Any(window => window.Visible)) return;
        if (input.IsActionPressed("ui_cancel") || input.IsActionPressed(ControllerBindings.Pause))
        {
            if (!_busy) CloseRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }
}

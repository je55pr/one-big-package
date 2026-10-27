using OBP.Core;
using Godot;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Effects;

namespace OBP.Game;

/// <summary>Common OBP catalogue UI. Source IDs, inventory and native actions stay behind the host ports.</summary>
public sealed partial class PauseMenuUi
{
    public Func<IReadOnlyList<EquipmentCatalogEntry>>? CatalogReader { get; init; }
    public Func<IReadOnlyList<SourceWeaponKey?>>? SlotReader { get; init; }
    public Func<string, EquipmentChangeResult>? CatalogEquip { get; init; }
    public Action<Exception> CatalogErrorReporter { get; init; } = error => GD.PushError("[pause] catalogue action failed: " + error);
    public Func<EquipmentChangeResult>? CatalogUnequip { get; init; }
    public Func<string, bool, EquipmentChangeResult>? CatalogGadget { get; init; }
    public Func<int, string?, EquipmentChangeResult>? CatalogAssign { get; init; }
    public Func<EquipmentCatalogEntry, Control?>? CatalogPreview { get; init; }
    public Func<EquipmentCatalogEntry, Texture2D?>? CatalogIcon { get; init; }
    public Func<IReadOnlyDictionary<string, string>>? ActorStatusReader { get; init; }
    public Func<string?>? SharedProfileStatusReader { get; init; }
    public Func<string?>? SaveNoticeReader { get; init; }
    public Func<string, EquipmentChangeResult>? ActorReset { get; init; }
    public Func<string?>? EffectStatusReader { get; init; }
    public Func<string, RuntimeEffectControlResult>? EffectControl { get; init; }
    public bool LabAvailable { get; init; }
    public event Action? LabRequested;
    public bool UniversalEnabled => CatalogReader is not null;
    public string UniversalSection => _universalSection;
    public int UniversalPage => _universalPage;
    public string? InspectedCatalogId => _inspectedCatalogId;
    private static readonly string[] UniversalSections = ["Weapons", "Gadgets", "Appearance", "Quick Select", "Settings", "Developer"];
    private const int UniversalPageSize = 6;
    private string _universalSection = "Weapons";
    private int _universalPage, _assignmentSlot;
    private string? _inspectedCatalogId;
    private VBoxContainer _universalBody = null!;
    private Label _catalogDetails = null!, _catalogStatus = null!, _catalogPage = null!;
    private HFlowContainer _catalogActions = null!;
    private Button _catalogPrevious = null!, _catalogNext = null!;
    private readonly List<Control> _universalFocus = [];
    private readonly List<Button> _universalTabs = [];
    private Control? _universalReturnFocus;
    private bool _catalogActionPending;

    private void BuildUniversal()
    {
        var background = new ColorRect { Color = new(0.025f, .045f, .075f, .95f), AnchorRight = 1, AnchorBottom = 1 };
        AddChild(background);
        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 16, OffsetTop = 12, OffsetRight = -16, OffsetBottom = -12 };
        AddChild(margin);
        var panel = new PanelContainer { ThemeTypeVariation = "MenuCard" }; margin.AddChild(panel);
        var column = new VBoxContainer(); panel.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        header.AddChild(new Label { Text = "ONE BIG PACKAGE  /  PAUSED", ThemeTypeVariation = "HeaderSmall", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _resume = MakeButton("RESUME"); _resume.Pressed += () => ResumeRequested?.Invoke(); header.AddChild(_resume);
        var tabs = new HFlowContainer(); column.AddChild(tabs);
        if (SaveNoticeReader?.Invoke() is { } status)
            column.AddChild(new Label { Name = "SharedProfileStatus", Text = status, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiTheme.TextDim });
        foreach (string section in UniversalSections)
        {
            var tab = MakeButton(section.ToUpperInvariant()); tab.Name = "ObpTab_" + section.Replace(" ", "");
            tab.CustomMinimumSize = new(118, 42); tab.Pressed += () => ShowUniversalSection(section);
            tabs.AddChild(tab); _universalTabs.Add(tab);
        }
        var scroll = new ScrollContainer
        {
            Name = "ObpCatalogScroll",
            FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(content);
        _universalBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; content.AddChild(_universalBody);
        _catalogDetails = new Label { Name = "ObpCatalogDetails", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(_catalogDetails);
        _catalogActions = new HFlowContainer(); content.AddChild(_catalogActions);
        var paging = new HBoxContainer(); content.AddChild(paging);
        _catalogPrevious = MakeButton("PREVIOUS"); _catalogPrevious.Pressed += () => ChangeUniversalPage(-1); paging.AddChild(_catalogPrevious);
        _catalogPage = new Label { HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; paging.AddChild(_catalogPage);
        _catalogNext = MakeButton("NEXT"); _catalogNext.Pressed += () => ChangeUniversalPage(1); paging.AddChild(_catalogNext);
        _catalogStatus = new Label { Name = "ObpCatalogStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiTheme.TextDim }; content.AddChild(_catalogStatus);
        column.AddChild(new Label { Text = "L1 / R1  Sections    Page Up / Down  Pages    Cross  Inspect / Confirm    Start  Resume    Circle  Back", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiTheme.TextDim });
        ShowUniversalSection("Weapons");
    }

    public void ShowUniversalSection(string section)
    {
        if (!UniversalSections.Contains(section)) throw new ArgumentException("Unknown pause section.");
        _universalSection = section; _universalPage = 0; _inspectedCatalogId = null;
        _catalogStatus.Text = "";
        RebuildUniversalPage();
    }

    private void ClearUniversalChildren(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private EquipmentCatalogEntry[] UniversalEntries() => CatalogReader!()
        .Where(entry => _universalSection switch
        {
            "Gadgets" => entry.Definition.Category == EquipmentCategory.Gadget,
            "Appearance" => entry.Definition.Category == EquipmentCategory.Appearance,
            _ => entry.Definition.Category == EquipmentCategory.Weapon,
        }).OrderBy(entry => entry.Definition.SourceGame)
        .ThenBy(entry => entry.Definition.DisplayName, StringComparer.Ordinal).ToArray();

    private void RebuildUniversalPage()
    {
        ClearUniversalChildren(_universalBody); ClearUniversalChildren(_catalogActions);
        _universalFocus.Clear(); _universalFocus.Add(_resume!); _universalFocus.AddRange(_universalTabs);
        _catalogDetails.Text = "";
        bool catalog = _universalSection is "Weapons" or "Gadgets" or "Appearance" or "Quick Select";
        _catalogPrevious.Visible = _catalogNext.Visible = _catalogPage.Visible = catalog;
        if (!catalog) { BuildUniversalOptions(); WireUniversalFocus(); return; }
        var entries = UniversalEntries();
        int pages = Math.Max(1, (entries.Length + UniversalPageSize - 1) / UniversalPageSize);
        _universalPage = Math.Clamp(_universalPage, 0, pages - 1);
        _catalogPage.Text = $"{_universalSection.ToUpperInvariant()}  ·  {_universalPage + 1} / {pages}";
        _catalogPrevious.Disabled = _universalPage == 0; _catalogNext.Disabled = _universalPage + 1 == pages;
        if (_universalSection == "Quick Select") BuildUniversalSlots();
        var visible = entries.Skip(_universalPage * UniversalPageSize).Take(UniversalPageSize).ToArray();
        foreach (var entry in visible)
        {
            string state = entry.State.Equipped ? "HELD" : entry.State.Selected ? "SELECTED" : entry.Usable ? "AVAILABLE" : "UNAVAILABLE";
            var card = MakeButton($"{entry.Definition.DisplayName}   [{OriginName(entry.Definition.SourceGame)}]\n{state}" +
                (entry.State.Ammo is { } ammo ? $"   {ammo}/{entry.State.Capacity}" : ""));
            card.Name = "ObpItem_" + entry.Definition.Id.Replace('/', '_');
            card.Icon = CatalogIcon?.Invoke(entry) ?? GD.Load<Texture2D>("res://assets/ui/obp-equipment-placeholder.svg");
            card.ExpandIcon = true; card.AddThemeConstantOverride("icon_max_width", 36);
            card.Alignment = HorizontalAlignment.Left; card.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            card.TooltipText = (EquipmentNotice(entry) ?? "Inspect equipment and choose an action.") +
                (entry.State.Icon == EquipmentResourceStatus.Available ? "" : "\nIcon unavailable.");
            card.CustomMinimumSize = new(0, 64);
            card.Pressed += () => InspectUniversalItem(entry.Definition.Id);
            _universalBody.AddChild(card); _universalFocus.Add(card);
        }
        if (entries.Length == 0) _universalBody.AddChild(new Label { Text = "No catalogue entries in this section.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (_inspectedCatalogId is null || !visible.Any(entry => entry.Definition.Id == _inspectedCatalogId)) _inspectedCatalogId = visible.FirstOrDefault()?.Definition.Id;
        if (_inspectedCatalogId is { } selected) InspectUniversalItem(selected, restoreFocus: false);
        WireUniversalFocus();
        var first = _universalFocus.FirstOrDefault(control => control.Name.ToString().StartsWith("ObpItem_", StringComparison.Ordinal));
        _universalReturnFocus = first ?? _universalTabs[Array.IndexOf(UniversalSections, _universalSection)];
        RestoreUniversalFocus();
    }

    private static string OriginName(ObpSourceGame source) => source switch { ObpSourceGame.Rac1 => "R&C1", ObpSourceGame.Rac2 => "GC", ObpSourceGame.Rac3 => "UYA", _ => SourceIdentityWire.FormatSource(source).ToUpperInvariant() };

    private string? EquipmentNotice(EquipmentCatalogEntry entry) => DebugOutputEnabled ? entry.Reason : entry.Usability switch
    {
        EquipmentUsability.Unimplemented => "This equipment is not implemented yet.",
        EquipmentUsability.MissingSource => "This equipment is temporarily unavailable.",
        EquipmentUsability.UnsupportedBuild => "This equipment is not supported in this build.",
        EquipmentUsability.MissingModel => "This appearance is not available yet.",
        EquipmentUsability.Unowned => "This equipment is not in your loadout.",
        EquipmentUsability.ContextBlocked => "This equipment cannot be used here.",
        _ => null,
    };

    private void BuildUniversalSlots()
    {
        var slots = SlotReader?.Invoke() ?? [];
        var entries = CatalogReader!();
        var row = new HFlowContainer(); _universalBody.AddChild(row);
        for (int index = 0; index < slots.Count; index++)
        {
            int slot = index;
            string name = slots[index] is { } key ? entries.SingleOrDefault(entry => entry.Definition.Item == key)?.Definition.DisplayName ?? key.ToString() : "Empty";
            var button = MakeButton($"{index + 1}: {name}"); button.Name = $"ObpQuickSlot_{slot}";
            button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; button.CustomMinimumSize = new(160, 44);
            button.TooltipText = $"Assign a weapon to direction {index + 1}. Select a weapon below, then ASSIGN.";
            button.Pressed += () => { _assignmentSlot = slot; _catalogStatus.Text = $"Editing Quick Select direction {slot + 1}."; if (_inspectedCatalogId is { } id) InspectUniversalItem(id); };
            row.AddChild(button); _universalFocus.Add(button);
        }
    }

    private void InspectUniversalItem(string id, bool restoreFocus = true)
    {
        var entry = CatalogReader!().SingleOrDefault(item => item.Definition.Id == id);
        if (entry is null) { _catalogStatus.Text = "This catalogue entry is no longer available."; return; }
        _inspectedCatalogId = id;
        _universalFocus.RemoveAll(control => control.GetParent() == _catalogActions);
        ClearUniversalChildren(_catalogActions);
        // The source/build provenance is useful when two games use the same name.
        _catalogDetails.Text = $"{entry.Definition.DisplayName}  ·  {OriginName(entry.Definition.SourceGame)}\n" +
            (EquipmentNotice(entry) ?? (entry.State.Equipped ? "Currently held." : entry.State.Selected ? "Selected in this player loadout." : "Available in this player loadout."));
        if (entry.Definition.Category == EquipmentCategory.Gadget) _catalogDetails.Text += "\n" + entry.Definition.Usage switch
        { EquipmentUsage.Backpack => "Movement pack", EquipmentUsage.Passive => "Passive equipment", EquipmentUsage.ContextAction => "Requires a supported world interaction", _ => "Held gadget" };
        var oldPreview = _universalBody.GetNodeOrNull<Control>("EquipmentPreview");
        if (oldPreview is not null) { _universalBody.RemoveChild(oldPreview); oldPreview.QueueFree(); }
        if (entry.Definition.Category == EquipmentCategory.Appearance && entry.Selectable && CatalogPreview?.Invoke(entry) is { } preview)
        { preview.Name = "EquipmentPreview"; _universalBody.AddChild(preview); }
        if (_universalSection == "Quick Select")
        {
            AddUniversalAction($"ASSIGN TO DIRECTION {_assignmentSlot + 1}", entry.Selectable, () => CatalogAssign?.Invoke(_assignmentSlot, id));
            AddUniversalAction("CLEAR DIRECTION", true, () => CatalogAssign?.Invoke(_assignmentSlot, null));
        }
        else if (entry.Definition.Category == EquipmentCategory.Gadget)
            AddUniversalAction(entry.State.Selected ? "DISABLE" : "ENABLE", entry.State.Selected || entry.Selectable,
                () => CatalogGadget?.Invoke(id, !entry.State.Selected));
        else
        {
            AddUniversalAction(entry.Definition.Category == EquipmentCategory.Appearance ? "SELECT APPEARANCE" : "EQUIP", entry.Selectable, () => CatalogEquip?.Invoke(id));
            if (entry.Definition.Category == EquipmentCategory.Weapon)
                AddUniversalAction("HOLD WRENCH", true, () => CatalogUnequip?.Invoke());
        }
        if (_catalogDetails.Text.Length > 0) _catalogDetails.TooltipText = DebugOutputEnabled ? entry.Definition.BuildId : "";
        WireUniversalFocus();
        if (restoreFocus)
        {
            _universalReturnFocus = _universalFocus.FirstOrDefault(control => control.GetParent() == _catalogActions && MenuFocus.Available(control)) ?? _universalReturnFocus;
            RestoreUniversalFocus();
        }
    }

    private void AddUniversalAction(string text, bool enabled, Func<EquipmentChangeResult?> action)
    {
        var button = MakeButton(text); button.Disabled = !enabled;
        button.Pressed += () =>
        {
            if (_catalogActionPending) return;
            _catalogActionPending = true;
            EquipmentChangeResult result;
            try { result = action() ?? new(false, "This action is unavailable."); }
            catch (Exception error)
            {
                _catalogActionPending = false;
                _catalogStatus.Text = "The change could not be applied.";
                CatalogErrorReporter(error);
                return;
            }
            _catalogStatus.Text = (DebugOutputEnabled ? result.Reason : null) ?? (result.Accepted ? "Loadout updated." : "The change could not be applied.");
            Callable.From(() => { if (!IsInsideTree() || IsQueuedForDeletion()) return; _catalogActionPending = false; RebuildUniversalPage(); }).CallDeferred();
        };
        _catalogActions.AddChild(button); _universalFocus.Add(button);
    }

    private void BuildUniversalOptions()
    {
        void Option(string label, Action action)
        {
            var button = MakeButton(label); button.Pressed += action;
            _universalBody.AddChild(button); _universalFocus.Add(button);
        }
        if (_universalSection == "Settings")
        {
            Option("CONTROLLER SETTINGS", () => ControllerSettingsRequested?.Invoke());
            Option("RETURN TO WORLDS", () => WorldsRequested?.Invoke());
            if (LabAvailable) Option("ENTER FUSION LAB", () => LabRequested?.Invoke());
            Option("QUIT TO DESKTOP", () => QuitRequested?.Invoke());
        }
        else
        {
            if (SharedProfileStatusReader?.Invoke() is { } profileStatus)
                _universalBody.AddChild(new Label { Name = "ProfileDiagnostics", Text = profileStatus, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            var debug = new CheckButton { Text = "SHOW DEBUG OUTPUT", ButtonPressed = DebugOutputEnabled, CustomMinimumSize = new(0, 48) };
            debug.Toggled += enabled => { DebugOutputEnabled = enabled; DebugOutputChanged?.Invoke(enabled); }; _universalBody.AddChild(debug); _universalFocus.Add(debug);
            if (MovementStatesAvailable)
            {
                Option("SAVE MOVEMENT STATE", () => SaveMovementStateRequested?.Invoke());
                Option("LOAD MOVEMENT STATE", () => LoadMovementStateRequested?.Invoke());
            }
            if (LabAvailable) Option("ENTER FUSION LAB", () => LabRequested?.Invoke());
            foreach (var actor in ActorStatusReader?.Invoke() ?? new Dictionary<string, string>())
            {
                var status = new Label { Text = actor.Value, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                _universalBody.AddChild(status);
                Option("RESET " + actor.Key.ToUpperInvariant(), () =>
                {
                    var result = ActorReset?.Invoke(actor.Key);
                    _catalogStatus.Text = result?.Reason ?? (result?.Accepted == true ? "Actor reset." : "Reset is unavailable.");
                });
            }
            if (EffectControl is { } effectControl)
            {
                _universalBody.AddChild(new Label { Name = "EffectsStatus", Text = EffectStatusReader?.Invoke() ?? "Effects unavailable.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
                Option("START NATIVE EFFECTS EXPERIMENT", () => _catalogStatus.Text = effectControl("start").Reason);
                Option("RESET NATIVE EFFECTS EXPERIMENT", () => _catalogStatus.Text = effectControl("reset").Reason);
            }
        }
        _universalReturnFocus = _universalFocus.Skip(1 + _universalTabs.Count).FirstOrDefault() ?? _resume;
        RestoreUniversalFocus();
    }

    private void WireUniversalFocus()
    {
        var order = _universalFocus.Concat(new Control[] { _catalogPrevious, _catalogNext }).Where(MenuFocus.Available).ToArray();
        MenuFocus.WireVertical(order);
    }

    private void RestoreUniversalFocus() => MenuFocus.Restore(_universalReturnFocus, _universalFocus);

    private void ChangeUniversalPage(int direction)
    {
        _universalPage += direction; _inspectedCatalogId = null; RebuildUniversalPage();
    }

    private void HandleUniversalInput(InputEvent input)
    {
        if (input is InputEventKey { Echo: true }) return;
        if (input.IsActionPressed(ControllerBindings.Pause) || input.IsActionPressed("ui_cancel"))
        { GetViewport().SetInputAsHandled(); ResumeRequested?.Invoke(); return; }
        if (input is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.LeftShoulder or JoyButton.RightShoulder } shoulder)
        {
            int direction = shoulder.ButtonIndex == JoyButton.LeftShoulder ? -1 : 1;
            int index = Array.IndexOf(UniversalSections, _universalSection);
            GetViewport().SetInputAsHandled(); ShowUniversalSection(UniversalSections[(index + direction + UniversalSections.Length) % UniversalSections.Length]);
        }
        else if (input is InputEventKey { Pressed: true, Keycode: Key.Pageup or Key.Pagedown } page)
        { GetViewport().SetInputAsHandled(); ChangeUniversalPage(page.Keycode == Key.Pageup ? -1 : 1); }
    }

    private void HandleUniversalPreGuiInput(InputEvent input)
    {
        // Shoulder buttons also have Godot focus bindings. Section navigation
        // must own them before a focused Control consumes ui_focus_next/prev.
        if (UniversalEnabled && Visible &&
            (input is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.LeftShoulder or JoyButton.RightShoulder } ||
             input is InputEventKey { Pressed: true, Keycode: Key.Pageup or Key.Pagedown }))
            HandleUniversalInput(input);
    }
}

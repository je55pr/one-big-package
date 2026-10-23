using Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

public sealed partial class PauseMenuUi : CanvasLayer
{
    public event Action? ResumeRequested;
    public event Action? ControllerSettingsRequested;
    public event Action? WorldsRequested;
    public event Action? QuitRequested;
    public event Action? SaveMovementStateRequested;
    public event Action? LoadMovementStateRequested;
    public event Action? WeaponPreviewRequested;
    public event Action? RefillPreviewRequested;
    public event Action? PackPreviewRequested;
    public event Action? SwingshotPreviewRequested;
    public event Action<bool>? DebugOutputChanged;
    public bool DebugOutputEnabled { get; init; }
    public string EquipmentStatus { get; init; } = string.Empty;
    public bool WeaponPreviewEnabled { get; init; }
    public bool MovementStatesAvailable { get; init; }
    private Label? _movementStatus;
    private Button? _resume;
    private readonly List<Control> _focusOrder = [];

    public void RestoreFocus()
    {
        if (RetailEnabled) { RestoreRetailFocus(); return; }
        MenuFocus.Restore(_resume, _focusOrder);
    }

    public void SetMovementStatus(string text)
    {
        if (_movementStatus is not null) _movementStatus.Text = text;
    }

    public override void _Ready()
    {
        Name = "PauseMenu";
        Layer = 250;
        ProcessMode = ProcessModeEnum.Always;
        if (RetailEnabled) { BuildRetail(); return; }

        AddChild(new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        });

        var centre = new CenterContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(centre);

        var panel = new PanelContainer
        {
            ThemeTypeVariation = "MenuCard",
            CustomMinimumSize = new Vector2(460, 0),
        };
        centre.AddChild(panel);

        var scroll = new ScrollContainer
        {
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(460, Mathf.Clamp(GetViewport().GetVisibleRect().Size.Y - 100, 220, 660)),
        };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(column);
        column.AddChild(new Label
        {
            Text = "PAUSED",
            ThemeTypeVariation = "HeaderLarge",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        column.AddChild(new Label
        {
            Text = "One Big Package",
            ThemeTypeVariation = "HeaderSmall",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var resume = MakeButton("RESUME");
        _resume = resume;
        var controller = MakeButton("CONTROLLER SETTINGS");
        var worlds = MakeButton("RETURN TO WORLDS");
        var quit = MakeButton("QUIT TO DESKTOP");

        resume.Pressed += () => ResumeRequested?.Invoke();
        controller.Pressed += () => ControllerSettingsRequested?.Invoke();
        worlds.Pressed += () => WorldsRequested?.Invoke();
        quit.Pressed += () => QuitRequested?.Invoke();

        column.AddChild(resume);
        column.AddChild(controller);
        _focusOrder.AddRange([resume, controller]);
        var debugOutput = new CheckButton
        {
            Name = "DebugOutputToggle",
            Text = "SHOW DEBUG OUTPUT",
            ButtonPressed = DebugOutputEnabled,
            CustomMinimumSize = new Vector2(0, 44),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Show world, movement and input diagnostics, development notices and inspection shortcuts.",
        };
        debugOutput.Toggled += enabled => DebugOutputChanged?.Invoke(enabled);
        column.AddChild(debugOutput);
        _focusOrder.Add(debugOutput);
        if (MovementStatesAvailable)
        {
            var weapons = MakeButton("TOGGLE WEAPON PREVIEW");
            var refill = MakeButton("REFILL PREVIEW AMMO");
            refill.Disabled = !WeaponPreviewEnabled;
            var pack = MakeButton("CYCLE MOVEMENT PACK");
            var swingshot = MakeButton("TOGGLE SWINGSHOT PREVIEW");
            weapons.Pressed += () => WeaponPreviewRequested?.Invoke();
            refill.Pressed += () => RefillPreviewRequested?.Invoke();
            pack.Pressed += () => PackPreviewRequested?.Invoke();
            swingshot.Pressed += () => SwingshotPreviewRequested?.Invoke();
            column.AddChild(new Label { Text = "TESTING EQUIPMENT", ThemeTypeVariation = "HeaderSmall" });
            column.AddChild(new Label { Text = EquipmentStatus, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            foreach (var control in new[] { weapons, refill, pack, swingshot }) { column.AddChild(control); _focusOrder.Add(control); }
            var save = MakeButton("SAVE MOVEMENT STATE  (F12)");
            var load = MakeButton("LOAD MOVEMENT STATE  (SHIFT+F12)");
            save.TooltipText = "Save a stationary player position, facing, camera and movement equipment for reproducing a bug.";
            load.TooltipText = "Restore position, camera, inventory, defeated enemies, broken crates, cargo and transformations.";
            save.Pressed += () => SaveMovementStateRequested?.Invoke();
            load.Pressed += () => LoadMovementStateRequested?.Invoke();
            var movementButtons = new HBoxContainer();
            save.Text = "SAVE STATE  (F12)";
            load.Text = "LOAD  (SHIFT+F12)";
            save.SizeFlagsHorizontal = load.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            movementButtons.AddChild(save);
            movementButtons.AddChild(load);
            column.AddChild(movementButtons);
            _focusOrder.AddRange([save, load]);
            _movementStatus = new Label
            {
                Text = "Save and restore your test setup, including defeated enemies and broken crates.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(420, 0),
            };
            column.AddChild(_movementStatus);
        }
        column.AddChild(worlds);
        column.AddChild(quit);
        _focusOrder.AddRange([worlds, quit]);
        MenuFocus.WireVertical(_focusOrder);

        var hint = new Label
        {
            Text = "Start  Resume   ·   Cross  Select   ·   Triangle / Circle / Esc  Back",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiTheme.TextDim,
        };
        column.AddChild(hint);

        RestoreFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (RetailEnabled) { HandleRetailInput(@event); return; }
        if (MovementStatesAvailable && @event is InputEventKey { Keycode: Key.F12, Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.ShiftPressed) LoadMovementStateRequested?.Invoke();
            else SaveMovementStateRequested?.Invoke();
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            ResumeRequested?.Invoke();
        }
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(0, 48),
        FocusMode = Control.FocusModeEnum.All,
    };
}

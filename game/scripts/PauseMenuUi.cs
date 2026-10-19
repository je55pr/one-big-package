using Godot;

namespace OneBigPackage;

public sealed partial class PauseMenuUi : CanvasLayer
{
    public event Action? ResumeRequested;
    public event Action? ControllerSettingsRequested;
    public event Action? WorldsRequested;
    public event Action? QuitRequested;
    public event Action? SaveMovementStateRequested;
    public event Action? LoadMovementStateRequested;
    public bool MovementStatesAvailable { get; init; }
    private Label? _movementStatus;

    public void SetMovementStatus(string text)
    {
        if (_movementStatus is not null) _movementStatus.Text = text;
    }

    public override void _Ready()
    {
        Name = "PauseMenu";
        Layer = 250;
        ProcessMode = ProcessModeEnum.Always;

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

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
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
        var controller = MakeButton("CONTROLLER SETTINGS");
        var worlds = MakeButton("RETURN TO WORLDS");
        var quit = MakeButton("QUIT TO DESKTOP");

        resume.Pressed += () => ResumeRequested?.Invoke();
        controller.Pressed += () => ControllerSettingsRequested?.Invoke();
        worlds.Pressed += () => WorldsRequested?.Invoke();
        quit.Pressed += () => QuitRequested?.Invoke();

        column.AddChild(resume);
        column.AddChild(controller);
        if (MovementStatesAvailable)
        {
            var save = MakeButton("SAVE MOVEMENT STATE  (F12)");
            var load = MakeButton("LOAD MOVEMENT STATE  (SHIFT+F12)");
            save.TooltipText = "Save a stationary player position, facing, camera and movement equipment for reproducing a bug.";
            load.TooltipText = "Restore the saved movement setup in a fresh level. Enemies and crates restart; campaign progress is not restored.";
            save.Pressed += () => SaveMovementStateRequested?.Invoke();
            load.Pressed += () => LoadMovementStateRequested?.Invoke();
            var movementButtons = new HBoxContainer();
            save.Text = "SAVE STATE  (F12)";
            load.Text = "LOAD  (SHIFT+F12)";
            save.SizeFlagsHorizontal = load.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            movementButtons.AddChild(save);
            movementButtons.AddChild(load);
            column.AddChild(movementButtons);
            _movementStatus = new Label
            {
                Text = "Saves cover position, camera and equipment. Loading restarts enemies and crates.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(420, 0),
            };
            column.AddChild(_movementStatus);
        }
        column.AddChild(worlds);
        column.AddChild(quit);

        var hint = new Label
        {
            Text = "ESC / BACK  Resume",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiTheme.TextDim,
        };
        column.AddChild(hint);

        Callable.From(resume.GrabFocus).CallDeferred();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
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

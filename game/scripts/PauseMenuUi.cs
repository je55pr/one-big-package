using Godot;

namespace OneBigPackage;

public sealed partial class PauseMenuUi : CanvasLayer
{
    public event Action? ResumeRequested;
    public event Action? ControllerSettingsRequested;
    public event Action? WorldsRequested;
    public event Action? QuitRequested;

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

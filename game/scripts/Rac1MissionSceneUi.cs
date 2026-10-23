using Godot;
using OBP.RAC1.Progression;

namespace OneBigPackage;

/// <summary>
/// OBP scene-completion adapter. Native talk records select the scene/movie;
/// this acknowledgement card does not pretend to reproduce streamed cinematics.
/// </summary>
public sealed partial class Rac1MissionSceneUi : CanvasLayer
{
    internal event Action<Rac1MissionSceneRequest>? Completed;
    internal Rac1MissionSceneRequest Request { get; private set; } = null!;
    private Label _title = null!;
    private Button _continue = null!;

    public override void _Ready()
    {
        Layer = 24;
        ProcessMode = ProcessModeEnum.Always;
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, .6f), AnchorRight = 1, AnchorBottom = 1 };
        AddChild(backdrop);
        var panel = new PanelContainer { CustomMinimumSize = new(430, 150) };
        backdrop.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        var column = new VBoxContainer();
        panel.AddChild(column);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "HeaderMedium" };
        column.AddChild(_title);
        _continue = new Button { Text = "CONTINUE", CustomMinimumSize = new(260, 48) };
        _continue.Pressed += () => Completed?.Invoke(Request);
        column.AddChild(_continue);
    }

    internal void Present(Rac1MissionSceneRequest request)
    {
        Request = request;
        _title.Text = request.Kind == Rac1MissionSceneKind.Movie ? "INFOBOT TRANSMISSION" : "CONVERSATION";
        _continue.GrabFocus();
    }
}

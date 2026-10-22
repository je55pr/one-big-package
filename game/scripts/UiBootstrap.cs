using Godot;

namespace OneBigPackage;

/// <summary>
/// Autoload that installs the application-wide <see cref="UiTheme"/> on the root
/// window, controller UI bindings and focus-repeat adapter before the main scene.
/// </summary>
public partial class UiBootstrap : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UiTheme.Apply(GetTree().Root);
        GetTree().Root.GuiEmbedSubwindows = true;
        ControllerBindings.Install();
        AddChild(new ControllerMenuNavigation());

        // The engine appends "(DEBUG)" to the window title for non-exported
        // builds; the showcase launcher is one, so pin a clean title.
        GetWindow().Title = "One Big Package";
    }

}

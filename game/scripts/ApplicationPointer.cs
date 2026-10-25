using Godot;

namespace OBP.Game;

/// <summary>
/// Logical pointer mode for gameplay/UI. Automated input can exercise the same
/// transitions while the real desktop cursor stays visible and unrestricted.
/// </summary>
public static class ApplicationPointer
{
    private static bool? _automated;
    private static Input.MouseModeEnum _requestedMode = Input.MouseModeEnum.Visible;
    public static bool IsAutomated => _automated ??= DisplayServer.GetName() == "headless" ||
        AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs());

    public static Input.MouseModeEnum MouseMode
    {
        get => IsAutomated ? _requestedMode : Input.MouseMode;
        set
        {
            _requestedMode = value;
            Input.MouseMode = ResolveDesktopMode(value, IsAutomated);
        }
    }

    public static Input.MouseModeEnum ResolveDesktopMode(Input.MouseModeEnum requested, bool automated) =>
        automated ? Input.MouseModeEnum.Visible : requested;

    public static void IsolateAutomatedWindow(Window window)
    {
        if (!IsAutomated) return;
        window.Unfocusable = true;
        window.MousePassthrough = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}

using Godot;

namespace OneBigPackage;

/// <summary>
/// Retains the most recent *physical* pointer modality without disturbing the
/// game's captured-mouse camera or synthesised UI navigation actions.
/// </summary>
public sealed partial class UiPointerModality : Node
{
    private static bool _controllerLast;
    private static bool _hiddenForController;
    public static bool ControllerWasLastInput => _controllerLast;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    /// <summary>Call after opening a pointer-capable menu.</summary>
    public static void MenuOpened()
    {
        _hiddenForController = _controllerLast;
        Input.MouseMode = _controllerLast ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
    }

    /// <summary>Restore a gameplay/input mode when the owning menu closes.</summary>
    public static void MenuClosed(Input.MouseModeEnum previous)
    {
        _hiddenForController = false;
        Input.MouseMode = previous;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventJoypadButton { Pressed: true } ||
            @event is InputEventJoypadMotion axis && Math.Abs(axis.AxisValue) >= 0.32f)
        {
            _controllerLast = true;
            if (Input.MouseMode is Input.MouseModeEnum.Visible or Input.MouseModeEnum.Hidden)
            {
                Input.MouseMode = Input.MouseModeEnum.Hidden;
                _hiddenForController = true;
            }
        }
        else if (@event is InputEventMouseMotion motion && motion.Relative.LengthSquared() >= 4f ||
                 @event is InputEventMouseButton { Pressed: true })
        {
            _controllerLast = false;
            if (_hiddenForController && Input.MouseMode == Input.MouseModeEnum.Hidden)
                Input.MouseMode = Input.MouseModeEnum.Visible;
            _hiddenForController = false;
        }
    }
}

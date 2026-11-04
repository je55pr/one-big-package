using Godot;

namespace OBP.Game;

/// <summary>
/// Retains the most recent *physical* pointer modality without disturbing the
/// game's captured-mouse camera or synthesised UI navigation actions.
/// </summary>
public sealed partial class UiPointerModality : Node
{
    private static bool _controllerLast;
    private static bool _hiddenForController;
    private static ulong _ignoreMouseUntil;
    public static bool ControllerWasLastInput => _controllerLast;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    /// <summary>Call after opening a pointer-capable menu.</summary>
    public static void MenuOpened()
    {
        // A pause activated by Start must never expose a previous mouse-hover
        // position. Only fresh physical mouse movement re-enables the pointer.
        _controllerLast = true;
        _hiddenForController = true;
        _ignoreMouseUntil = Time.GetTicksMsec() + 260;
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Hidden;
    }

    /// <summary>Restore a gameplay/input mode when the owning menu closes.</summary>
    public static void MenuClosed(Input.MouseModeEnum previous)
    {
        _hiddenForController = false;
        ApplicationPointer.MouseMode = previous;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventJoypadButton { Pressed: true } ||
            @event is InputEventJoypadMotion axis && Math.Abs(axis.AxisValue) >= 0.32f)
        {
            _controllerLast = true;
            if (ApplicationPointer.MouseMode is Input.MouseModeEnum.Visible or Input.MouseModeEnum.Hidden)
            {
                ApplicationPointer.MouseMode = Input.MouseModeEnum.Hidden;
                _hiddenForController = true;
            }
        }
        else if (Time.GetTicksMsec() >= _ignoreMouseUntil &&
                 (@event is InputEventMouseMotion motion && motion.Relative.LengthSquared() >= 4f ||
                  @event is InputEventMouseButton { Pressed: true }))
        {
            _controllerLast = false;
            if (_hiddenForController && ApplicationPointer.MouseMode == Input.MouseModeEnum.Hidden)
                ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
            _hiddenForController = false;
        }
    }
}

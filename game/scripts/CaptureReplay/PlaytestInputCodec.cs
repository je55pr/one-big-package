using Godot;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal static class PlaytestInputCodec
{
    internal static PlaytestInputEvent? Capture(InputEvent input, ulong time) => input switch
    {
        InputEventKey key => new(PlaytestInputKind.Key, time, (int)key.Keycode, (int)key.PhysicalKeycode,
            key.Pressed, key.Echo, Modifiers(key)),
        InputEventJoypadButton button => new(PlaytestInputKind.JoyButton, time, (int)button.ButtonIndex, Pressed: button.Pressed),
        InputEventJoypadMotion axis => new(PlaytestInputKind.JoyAxis, time, (int)axis.Axis, Value: axis.AxisValue),
        InputEventMouseButton button => new(PlaytestInputKind.MouseButton, time, (int)button.ButtonIndex,
            Pressed: button.Pressed, Modifiers: Modifiers(button), X: button.Position.X, Y: button.Position.Y, DoubleClick: button.DoubleClick),
        InputEventMouseMotion mouse => new(PlaytestInputKind.MouseMotion, time,
            Modifiers: Modifiers(mouse), X: mouse.Position.X, Y: mouse.Position.Y,
            RelativeX: mouse.Relative.X, RelativeY: mouse.Relative.Y),
        InputEventAction action => new(PlaytestInputKind.Action, time, Pressed: action.Pressed,
            Value: action.Strength, Action: action.Action.ToString()),
        _ => null,
    };

    internal static InputEvent Restore(PlaytestInputEvent input)
    {
        input.Validate();
        InputEvent result = input.Kind switch
        {
            PlaytestInputKind.Key => new InputEventKey { Keycode = (Key)input.Code, PhysicalKeycode = (Key)input.PhysicalCode, Pressed = input.Pressed, Echo = input.Echo },
            PlaytestInputKind.JoyButton => new InputEventJoypadButton { ButtonIndex = (JoyButton)input.Code, Pressed = input.Pressed },
            PlaytestInputKind.JoyAxis => new InputEventJoypadMotion { Axis = (JoyAxis)input.Code, AxisValue = input.Value },
            PlaytestInputKind.MouseButton => new InputEventMouseButton { ButtonIndex = (MouseButton)input.Code, Pressed = input.Pressed, DoubleClick = input.DoubleClick, Position = new(input.X, input.Y), GlobalPosition = new(input.X, input.Y) },
            PlaytestInputKind.MouseMotion => new InputEventMouseMotion { Position = new(input.X, input.Y), GlobalPosition = new(input.X, input.Y), Relative = new(input.RelativeX, input.RelativeY) },
            PlaytestInputKind.Action => new InputEventAction { Action = input.Action!, Pressed = input.Pressed, Strength = input.Value },
            _ => throw new InvalidDataException("Unsupported recorded input."),
        };
        if (result is InputEventWithModifiers modifiers)
        {
            modifiers.ShiftPressed = (input.Modifiers & 1) != 0;
            modifiers.CtrlPressed = (input.Modifiers & 2) != 0;
            modifiers.AltPressed = (input.Modifiers & 4) != 0;
            modifiers.MetaPressed = (input.Modifiers & 8) != 0;
        }
        return result;
    }

    private static int Modifiers(InputEventWithModifiers value) =>
        (value.ShiftPressed ? 1 : 0) | (value.CtrlPressed ? 2 : 0) | (value.AltPressed ? 4 : 0) | (value.MetaPressed ? 8 : 0);

    internal static PlaytestPlayerInput Capture(RawPlayerInputFrame p) => new(p.Move.X, p.Move.Y,
        p.CameraIntent.X, p.CameraIntent.Y, p.JumpHeld, p.CrouchHeld, p.ActionJustPressed,
        p.TraversalHeld, p.EquippedUseJustPressed, p.ActionHeld);

    internal static RawPlayerInputFrame Restore(PlaytestPlayerInput p) => new(new(p.MoveX, p.MoveY),
        new(p.CameraX, p.CameraY), p.Jump, p.Crouch, p.ActionPressed, null, p.UseHeld, p.UsePressed, p.ActionHeld);
}

using Godot;

namespace OneBigPackage;

/// <summary>SDL logical buttons, labelled for a PlayStation controller. Host UI policy only.</summary>
internal static class ControllerBindings
{
    public static readonly StringName Pause = "obp_pause";
    public static readonly StringName QuickSelect = "obp_quick_select";
    public static readonly StringName CameraCenter = "obp_camera_center";
    public static readonly StringName CrouchAlternate = "obp_rac1_crouch_alternate";
    public const string MenuHint = "D-pad / left stick  Navigate   ·   Cross / Enter  Select   ·   Triangle / Circle / Esc  Back";
    public static bool MenuNavigationSuspended { get; set; }
    public static StringName PadAction(string action) => new("obp_pad_" + action);

    public static void Install()
    {
        // Godot's desktop defaults omit joypad accept/cancel. Do not depend on
        // platform defaults or physical DS3 driver button numbers.
        Button("ui_accept", JoyButton.A);
        Button("ui_cancel", JoyButton.B);
        Button("ui_cancel", JoyButton.Y);
        Button("ui_focus_next", JoyButton.RightShoulder);
        Button("ui_focus_prev", JoyButton.LeftShoulder);
        Direction("ui_left", RawGamepadInput.MoveLeft, JoyButton.DpadLeft, JoyAxis.LeftX, -1);
        Direction("ui_right", RawGamepadInput.MoveRight, JoyButton.DpadRight, JoyAxis.LeftX, 1);
        Direction("ui_up", RawGamepadInput.MoveForward, JoyButton.DpadUp, JoyAxis.LeftY, -1);
        Direction("ui_down", RawGamepadInput.MoveBack, JoyButton.DpadDown, JoyAxis.LeftY, 1);
        KeyBinding(Pause, Key.Escape); Button(Pause, JoyButton.Start);
        KeyBinding(QuickSelect, Key.Q); Button(QuickSelect, JoyButton.Y);
        KeyBinding(CameraCenter, Key.Home); Button(CameraCenter, JoyButton.LeftShoulder);
        Motion(CrouchAlternate, JoyAxis.TriggerRight, 1);
        InputMap.ActionSetDeadzone(CrouchAlternate, .5f);
        Button("obp_planet_map", JoyButton.RightStick);
    }

    private static void Direction(StringName ui, StringName move, JoyButton button, JoyAxis axis, float value)
    {
        Button(ui, button); Motion(ui, axis, value);
        InputMap.ActionSetDeadzone(ui, .5f);
        Button(move, button);
    }

    private static void Ensure(StringName action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }

    private static void Button(StringName action, JoyButton button)
    {
        Ensure(action);
        if (!InputMap.ActionGetEvents(action).OfType<InputEventJoypadButton>().Any(e => e.ButtonIndex == button))
            InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button, Device = -1 });
        if (action.ToString().StartsWith("ui_", StringComparison.Ordinal)) Button(PadAction(action), button);
    }

    private static void Motion(StringName action, JoyAxis axis, float value)
    {
        Ensure(action);
        if (!InputMap.ActionGetEvents(action).OfType<InputEventJoypadMotion>().Any(e => e.Axis == axis && e.AxisValue == value))
            InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = -1 });
        if (action.ToString().StartsWith("ui_", StringComparison.Ordinal)) Motion(PadAction(action), axis, value);
    }

    private static void KeyBinding(StringName action, Key key)
    {
        Ensure(action);
        if (!InputMap.ActionGetEvents(action).OfType<InputEventKey>().Any(e => e.PhysicalKeycode == key))
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}

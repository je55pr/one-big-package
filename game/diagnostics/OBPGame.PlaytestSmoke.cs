using Godot;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    private async Task RunPlaytestRecordingSmokeAsync()
    {
        try
        {
            for (int i = 0; i < 360 && _playtest?.WaitingForEntry == true; ++i)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_playtest is null || _player is null) throw new InvalidOperationException("Playtest recorder/player missing.");
            await PhysicsFramesAsync(30);
            // Ordinary input events, including UI edges and raw analogue axes.
            // No transforms, inventory grants, damage or progression injections.
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -.55f });
            await PhysicsFramesAsync(40);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
            await PhysicsFramesAsync(15);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
            await PhysicsFramesAsync(10);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
            await PhysicsFramesAsync(75);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = .65f });
            await PhysicsFramesAsync(35);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 0 });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = true });
            await PhysicsFramesAsync(3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = false });
            await PhysicsFramesAsync(25);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = true });
            await PhysicsFramesAsync(3);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = false });
            await PhysicsFramesAsync(150);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.X, Pressed = true });
            await PhysicsFramesAsync(3);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.X, Pressed = false });
            await PhysicsFramesAsync(75);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true });
            await PhysicsFramesAsync(20);
            if (!QuickSelectOpen) throw new InvalidOperationException("Recorded Triangle did not open Quick Select.");
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = false });
            await PhysicsFramesAsync(5);
            if (QuickSelectOpen) throw new InvalidOperationException("Recorded Triangle release did not close Quick Select.");
            GetWindow().Size = new(1000, 700);
            await PhysicsFramesAsync(10);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Start, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Start, Pressed = false });
            for (int frame = 0; frame < 15; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!PauseMenuOpen) throw new InvalidOperationException("Recorded Start did not pause.");
            // Leave focus on Controller Settings using real menu input.
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = true });
            for (int frame = 0; frame < 2; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = false });
            for (int frame = 0; frame < 5; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Both logical and physical-only function-key events must mark.
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F8, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F8, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.X, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.X, Pressed = false });
            if (_world?.LevelId == 0)
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F9, Pressed = true });
            else
                Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Back, Pressed = true });
        }
        catch (Exception error)
        {
            GD.PrintErr("[playtest-smoke] FAIL: " + error.Message);
            ApplicationLifecycle.RequestQuit(this, "playtest-smoke-failed", 2);
        }
    }
}

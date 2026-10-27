using Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Owns only synthetic in-application, device-zero events. It never injects desktop input.</summary>
internal sealed class GodotCaptureDiagnosticInputFactory : ICaptureDiagnosticInputFactory
{
    public ICaptureDiagnosticInput Acquire() => new Lease();
    private sealed class Lease : ICaptureDiagnosticInput
    {
        private readonly HashSet<(CaptureKey Key, CaptureKeyOrigin Origin, bool Shift)> _keys = [];
        private readonly HashSet<CaptureButton> _buttons = [];
        private readonly HashSet<CaptureAxis> _axes = [];
        private System.Numerics.Vector2? _mouse;
        private bool _disposed;
        private static global::Godot.Key Native(CaptureKey key) => key switch
        {
            CaptureKey.W => global::Godot.Key.W,
            CaptureKey.A => global::Godot.Key.A,
            CaptureKey.S => global::Godot.Key.S,
            CaptureKey.D => global::Godot.Key.D,
            CaptureKey.Q => global::Godot.Key.Q,
            CaptureKey.E => global::Godot.Key.E,
            CaptureKey.Space => global::Godot.Key.Space,
            CaptureKey.F7 => global::Godot.Key.F7,
            CaptureKey.F8 => global::Godot.Key.F8,
            CaptureKey.F9 => global::Godot.Key.F9,
            CaptureKey.F12 => global::Godot.Key.F12,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        public void Key(CaptureKey key, bool pressed, CaptureKeyOrigin origin = CaptureKeyOrigin.Both, bool shift = false, char? unicode = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var owned = (key, origin, shift); if (pressed) _keys.Add(owned); else _keys.Remove(owned);
            using var input = new InputEventKey
            {
                Keycode = origin == CaptureKeyOrigin.Physical ? global::Godot.Key.None : Native(key),
                PhysicalKeycode = origin == CaptureKeyOrigin.Logical ? global::Godot.Key.None : Native(key),
                Pressed = pressed,
                ShiftPressed = shift,
                Unicode = unicode ?? 0u,
            };
            Input.ParseInputEvent(input);
        }
        public void Pad(CaptureButton button, bool pressed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (pressed) _buttons.Add(button); else _buttons.Remove(button);
            JoyButton native = button switch
            {
                CaptureButton.A => JoyButton.A,
                CaptureButton.B => JoyButton.B,
                CaptureButton.X => JoyButton.X,
                CaptureButton.Y => JoyButton.Y,
                CaptureButton.Start => JoyButton.Start,
                CaptureButton.Back => JoyButton.Back,
                CaptureButton.Down => JoyButton.DpadDown,
                CaptureButton.Right => JoyButton.DpadRight,
                CaptureButton.Up => JoyButton.DpadUp,
                _ => throw new ArgumentOutOfRangeException(nameof(button)),
            };
            using var input = new InputEventJoypadButton { ButtonIndex = native, Pressed = pressed }; Input.ParseInputEvent(input);
        }
        public void Axis(CaptureAxis axis, float value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value != 0) _axes.Add(axis); else _axes.Remove(axis);
            JoyAxis native = axis switch { CaptureAxis.LeftY => JoyAxis.LeftY, CaptureAxis.RightX => JoyAxis.RightX, _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
            using var input = new InputEventJoypadMotion { Axis = native, AxisValue = value }; Input.ParseInputEvent(input);
        }
        public void Mouse(bool pressed, System.Numerics.Vector2 position)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); _mouse = pressed ? position : null;
            using var input = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = new(position.X, position.Y), Pressed = pressed }; Input.ParseInputEvent(input);
        }
        public void Dispose()
        {
            if (_disposed) return;
            List<Action> releases = [];
            foreach (var (key, origin, shift) in _keys.ToArray()) releases.Add(() => Key(key, false, origin, shift, null));
            foreach (var button in _buttons.ToArray()) releases.Add(() => Pad(button, false));
            foreach (var axis in _axes.ToArray()) releases.Add(() => Axis(axis, 0));
            if (_mouse is { } point) releases.Add(() => Mouse(false, point));
            try { DiagnosticCleanup.Run("Synthetic capture input cleanup failed.", [.. releases]); }
            finally { _disposed = true; }
        }
    }
}
internal sealed class GodotCaptureDiagnosticTime : ICaptureDiagnosticTime
{
    public ulong Milliseconds => Time.GetTicksMsec();
    public Task DelayAsync(int milliseconds, CancellationToken token) => Task.Delay(milliseconds, token);
}

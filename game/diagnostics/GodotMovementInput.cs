using Godot;
using OBP.Godot;

namespace OBP.Game.Diagnostics;

internal sealed class GodotMovementPhysicalInput : IDiagnosticPhysicalInput
{
    private static readonly DiagnosticPhysicalInput Shared = new((key, pressed) =>
    {
        Key native = key switch
        {
            DiagnosticPhysicalKey.W => Key.W,
            DiagnosticPhysicalKey.Space => Key.Space,
            DiagnosticPhysicalKey.F => Key.F,
            DiagnosticPhysicalKey.R => Key.R,
            DiagnosticPhysicalKey.E => Key.E,
            DiagnosticPhysicalKey.F11 => Key.F11,
            DiagnosticPhysicalKey.Key1 => Key.Key1,
            DiagnosticPhysicalKey.Key2 => Key.Key2,
            DiagnosticPhysicalKey.Q => Key.Q,
            DiagnosticPhysicalKey.X => Key.X,
            DiagnosticPhysicalKey.F8 => Key.F8,
            DiagnosticPhysicalKey.F9 => Key.F9,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        using var input = new InputEventKey { Keycode = native, PhysicalKeycode = native, Pressed = pressed, Echo = false };
        Input.ParseInputEvent(input);
    }, (button, pressed) =>
    {
        using var input = new InputEventJoypadButton
        {
            Device = 15,
            ButtonIndex = button == DiagnosticPhysicalButton.Wrench ? JoyButton.X : JoyButton.B,
            Pressed = pressed
        };
        Input.ParseInputEvent(input);
    }, (axis, value) =>
    {
        using var input = new InputEventJoypadMotion
        {
            Device = 0,
            Axis = axis == DiagnosticPhysicalAxis.LeftX ? JoyAxis.LeftX : JoyAxis.LeftY,
            AxisValue = value
        };
        Input.ParseInputEvent(input);
    });
    public IDiagnosticPhysicalInputLease Acquire() => Shared.Acquire();
}

/// <summary>Temporary invocation-only compatibility for suites migrating in #340-#345. No scenario assertions live here.</summary>
internal static class GodotMovementCompatibility
{
    public static async Task FramesAsync(Node owner, int count, CancellationToken token)
    {
        for (int i = 0; i < count; i++)
            await GodotAsyncWait.SignalAsync(owner.GetTree(), SceneTree.SignalName.PhysicsFrame, token.CanBeCanceled ? token : LegacyDiagnosticCancellation.Current);
    }
    public static async Task GroundedAsync(Node owner, PlayerHost player, int count, CancellationToken token)
    {
        int supported = 0;
        token = token.CanBeCanceled ? token : LegacyDiagnosticCancellation.Current;
        for (int i = 0; i < count; i++)
        {
            await FramesAsync(owner, 1, token);
            await GodotAsyncWait.SignalAsync(owner.GetTree(), SceneTree.SignalName.ProcessFrame, token);
            supported = player.IsOnFloor() && player.Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded ? supported + 1 : 0;
            if (supported >= 3) { return; }
        }
        throw new InvalidOperationException("player did not reach Godot floor contact within smoke timeout");
    }
    public static async Task AnimationAsync(Node owner, PlayerHost player, OBP.Runtime.Player.PlayerAnimationState state, int count, string label)
    {
        for (int i = 0; i < count; i++) { await FramesAsync(owner, 1, LegacyDiagnosticCancellation.Current); if (player.AnimationState == state) { return; } }
        throw new InvalidOperationException($"{label} did not reach animation state {state}; current={player.AnimationState}");
    }
    public static void Key(Key key, bool pressed)
    {
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = false };
        Input.ParseInputEvent(input);
    }
    public static async Task TapKeyAsync(Node owner, Key key)
    {
        Key(key, true); await FramesAsync(owner, 2, LegacyDiagnosticCancellation.Current);
        Key(key, false); await FramesAsync(owner, 2, LegacyDiagnosticCancellation.Current);
    }
    public static void Analogue(float right, float forward)
    {
        ReleaseDirections();
        if (right > 0) { Input.ActionPress(RawGamepadInput.MoveRight, right); } else if (right < 0) { Input.ActionPress(RawGamepadInput.MoveLeft, -right); }
        if (forward > 0) { Input.ActionPress(RawGamepadInput.MoveForward, forward); } else if (forward < 0) { Input.ActionPress(RawGamepadInput.MoveBack, -forward); }
    }
    public static void Conditioned(double right, double forward)
    {
        var raw = MovementDiagnosticDriver.RawConditionedAxes(right, forward); Analogue(raw.X, raw.Y);
    }
    public static void Heading(PlayerHost player, double nativeYaw, double magnitude)
    {
        double controlHeading = player.Rac1RuntimeCameraState?.ControlHeadingRadians ?? player.Rac1ControlYaw;
        double stickAngle = controlHeading - nativeYaw;
        Conditioned(Math.Sin(stickAngle) * magnitude, Math.Cos(stickAngle) * magnitude);
    }
    public static void ReleaseDirections()
    {
        Input.ActionRelease(RawGamepadInput.MoveLeft); Input.ActionRelease(RawGamepadInput.MoveRight);
        Input.ActionRelease(RawGamepadInput.MoveForward); Input.ActionRelease(RawGamepadInput.MoveBack);
    }
    public static void Clear()
    {
        ReleaseDirections(); Input.ActionRelease(RawGamepadInput.Jump); Input.ActionRelease(RawGamepadInput.Crouch);
        Input.ActionRelease(RawGamepadInput.Action); Input.ActionRelease(RawGamepadInput.Traversal);
    }
}

using Godot;
using OBP.Godot.Controls;

namespace OneBigPackage;

/// <summary>
/// Godot/SDL input boundary for ordinary player controls.
/// Stick actions are read with GetActionRawStrength so action dead zones are bypassed;
/// the resulting vectors are never normalized or curved here.
/// </summary>
internal sealed class RawGamepadInput
{
    public static readonly StringName MoveLeft = "obp_move_left";
    public static readonly StringName MoveRight = "obp_move_right";
    public static readonly StringName MoveForward = "obp_move_forward";
    public static readonly StringName MoveBack = "obp_move_back";
    public static readonly StringName CameraLeft = "obp_camera_left";
    public static readonly StringName CameraRight = "obp_camera_right";
    public static readonly StringName CameraUp = "obp_camera_up";
    public static readonly StringName CameraDown = "obp_camera_down";
    public static readonly StringName Jump = "obp_jump";
    public static readonly StringName Crouch = "obp_crouch";
    public static readonly StringName Action = "obp_action";

    private int? _activeDevice;
    private int? _announcedDevice;

    public void Observe(InputEvent @event)
    {
        if (@event is InputEventJoypadMotion or InputEventJoypadButton)
            _activeDevice = @event.Device;
    }

    public RawPlayerInputFrame Read()
    {
        var move = new Vector2(
            Axis(MoveLeft, MoveRight),
            Axis(MoveForward, MoveBack));
        var camera = new Vector2(
            Axis(CameraLeft, CameraRight),
            Axis(CameraUp, CameraDown));
        RawGamepadDiagnostic? diagnostic = ReadDiagnostic();
        if (diagnostic is { } current && _announcedDevice != current.DeviceId)
        {
            _announcedDevice = current.DeviceId;
            GD.Print($"[Input] {current.Format()}");
        }
        else if (diagnostic is null)
        {
            _announcedDevice = null;
        }

        return new RawPlayerInputFrame(
            move,
            camera,
            Input.IsActionPressed(Jump),
            Input.IsActionPressed(Crouch),
            Input.IsActionJustPressed(Action),
            diagnostic);
    }

    private static float Axis(StringName negative, StringName positive) =>
        RawGamepadInputMath.ComposeAxis(
            Input.GetActionRawStrength(negative, exactMatch: true),
            Input.GetActionRawStrength(positive, exactMatch: true));

    private RawGamepadDiagnostic? ReadDiagnostic()
    {
        int? device = ResolveActiveDevice();
        if (device is null)
            return null;

        int id = device.Value;
        return new RawGamepadDiagnostic(
            id,
            Input.GetJoyName(id),
            Input.GetJoyGuid(id),
            Input.IsJoyKnown(id),
            new RawGamepadAxes(
                Input.GetJoyAxis(id, JoyAxis.LeftX),
                Input.GetJoyAxis(id, JoyAxis.LeftY),
                Input.GetJoyAxis(id, JoyAxis.RightX),
                Input.GetJoyAxis(id, JoyAxis.RightY)));
    }

    private int? ResolveActiveDevice()
    {
        var connected = Input.GetConnectedJoypads();
        int? first = null;
        bool activeConnected = false;
        foreach (int device in connected)
        {
            if (first is null || device < first.Value)
                first = device;
            if (_activeDevice == device)
                activeConnected = true;
        }

        if (!activeConnected)
            _activeDevice = first;
        return _activeDevice;
    }
}

internal readonly record struct RawPlayerInputFrame(
    Vector2 Move,
    Vector2 CameraIntent,
    bool JumpHeld,
    bool CrouchHeld,
    bool ActionJustPressed,
    RawGamepadDiagnostic? Diagnostic);

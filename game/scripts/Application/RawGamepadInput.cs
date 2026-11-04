using Godot;
using OBP.Godot.Controls;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>
/// Godot/SDL input boundary for ordinary player controls.
/// Stick actions are read with GetActionRawStrength so action dead zones are bypassed;
/// the resulting vectors are never normalized or curved here. Buttons are consumed
/// only through named InputMap actions, never vendor-specific physical button ids.
/// </summary>
internal sealed class RawGamepadInput : IDisposable
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
    public static readonly StringName Traversal = "obp_traversal";

    private int? _activeDevice;
    private int? _announcedDevice;
    private int _suppressedButtons = 15;
    private bool _listening;

    public void Attach()
    {
        if (_listening) return;
        Input.JoyConnectionChanged += ConnectionChanged;
        _listening = true;
    }

    public void Dispose()
    {
        if (!_listening) return;
        Input.JoyConnectionChanged -= ConnectionChanged;
        _listening = false;
    }

    internal void ConnectionChanged(long device, bool connected)
    {
        if (_activeDevice != device && _activeDevice.HasValue) return;
        _activeDevice = null;
        _announcedDevice = null;
        // Rebind at the next read. Held buttons must pass through release;
        // connection events cannot synthesize a new attack/jump edge.
        _suppressedButtons = 15;
    }

    public int HeldButtons => (Input.IsActionPressed(Jump) ? 1 : 0) |
        (Input.IsActionPressed(Crouch) || Input.IsActionPressed(ControllerBindings.CrouchAlternate) ? 2 : 0) |
        (Input.IsActionPressed(Action) ? 4 : 0) | (Input.IsActionPressed(Traversal) ? 8 : 0);
    public void SuppressButtonsUntilRelease(int preserveButtons = 0) => _suppressedButtons = HeldButtons & ~preserveButtons;

    public void Observe(InputEvent @event)
    {
        if (@event is InputEventJoypadMotion or InputEventJoypadButton)
            _activeDevice = @event.Device;
    }

    public RawPlayerInputFrame Read(PlayerInputPolicy? policy = null)
    {
        int? device = ResolveActiveDevice();
        var rawMove = new Vector2(
            Axis(MoveLeft, MoveRight),
            Axis(MoveForward, MoveBack));
        var rawCamera = new Vector2(
            Axis(CameraLeft, CameraRight),
            Axis(CameraUp, CameraDown));
        var move = ControllerCalibrationStore.ApplyLeft(device, rawMove);
        var camera = ControllerCalibrationStore.ApplyRight(device, rawCamera);
        RawGamepadDiagnostic? diagnostic = ReadDiagnostic(device);
        if (diagnostic is { } current && _announcedDevice != current.DeviceId)
        {
            _announcedDevice = current.DeviceId;
            GD.Print($"[Input] {current.Format()}");
        }
        else if (diagnostic is null)
        {
            _announcedDevice = null;
        }

        bool jump = Input.IsActionPressed(Jump);
        bool crouch = Input.IsActionPressed(Crouch) || policy?.AlternateCrouch == true && Input.IsActionPressed(ControllerBindings.CrouchAlternate);
        bool action = Input.IsActionPressed(Action);
        bool use = Input.IsActionPressed(Traversal);
        int held = (jump ? 1 : 0) | (crouch ? 2 : 0) | (action ? 4 : 0) | (use ? 8 : 0);
        _suppressedButtons &= held;
        jump &= (_suppressedButtons & 1) == 0;
        crouch &= (_suppressedButtons & 2) == 0;
        action &= (_suppressedButtons & 4) == 0;
        use &= (_suppressedButtons & 8) == 0;
        return new RawPlayerInputFrame(
            move,
            camera,
            jump,
            crouch,
            action && Input.IsActionJustPressed(Action),
            diagnostic,
            use,
            use && Input.IsActionJustPressed(Traversal),
            ActionHeld: action);
    }

    private static float Axis(StringName negative, StringName positive) =>
        RawGamepadInputMath.ComposeAxis(
            Input.GetActionRawStrength(negative, exactMatch: true),
            Input.GetActionRawStrength(positive, exactMatch: true));

    private static RawGamepadDiagnostic? ReadDiagnostic(int? device)
    {
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
    RawGamepadDiagnostic? Diagnostic,
    bool TraversalHeld = false,
    bool EquippedUseJustPressed = false,
    bool ActionHeld = false);

using Godot;

namespace OneBigPackage;

/// <summary>
/// Explicit developer-only controls attached beside <see cref="PlayerHost"/>.
/// Production movement/input code exposes narrow seams; this node owns the
/// keyboard bindings for fly/noclip, diagnostics, camera fallback and spawn reset.
/// </summary>
public partial class PlayerDevelopmentControls : Node
{
    private PlayerHost? _host;

    public float FlySpeed { get; set; } = 45f;
    public bool FlyEnabled { get; private set; }
    public bool DiagnosticsVisible { get; private set; }
    public bool ForceHostCamera { get; private set; }
    public Vector3 SpawnPosition { get; private set; }

    public void Attach(PlayerHost host, Vector3 spawnPosition)
    {
        _host = host;
        SpawnPosition = spawnPosition;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_host is null || _host.Scripted ||
            @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.Tab)
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
        else if (key.Keycode == Key.F8)
        {
            DiagnosticsVisible = !DiagnosticsVisible;
        }
        else if (key.Keycode == Key.F9)
        {
            ForceHostCamera = !ForceHostCamera;
            _host.RefreshDevelopmentCameraPresentation();
            GD.Print($"[player-dev] camera {_host.CameraControllerLabel}");
        }
        else if (key.Keycode == Key.F)
        {
            FlyEnabled = !FlyEnabled;
            _host.ApplyDevelopmentFlyMode(FlyEnabled);
            GD.Print($"[player-dev] fly mode {(FlyEnabled ? "on" : "off")}");
        }
        else if (key.Keycode == Key.R && !_host.UseRac1Gameplay)
        {
            ResetToSpawn();
        }
    }

    public void ResetToSpawn()
    {
        if (_host is not null)
            _host.ApplyDevelopmentReset(SpawnPosition);
    }
    public void StepFly(float delta, Vector2 move)
    {
        if (_host is null || !FlyEnabled)
            return;

        float lift =
            (Input.IsPhysicalKeyPressed(Key.Space) || Input.IsPhysicalKeyPressed(Key.E) ? 1f : 0f) -
            (Input.IsPhysicalKeyPressed(Key.Ctrl) || Input.IsPhysicalKeyPressed(Key.Q) ? 1f : 0f);
        bool boost = Input.IsPhysicalKeyPressed(Key.Shift);

        Vector3 dir = _host.DevelopmentCameraBasis * new Vector3(move.X, 0f, move.Y);
        dir += Vector3.Up * lift;
        if (dir.LengthSquared() > 1e-4f)
        {
            _host.GlobalPosition += dir.Normalized() * (FlySpeed * (boost ? 3f : 1f) * delta);
        }

        _host.Velocity = Vector3.Zero;
    }

    public string ControlSummary(bool useRac1Gameplay) =>
        useRac1Gameplay
            ? "mouse / right stick debug camera / F fly / F8 diagnostics / F9 camera fallback / Tab cursor / Esc"
            : "mouse / right stick debug camera / F fly / R respawn (development) / F8 diagnostics / F9 camera fallback / Tab cursor / Esc";
}

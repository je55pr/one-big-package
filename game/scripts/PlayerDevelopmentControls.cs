using Godot;

namespace OBP.Game;

/// <summary>
/// Explicit developer-only controls attached beside <see cref="PlayerHost"/>.
/// Production movement/input code exposes narrow seams; this node owns the
/// keyboard bindings for fly/noclip, diagnostics, camera fallback and spawn reset.
/// </summary>
public partial class PlayerDevelopmentControls : Node
{
    private PlayerHost? _host;
    public event Action? SaveMovementStateRequested;
    public event Action? LoadMovementStateRequested;

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
        if (key.Keycode == Key.F12)
        {
            if (key.CtrlPressed)
                CallDeferred(nameof(SaveViewBookmark));
            else if (key.ShiftPressed)
                LoadMovementStateRequested?.Invoke();
            else
                SaveMovementStateRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Tab && !GetWindow().Unfocusable)
        {
            ApplicationPointer.MouseMode = ApplicationPointer.MouseMode == Input.MouseModeEnum.Captured
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
        else if (key.Keycode == Key.F10)
        {
            _host.CycleRac1DevelopmentPack();
        }
        else if (key.Keycode == Key.F11)
        {
            _host.ToggleRac1DevelopmentSwingshot();
        }
        else if (key.Keycode == Key.F)
        {
            FlyEnabled = !FlyEnabled;
            _host.ApplyDevelopmentFlyMode(FlyEnabled);
            GD.Print($"[player-dev] fly mode {(FlyEnabled ? "on" : "off")}");
        }
        else if (key.Keycode == Key.R && !_host.CanSaveMovementState)
        {
            ResetToSpawn();
        }
    }

    /// <summary>
    /// Save a visual-only diagnostic without claiming a deterministic gameplay
    /// replay. This remains usable when source actors have no replay fixture.
    /// </summary>
    private void SaveViewBookmark()
    {
        if (_host is null || !IsInstanceValid(_host)) return;
        try
        {
            var camera = GetViewport().GetCamera3D()
                ?? throw new InvalidOperationException("No active camera is available.");
            var directory = Path.GetFullPath(Path.Combine(
                ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros"));
            Directory.CreateDirectory(directory);
            var name = "view-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var imagePath = Path.Combine(directory, name + ".png");
            var jsonPath = Path.Combine(directory, name + ".json");

            var image = GetViewport().GetTexture().GetImage();
            var imageError = image.SavePng(imagePath);
            if (imageError != Error.Ok)
                throw new IOException($"Could not save view screenshot: {imageError}");

            static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];
            var pose = camera.GlobalTransform;
            var record = new
            {
                Version = 1,
                Kind = "visual-only; not a replay or game save",
                CapturedUtc = DateTimeOffset.UtcNow,
                PlayerPosition = Vec(_host.GlobalPosition),
                CameraPosition = Vec(pose.Origin),
                CameraRight = Vec(pose.Basis.X),
                CameraUp = Vec(pose.Basis.Y),
                CameraForward = Vec(-pose.Basis.Z),
                CameraFovDegrees = camera.Fov,
                Screenshot = Path.GetFileName(imagePath),
            };
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(
                record, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[view-bookmark] SAVED {jsonPath}");
            GetWindow().Title = $"One Big Package (DEBUG) - view saved {name}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            GD.PushError("[view-bookmark] " + error.Message);
            GetWindow().Title = "One Big Package (DEBUG) - view save failed";
        }
    }

    public void ResetToSpawn()
    {
        if (_host is not null)
            _host.ApplyDevelopmentReset(SpawnPosition);
    }

    internal void SetFlyEnabled(bool enabled)
    {
        FlyEnabled = enabled;
        _host?.ApplyDevelopmentFlyMode(enabled);
    }

    internal void SetForceHostCamera(bool enabled) => ForceHostCamera = enabled;
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
            ? "F12 save movement / Shift+F12 load\nmouse / right stick debug camera / F fly / F8 diagnostics / F9 camera fallback / Tab cursor / Esc"
            : "mouse / right stick debug camera / F fly / R respawn (development) / F8 diagnostics / F9 camera fallback / Tab cursor / Esc";
}

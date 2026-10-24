using System.Text.Json;
using Godot;

namespace OneBigPackage;

/// <summary>Application window preferences, shared by interactive and smoke launches.</summary>
public partial class WindowPlacement : Node
{
    private const string SettingsPath = "user://window-placement.json";
    private sealed record Placement(int X, int Y, int Width, int Height, bool Maximized, int Screen = 0);
    private Window? _window;
    private Placement? _normal, _saved;
    private double _pollDelay = .5;
    private bool _warned;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (Engine.IsEditorHint() || DisplayServer.GetName() == "headless")
        {
            SetProcess(false);
            return;
        }

        _window = GetTree().Root;
        var args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
        // The project starts unfocusable so a test cannot steal focus before
        // autoloads run. Ordinary interactive launches regain normal focus.
        _window.Unfocusable = WorldSmokeHarness.IsRequested(args) ||
            args.Rac1StartupVisibilitySmoke || args.FusionSmoke || args.StressSwitch is not null || args.ComposeReload is not null ||
            args.CaptureFrame is not null || args.ShotsPath is not null || args.ReplayPlaytest is not null || args.PlaytestSmoke || args.BugReportSmoke || args.ReplayBugReport is not null && !args.PlaytestInspectMarker;
        if (args.ControllerSmoke || args.FusionSmoke || args.Rac1PlayerAudioSmoke || args.CaptureFrame is not null || args.ShotsPath is not null || args.ReplayForwardFrames.HasValue || args.Rac1BombGloveSmoke || args.Rac1WeaponsSmoke || args.ReplayPlaytest is not null || args.PlaytestSmoke || args.BugReportSmoke || args.ReplayBugReport is not null)
        {
            _window = null; // Automated capture/replay placement never replaces personal preferences.
            SetProcess(false);
            return;
        }

        _normal = new(_window.Position.X, _window.Position.Y, _window.Size.X, _window.Size.Y, false, _window.CurrentScreen);
        try
        {
            var path = ProjectSettings.GlobalizePath(SettingsPath);
            if (System.IO.File.Exists(path))
            {
                var placement = JsonSerializer.Deserialize<Placement>(System.IO.File.ReadAllText(path));
                if (placement is { Width: > 0 and <= 32768, Height: > 0 and <= 32768 })
                    Restore(placement);
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or JsonException)
        {
            GD.PushWarning($"Could not load window placement: {exception.Message}");
        }
    }

    private void Restore(Placement placement)
    {
        var position = new Vector2I(placement.X, placement.Y);
        var size = new Vector2I(placement.Width, placement.Height);
        var wanted = new Rect2I(position, size);
        bool maximized = placement.Maximized && !OS.GetCmdlineArgs().Contains("--windowed");
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
        long bestArea = 0;
        for (int screen = 0; screen < DisplayServer.GetScreenCount(); screen++)
        {
            var candidate = DisplayServer.ScreenGetUsableRect(screen);
            var intersection = candidate.Intersection(wanted);
            long area = (long)Math.Max(0, intersection.Size.X) * Math.Max(0, intersection.Size.Y);
            if (area > bestArea) { usable = candidate; bestArea = area; }
        }
        if (maximized && placement.Screen >= 0 && placement.Screen < DisplayServer.GetScreenCount())
            usable = DisplayServer.ScreenGetUsableRect(placement.Screen);
        // Recover safely after a monitor is disconnected, preserving negative
        // desktop coordinates on monitors to the left or above the primary one.
        size = new(Math.Clamp(size.X, 1, Math.Max(1, usable.Size.X)), Math.Clamp(size.Y, 1, Math.Max(1, usable.Size.Y)));
        position = new(Math.Clamp(position.X, usable.Position.X, usable.End.X - size.X),
            Math.Clamp(position.Y, usable.Position.Y, usable.End.Y - size.Y));
        _window!.InitialPosition = Window.WindowInitialPosition.Absolute;
        _window.Mode = Window.ModeEnum.Windowed;
        _window.Size = size;
        _window.Position = position;
        _normal = new(position.X, position.Y, size.X, size.Y, false, _window.CurrentScreen);
        _saved = placement;
        if (maximized) _window.Mode = Window.ModeEnum.Maximized;
    }

    public override void _Process(double delta)
    {
        _pollDelay -= delta;
        if (_pollDelay > 0) return;
        _pollDelay = .25;
        Save();
    }

    public override void _ExitTree() => Save();

    private void Save()
    {
        if (_window is null || !IsInstanceValid(_window) || _normal is null) return;
        if (_window.Mode == Window.ModeEnum.Windowed)
            _normal = new(_window.Position.X, _window.Position.Y, _window.Size.X, _window.Size.Y, false, _window.CurrentScreen);
        if (_window.Mode is not (Window.ModeEnum.Windowed or Window.ModeEnum.Maximized)) return;
        var current = _normal with { Maximized = _window.Mode == Window.ModeEnum.Maximized, Screen = _window.CurrentScreen };
        if (current == _saved || current.Width <= 0 || current.Height <= 0) return;
        try
        {
            string path = ProjectSettings.GlobalizePath(SettingsPath);
            System.IO.File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(current));
            System.IO.File.Move(path + ".tmp", path, overwrite: true);
            _saved = current;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            if (!_warned) GD.PushWarning($"Could not save window placement: {exception.Message}");
            _warned = true;
        }
    }
}

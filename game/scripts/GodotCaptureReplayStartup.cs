using Godot;
using OBP.Godot;

namespace OBP.Game;

internal static class GodotCaptureReplayStartup
{
    internal static string PhysicsEngine() => ProjectSettings.GetSetting("physics/3d/physics_engine", "DEFAULT").AsString();
    internal static CaptureEnvironment Environment() => new(Engine.GetVersionInfo()["string"].AsString(), OS.GetName(), PhysicsEngine(), Engine.PhysicsTicksPerSecond);

    internal static (bool Ready, CommandLineArgs Args) Initialize(Node host, CaptureReplayCoordinator coordinator, CommandLineArgs args, PlaytestDriverPorts ports)
    {
        PlaytestDriver? driver = null;
        try
        {
            args = coordinator.Initialize(args, () => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),
                "../captures/repros/replay-" + Guid.NewGuid().ToString("N"), "campaign.json")), (session, reader, camera) =>
            {
                driver = new() { Session = session, Ports = ports, Reader = reader, OriginalCameraOptions = CameraSettingsStore.Current };
                if (camera is not null) CameraSettingsStore.Set(camera, persist: false);
                // Last root sibling receives window input before descendant UI; callback priorities retain their original phases.
                var installed = driver;
                Callable.From(() => host.GetTree().Root.AddChild(installed)).CallDeferred();
                host.TreeExiting += installed.FinishOnExit;
                return installed;
            });
            return (true, args);
        }
        catch (Exception error)
        {
            GD.PrintErr("[playtest] startup failed: " + error.Message);
            if (args.BugCapture && !args.BugReportSmoke && DisplayServer.GetName() != "headless")
                OS.Alert(error.Message, "OBP Bug Capture could not start");
            try { coordinator.WriteStartupFailure(error); }
            catch (Exception reportError) { GD.PrintErr("[playtest] report failed: " + reportError.Message); }
            if (driver is not null)
            {
                CameraSettingsStore.Set(driver.OriginalCameraOptions, persist: false);
                driver.Free();
            }
            host.GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(host, "playtest-invalid", 2);
            return (false, args);
        }
    }
}

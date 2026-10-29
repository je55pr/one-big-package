using Godot;
using OBP.Godot;
using OBP.Fusion;

namespace OBP.Game;

internal sealed class GodotReplayBookmarkNavigation(Action freeBootstrap, Action<string, int> quit, Action<string> report)
{
    public int? Step { get; private set; }

    public bool Initialize(ref CommandLineArgs args)
    {
        if (args.LoadMovementState is not { } path) return true;
        try
        {
            var state = MovementReproStateFile.Load(path);
            if (state.Version != MovementReproState.CurrentVersion)
            {
                // The old bookmark schema owns a native R&C1 inventory/world,
                // so decode it in its original explicit parity session.
                args = args with { RetailParity = true, NoProfile = true };
                return true;
            }
            if (args.PlaytestRequested || args.ReplayForwardFrames.HasValue || args.BugCapture || args.ReplayBugReport is not null)
                throw new InvalidDataException("Choose either a universal bookmark or another capture/replay operation.");
            string recording = ReplayBookmarkResolver.Resolve(path, state);
            Step = state.Replay!.Step;
            args = args with { LoadMovementState = null, ReplayPlaytest = recording };
            return true;
        }
        catch (Exception error)
        {
            GD.PrintErr("[movement-state] bookmark startup failed: " + error.Message);
            freeBootstrap();
            quit("bookmark-invalid", 2);
            return false;
        }
    }

    public void Open(string path, MovementReproState state, CommandLineArgs args)
    {
        _ = ReplayBookmarkResolver.Resolve(path, state);
        var arguments = new List<string> { "--path", ProjectSettings.GlobalizePath("res://"), "--fixed-fps", "60" };
        if (DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs())) arguments.Add("--headless");
        arguments.AddRange(["--", "--load-movement-state", Path.GetFullPath(path)]);
        if (args.Rac1Iso is { } rac1) arguments.AddRange(["--rac1-iso", rac1]);
        if (args.Rac2Iso is { } rac2) arguments.AddRange(["--rac2-iso", rac2]);
        if (args.Rac3Iso is { } rac3) arguments.AddRange(["--rac3-iso", rac3]);
        if (OS.CreateProcess(OS.GetExecutablePath(), arguments.ToArray()) < 0) throw new IOException("Could not open the bookmark replay process.");
        report("Opened the universal bookmark in a separate reproduction process; ordinary profile writes remain isolated.");
    }
}

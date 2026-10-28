using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OBP.Godot;
using OBP.Fusion;

namespace OBP.Game;

public partial class OBPGame
{
    private int? _bookmarkReplayStep;

    private static string ResolveReplayBookmark(string path, MovementReproState state)
    {
        state.Validate();
        var replay = state.Replay!;
        string recording = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, replay.FileName);
        using var file = File.OpenRead(recording);
        if (file.Length > OBP.Runtime.Diagnostics.PlaytestRunReader.MaximumCompressedBytes ||
            !Convert.ToHexString(SHA256.HashData(file)).Equals(replay.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bookmark recording is missing, too large or has changed.");
        return recording;
    }

    private bool InitializeReplayBookmark()
    {
        if (_args.LoadMovementState is not { } path) return true;
        try
        {
            var state = MovementReproStateFile.Load(path);
            if (state.Version != MovementReproState.CurrentVersion)
            {
                // The old bookmark schema owns a native R&C1 inventory/world,
                // so decode it in its original explicit parity session.
                _args = _args with { RetailParity = true, NoProfile = true };
                return true;
            }
            if (_args.PlaytestRequested || _args.ReplayForwardFrames.HasValue || _args.BugCapture || _args.ReplayBugReport is not null)
                throw new InvalidDataException("Choose either a universal bookmark or another capture/replay operation.");
            string recording = ResolveReplayBookmark(path, state);
            _bookmarkReplayStep = state.Replay!.Step;
            _args = _args with { LoadMovementState = null, ReplayPlaytest = recording };
            return true;
        }
        catch (Exception error)
        {
            GD.PrintErr("[movement-state] bookmark startup failed: " + error.Message);
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(this, "bookmark-invalid", 2);
            return false;
        }
    }

    private void OpenReplayBookmark(string path, MovementReproState state)
    {
        _ = ResolveReplayBookmark(path, state);
        var arguments = new List<string> { "--path", ProjectSettings.GlobalizePath("res://"), "--fixed-fps", "60" };
        if (DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs())) arguments.Add("--headless");
        arguments.AddRange(["--", "--load-movement-state", Path.GetFullPath(path)]);
        if (_args.Rac1Iso is { } rac1) arguments.AddRange(["--rac1-iso", rac1]);
        if (_args.Rac2Iso is { } rac2) arguments.AddRange(["--rac2-iso", rac2]);
        if (_args.Rac3Iso is { } rac3) arguments.AddRange(["--rac3-iso", rac3]);
        if (OS.CreateProcess(OS.GetExecutablePath(), arguments.ToArray()) < 0) throw new IOException("Could not open the bookmark replay process.");
        ReportMovementState("Opened the universal bookmark in a separate reproduction process; ordinary profile writes remain isolated.");
    }
}

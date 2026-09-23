using Godot;

namespace OBP.Godot;

/// <summary>
/// Central lifecycle telemetry for deliberate process exits. Ordinary runtime code
/// should go through this helper instead of calling SceneTree.Quit directly so a
/// clean exit is distinguishable from a desktop close or an external teardown.
/// </summary>
public static class ApplicationLifecycle
{
    private static string? _lastQuitReason;
    private static int _lastExitCode;

    public enum EscapeFallback
    {
        StayAlive,
        Quit,
    }

    /// <summary>
    /// A gameplay/world browser Escape that reaches the fallback path is never a
    /// process-exit request. Non-interactive surfaces may still use Escape to quit.
    /// </summary>
    public static EscapeFallback ResolveEscapeFallback(bool interactiveSurface) =>
        interactiveSurface ? EscapeFallback.StayAlive : EscapeFallback.Quit;
    public static void InstallWindowCloseInterception(SceneTree tree)
    {
        tree.AutoAcceptQuit = false;
        GD.Print("[lifecycle] window-close interception active");
    }

    public static void RequestQuit(Node owner, string reason, int exitCode = 0) =>
        RequestQuit(owner.GetTree(), reason, exitCode);

    public static void RequestQuit(SceneTree tree, string reason, int exitCode = 0)
    {
        _lastQuitReason = reason;
        _lastExitCode = exitCode;
        GD.Print($"[lifecycle] quit-request reason={reason} exitCode={exitCode}");
        tree.Quit(exitCode);
    }

    public static void ReportNavigation(string reason) =>
        GD.Print($"[lifecycle] navigation reason={reason}");

    public static void ReportStayAlive(string reason) =>
        GD.Print($"[lifecycle] stay-alive reason={reason}");

    public static void ReportRootExit(string owner)
    {
        string reason = _lastQuitReason ?? "none";
        GD.Print(
            $"[lifecycle] root-exit owner={owner} requestedQuit={reason} exitCode={_lastExitCode}");
    }
}

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
    private static Func<Task>? _prepareQuit;
    private static bool _quitPending;

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

    /// <summary>Install the application root's asynchronous resource teardown.</summary>
    public static void SetQuitPreparation(Func<Task>? prepareQuit) => _prepareQuit = prepareQuit;

    public static void RequestQuit(SceneTree tree, string reason, int exitCode = 0)
    {
        // A failure during shutdown must still turn an earlier pass into failure.
        if (_quitPending && exitCode == 0) return;
        _lastQuitReason = reason;
        _lastExitCode = exitCode;
        GD.Print($"[lifecycle] quit-request reason={reason} exitCode={exitCode}");
        if (_quitPending) return;
        _quitPending = true;
        _ = CompleteQuitAsync(tree);
    }

    private static async Task CompleteQuitAsync(SceneTree tree)
    {
        try
        {
            if (_prepareQuit is { } prepareQuit) await prepareQuit();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[lifecycle] quit preparation failed: {ex}");
            if (_lastExitCode == 0) _lastExitCode = 1;
        }
        finally
        {
            tree.Quit(_lastExitCode);
        }
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

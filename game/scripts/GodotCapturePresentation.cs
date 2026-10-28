using Godot;
using OBP.Godot;

namespace OBP.Game;

/// <summary>Engine frame/post-draw capture boundary, shared by single captures and the shot-list runner.</summary>
internal sealed class GodotCapturePresentation(Node host, Func<CaptureFrameSnapshot> snapshot, Func<string> scene,
    Func<string?> output)
{
    internal Dictionary<string, object?> Metadata() => CaptureFrameMetadata.Build(snapshot());
    internal static void ReportHostOperationFailures(Node host, HostOperationScope operations)
    {
        foreach (var failure in operations.TakeFailures())
        {
            GD.PrintErr($"[lifecycle] {failure.Name} failed (superseded={failure.Superseded}): {failure.Error}");
            if (failure.RequiresFailureExit(operations.Stopping) && host.IsInsideTree())
                ApplicationLifecycle.RequestQuit(host, failure.Name + "-failed", 2);
        }
    }
    internal async Task RunCaptureAsync(int frameArg, CancellationToken cancellationToken)
    {
        var result = await CaptureHarness.CaptureAsync(host, output() ?? $"captures/{scene()}.png", frameArg, () =>
        {
            var metadata = Metadata(); metadata["capture"] = scene(); metadata["captureFrameArg"] = frameArg; return metadata;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationLifecycle.RequestQuit(host, result.Ok ? "capture-complete" : "capture-save-failed", result.Ok ? 0 : 1);
    }
}

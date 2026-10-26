using Godot;
using OBP.Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Existing real-engine signal/capture ownership proof, independent of the production root's private state.</summary>
internal sealed class GodotSessionOperationChecks(Node owner, Func<CancellationToken, Task> movementStateCheck,
    Func<bool> hostStopping, Action<string> log) : IDiagnosticSessionEngineChecks
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pulse = new Node();
        try
        {
            bool emitting = false, resumedInsideSignal = false;
            async Task Probe()
            {
                await GodotAsyncWait.SignalAsync(pulse, Node.SignalName.TreeEntered, cancellationToken);
                resumedInsideSignal = emitting;
            }
            var probe = Probe(); emitting = true; pulse.EmitSignal(Node.SignalName.TreeEntered); emitting = false;
            await probe;
            Require(resumedInsideSignal, "signal wait moved continuation into a different callback phase");
        }
        finally { pulse.Free(); }
        using var operations = new HostOperationScope();
        int subscriptions = RenderingServer.Singleton.GetSignalConnectionList(RenderingServer.SignalName.FramePostDraw).Count;
        operations.Start("waiting-for-render", token => GodotAsyncWait.SignalAsync(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw, token), lifetime: CancellationToken.None);
        Require(RenderingServer.Singleton.GetSignalConnectionList(RenderingServer.SignalName.FramePostDraw).Count == subscriptions + 1, "owned signal wait was not connected");
        await operations.StopAsync();
        Require(operations.PendingCount == 0 && operations.TakeFailures().Count == 0, "canceled host work failed or did not drain");
        Require(RenderingServer.Singleton.GetSignalConnectionList(RenderingServer.SignalName.FramePostDraw).Count == subscriptions, "canceled render signal left a subscription");
        using var canceled = new CancellationTokenSource();
        bool metadataRead = false;
        string output = Path.Combine(ProjectSettings.GlobalizePath("res://../captures"), "canceled-" + Guid.NewGuid().ToString("N") + ".png");
        var capture = CaptureHarness.CaptureAsync(owner, output, 60, () => { metadataRead = true; return new Dictionary<string, object?>(); }, canceled.Token);
        canceled.Cancel();
        try { await capture; throw new InvalidOperationException("Canceled capture completed successfully."); }
        catch (OperationCanceledException error) when (error.CancellationToken == canceled.Token) { }
        Require(!metadataRead && !File.Exists(output), "canceled capture accessed a retired viewport or wrote an output");
        using var retiredDiagnostic = new CancellationTokenSource(); retiredDiagnostic.Cancel();
        await movementStateCheck(retiredDiagnostic.Token);
        Require(!hostStopping(), "expected diagnostic cancellation requested a failing quit");
        log("[player-session-smoke] PASS: canceled host/render waits drain, disconnect and publish no capture");
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}

using System.Collections.Immutable;
using Godot;
using OBP.Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Explicit CI driver for the existing asset-free startup scenario; no production root access.</summary>
public partial class SourceFreeStartupProbe : Node
{
    [Signal] public delegate void CompletedEventHandler(bool passed, string detail);
    private readonly HostOperationScope _operations = new();
    private bool _started;
    private bool _retired;

    public void Start(Node startupScene)
    {
        if (_started) throw new InvalidOperationException("The startup probe has already run.");
        _started = true;
        _operations.Start("source-free-startup-scenario", async cancellation =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            void RetireScene() => lifetime.Cancel();
            startupScene.TreeExiting += RetireScene;
            try
            {
                var args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
                var scenario = new SourceFreeStartupScenario(new GodotDiagnosticSceneObservations(startupScene));
                var runner = new DiagnosticScenarioRunner(new NoSources(), new GodotDiagnosticFrameClock(this));
                var result = await runner.RunAsync(scenario, args.TestSceneExplicit && args.TestScene == "picker", lifetime.Token);
                if (result.Status == DiagnosticRunStatus.Passed) VerifyInputOwnership();
                if (result.Status == DiagnosticRunStatus.Passed) await VerifyInteractiveLifetimeAsync();
                if (!_retired) EmitSignal(SignalName.Completed, result.Status == DiagnosticRunStatus.Passed, result.Detail);
            }
            catch (Exception error)
            { if (!_retired) EmitSignal(SignalName.Completed, false, error.Message); }
            finally
            {
                if (GodotObject.IsInstanceValid(startupScene)) startupScene.TreeExiting -= RetireScene;
            }
        });
    }

    public override void _ExitTree() { _retired = true; _operations.Dispose(); }

    private async Task VerifyInteractiveLifetimeAsync()
    {
        var tree = GetTree();
        int before = tree.GetSignalConnectionList(SceneTree.SignalName.ProcessFrame).Count;
        var definition = new DiagnosticScenarioDefinition("ci-interactive-loop/v1", DiagnosticSourceAccess.AssetFree,
            DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 120, FailureExitCode: 3);
        var runner = new DiagnosticScenarioRunner(new NoSources(), new GodotDiagnosticFrameClock(this));
        bool released = false, quit = false;
        var entered = new TaskCompletionSource();
        LegacyDiagnosticScenario Loop() => new(definition, async _ =>
        {
            entered.TrySetResult();
            try
            {
                while (IsInsideTree()) await LegacyDiagnosticWait.SignalAsync(tree, SceneTree.SignalName.ProcessFrame);
            }
            finally { released = true; }
        });
        using (var owner = new HostOperationScope())
        {
            DiagnosticScenarioDispatch.Start([Loop()], owner, runner, (_, _) => quit = true, _ => { });
            await entered.Task;
            await GodotAsyncWait.SignalAsync(tree, SceneTree.SignalName.ProcessFrame, CancellationToken.None);
            await owner.StopAsync();
            if (!released || quit || owner.PendingCount != 0 || !IsInsideTree())
                throw new InvalidOperationException("An interactive diagnostic did not cancel/drain before scene retirement.");
        }
        released = false;
        var completed = new TaskCompletionSource<int>();
        using (var owner = new HostOperationScope())
        {
            var failed = new LegacyDiagnosticScenario(definition with { Id = "ci-throw/v1" }, _ => throw new IOException("Expected CI sibling failure"));
            DiagnosticScenarioDispatch.Start([Loop(), failed], owner, runner, (_, code) => completed.TrySetResult(code), _ => { });
            if (await completed.Task != 3 || !released) throw new InvalidOperationException("A failed diagnostic did not cancel/drain its interactive sibling.");
            await owner.StopAsync();
        }
        if (tree.GetSignalConnectionList(SceneTree.SignalName.ProcessFrame).Count != before)
            throw new InvalidOperationException("Interactive diagnostic retirement leaked a frame subscription.");
        GD.Print("[diagnostic-bindings-smoke] PASS: interactive host-stop and fail-fast sibling cleanup while the scene remains alive");
    }

    private static void VerifyInputOwnership()
    {
        const string action = "obp_ci_diagnostic_input_ownership";
        if (InputMap.HasAction(action)) throw new InvalidOperationException("The diagnostic input fixture action already exists.");
        using var down = new InputEventKey { Keycode = Key.F24, PhysicalKeycode = Key.F24, Pressed = true };
        using var up = new InputEventKey { Keycode = Key.F24, PhysicalKeycode = Key.F24, Pressed = false };
        InputMap.AddAction(action);
        try
        {
            InputMap.ActionAddEvent(action, down);
            Input.ParseInputEvent(down); // Engine input only; never an OS keystroke.
            Input.FlushBufferedEvents();
            if (!Input.IsActionPressed(action)) throw new InvalidOperationException("The physical-input fixture did not press its action.");
            var input = new GodotDiagnosticInputActions();
            using (var lease = input.Acquire(CancellationToken.None)) lease.SetAction(action, 1);
            Input.ParseInputEvent(up);
            Input.FlushBufferedEvents();
            if (Input.IsActionPressed(action)) throw new InvalidOperationException("Diagnostic input remained simulated after physical release.");
            using var first = input.Acquire(CancellationToken.None);
            using var second = new GodotDiagnosticInputActions().Acquire(CancellationToken.None);
            first.SetAction(action, .4f); second.SetAction(action, .8f);
            second.Dispose();
            if (Math.Abs(Input.GetActionStrength(action) - .4f) > .001f) throw new InvalidOperationException("A separate simulated owner was cleared.");
            first.Dispose();
            if (Input.IsActionPressed(action)) throw new InvalidOperationException("The last simulated input owner was not released.");
            GD.Print("[diagnostic-bindings-smoke] PASS: physical release and independent simulated input ownership");
        }
        finally
        {
            using var cleanupUp = new InputEventKey { Keycode = Key.F24, PhysicalKeycode = Key.F24, Pressed = false };
            Input.ParseInputEvent(cleanupUp); Input.FlushBufferedEvents(); Input.ActionRelease(action); InputMap.EraseAction(action);
        }
    }

    private sealed class NoSources : IDiagnosticSources
    {
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => [];
    }
}

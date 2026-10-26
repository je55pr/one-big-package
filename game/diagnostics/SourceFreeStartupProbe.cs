using System.Collections.Immutable;
using Godot;

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
            if (!Input.IsActionPressed(action)) throw new InvalidOperationException("The physical-input fixture did not press its action.");
            var input = new GodotDiagnosticInputActions();
            using (var lease = input.Acquire(CancellationToken.None)) lease.SetAction(action, 1);
            Input.ParseInputEvent(up);
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
            Input.ParseInputEvent(up); Input.ActionRelease(action); InputMap.EraseAction(action);
        }
    }

    private sealed class NoSources : IDiagnosticSources
    {
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => [];
    }
}

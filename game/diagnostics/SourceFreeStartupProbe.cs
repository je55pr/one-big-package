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

    private sealed class NoSources : IDiagnosticSources
    {
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => [];
    }
}

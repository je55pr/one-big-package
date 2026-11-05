using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.PS2;

namespace OBP.Game.Diagnostics;

/// <summary>
/// Godot-only entry point for the explicit effects acceptance experiment.
/// The root supplies typed observations/actions; the diagnostic owns scenario
/// admission, construction and supervised execution.
/// </summary>
internal static class GodotEffectsSmoke
{
    internal sealed record Ports(
        Node Host,
        ObpSourceLibrary Sources,
        Func<DiagnosticWorldSnapshot> ReadWorld,
        HostOperationScope Operations,
        Func<WorldEffectHost?> CurrentEffects,
        Func<Node3D?> CurrentRoot,
        Func<string?> Unavailable,
        Func<string> ProtectedState,
        Func<string, bool> Navigate,
        Action<string, int> Quit,
        Action<string> Log,
        Action<string> Error,
        Func<IDisposable> PauseReadinessPolling);

    internal static bool IsRequested(CommandLineArgs args, int worldSwitches) =>
        args.EffectsSmoke && !args.PlaytestRequested && worldSwitches == 1;

    // The bindings are created only for the first-world explicit diagnostic;
    // ordinary world joins must not allocate a fixture or snapshot source state.
    internal static void StartIfRequested(CommandLineArgs args, int worldSwitches, Func<Ports> bind)
    {
        if (!IsRequested(args, worldSwitches)) return;
        var ports = bind();
        string report = args.AcceptanceOut ?? ProjectSettings.GlobalizePath("res://captures/effects-285/report.json");
        var fixture = new GodotEffectsFixture(ports.Host, ports.CurrentEffects, ports.CurrentRoot,
            ports.ProtectedState, Path.GetDirectoryName(Path.GetFullPath(report))!, ports.Unavailable);
        var definition = new DiagnosticScenarioDefinition("effects-room/v1", DiagnosticSourceAccess.OrdinaryPlay,
            DiagnosticSetup.ExistingWorld,
            [.. TrilogySourceDefinitions.All.Select(s => new DiagnosticSourceRequirement(s.Game, s.Authority.BuildId))],
            TimeSpan.FromMinutes(10), 30000, "obp:fusion-lab");
        var scenario = new EffectsExperimentScenario(definition, fixture,
            new EffectDiagnosticNavigation(ports.Navigate),
            new DiagnosticSourceAttachmentFixture(ports.Sources, () => { }, ports.PauseReadinessPolling), report, ports.Log);
        var runner = new DiagnosticScenarioRunner(
            new GodotDiagnosticSources(() => [.. ports.Sources.Attached.Values.Select(source =>
                new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
            new GodotDiagnosticFrameClock(ports.Host),
            new ExistingDiagnosticWorld(ports.ReadWorld, ports.Operations.Token));
        DiagnosticScenarioDispatch.Start([scenario], ports.Operations, runner, ports.Quit, ports.Error);
    }
}

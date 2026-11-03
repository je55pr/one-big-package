using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.PS2;

namespace OBP.Game.Diagnostics;

/// <summary>
/// Godot-only adapter for first-world smoke selection and supervised execution.
/// Scenario policy and its lazy harness routing remain in WorldSmokeHarness.
/// </summary>
internal static class GodotWorldSmokeDispatch
{
    internal sealed record Ports(
        Node Host,
        CommandLineArgs Args,
        ObpDestination Destination,
        int WorldSwitches,
        WorldSmokeHarness.Factories Factories,
        bool OrdinarySourceAccess,
        ObpSourceLibrary Sources,
        Func<DiagnosticWorldSnapshot> ReadWorld,
        HostOperationScope Operations,
        Action<string, int> Quit,
        Action<string> LogError);

    internal static void Start(Ports ports)
    {
        var scenarios = WorldSmokeHarness.Create(
            ports.Args, ports.Destination, ports.WorldSwitches,
            WorldSmokeHarness.Bind(ports.Factories), ports.OrdinarySourceAccess,
            TrilogySourceDefinitions.All);
        if (scenarios.Length == 0) return;

        var runner = new DiagnosticScenarioRunner(
            new GodotDiagnosticSources(() => [.. ports.Sources.Attached.Values.Select(source =>
                new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
            new GodotDiagnosticFrameClock(ports.Host),
            new ExistingDiagnosticWorld(ports.ReadWorld, ports.Operations.Token));
        DiagnosticScenarioDispatch.Start(scenarios, ports.Operations, runner, ports.Quit, ports.LogError);
    }
}

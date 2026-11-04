using System.Collections.Immutable;
using OBP.Core;

namespace OBP.Game.Diagnostics;

internal sealed record PlayerSessionDiagnosticPorts(IDiagnosticPersistentPlayerFixture Player, IDiagnosticPlayerObservations Movement,
    IDiagnosticPlayerInputFixture Input, IDiagnosticNavigationActions Navigation, IDiagnosticMenuFixture Menus,
    IDiagnosticSourceAttachmentFixture Sources, IDiagnosticWorldLifecycleFixture Lifecycle, IDiagnosticSessionEngineChecks EngineChecks,
    IDiagnosticPlayerEquipmentFixture Equipment, IDiagnosticPlayerInterfaceFixture Ui, IDiagnosticSharedProfileFixture Profile,
    IDiagnosticProfileEvidence Storage, IDiagnosticAcceptanceReporter Report, IDiagnosticCapabilitySamples Samples,
    IDiagnosticCommonContactFixture Contact, IDiagnosticRetiredActorProbe Actors, IDiagnosticCapabilityHealthFixture Health,
    Func<string?> ConfigurationIdentity, IDiagnosticPlayerAudioFixture Audio, IDiagnosticPlayerAudioEvents AudioEvents);

/// <summary>Explicit player integration scenarios. The app scope survives their own travel/repair, while fixtures own local resources.</summary>
internal sealed class PlayerSessionDiagnosticHarness(CommandLineArgs args, ImmutableArray<DiagnosticSourceRequirement> authorities,
    PlayerSessionDiagnosticPorts ports, DiagnosticScenarioRunner runner, HostOperationScope operations,
    Action<string, int> quit, Action<string> log, Action<string> logError)
{
    internal static DiagnosticScenarioRegistry Registry { get; } = new(
    [
        new("player-session-smoke", ["--player-session-smoke"], a => a.PlayerSessionSmoke),
        new("player-capabilities-smoke", ["--player-capabilities-smoke"], a => a.PlayerCapabilitiesSmoke),
        new("shared-profile-smoke", ["--profile-smoke"], a => a.ProfileSmoke is not null),
        new("player-capability-trial", [], _ => false),
    ]);
    internal static bool IsRequested(CommandLineArgs args) => Registry.Registrations.Any(entry => entry.Requested(args));
    internal void StartRequested()
    {
        var scenarios = Registry.Registrations.Where(entry => entry.Requested(args)).Select(entry => Create(entry.Id)).ToArray();
        DiagnosticScenarioDispatch.Start(scenarios, operations, runner, quit, logError);
    }
    private IDiagnosticScenario Create(string id)
    {
        var definition = new DiagnosticScenarioDefinition(id + "/v1", DiagnosticSourceAccess.ExplicitSourceDiagnostic,
            DiagnosticSetup.AssetFreeFixture, authorities, TimeSpan.FromMinutes(10), 108_000, FailureExitCode: id == "shared-profile-smoke" ? 2 : 3);
        IDiagnosticScenario scenario = id switch
        {
            "player-session-smoke" => new PersistentPlayerScenario(definition, ports.Player, ports.Movement, ports.Input, ports.Navigation,
                ports.Menus, ports.Sources, ports.Lifecycle, ports.EngineChecks, log),
            "player-capabilities-smoke" => new PlayerCapabilitiesScenario(definition, ports.Player, ports.Input, ports.Navigation, ports.Menus,
                ports.Equipment, ports.Ui, ports.Sources, ports.Samples, ports.Contact, ports.Actors, ports.Health, ports.ConfigurationIdentity, args.PlayerCapabilitiesOut, log),
            "shared-profile-smoke" => new SharedProfileScenario(definition, args.ProfileSmoke, ports.Profile, ports.Player, ports.Equipment,
                ports.Ui, ports.Storage, ports.Sources, ports.Navigation, ports.Report, log),
            _ => throw new DiagnosticUnavailableException("Unknown player diagnostic: " + id),
        };
        return new DiagnosticProcessScenario(scenario, new(id + "-pass", 0), new(id + "-fail", definition.FailureExitCode));
    }
    internal IDiagnosticScenario CreateAudio(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new Rac1PlayerAudioScenario(definition, ports.Audio, ports.AudioEvents, log), new("rac1-player-audio-smoke-pass", 0), new("rac1-player-audio-smoke-fail", 1));

    internal Task CaptureTrialAsync(CancellationToken lifetime)
    {
        lifetime.ThrowIfCancellationRequested();
        var completed = new TaskCompletionSource(); // Inline completion preserves the existing physics callback phase.
        operations.Start("player-capability-trial", async token =>
        {
            try
            {
                var definition = new DiagnosticScenarioDefinition(Registry.Find("player-capability-trial").Id + "/v1",
                    DiagnosticSourceAccess.ExplicitSourceDiagnostic, DiagnosticSetup.AssetFreeFixture, authorities, TimeSpan.FromMinutes(2), 7200, FailureExitCode: 3);
                var result = await runner.RunAsync(new StandalonePlayerCapabilityTrial(definition, ports.Samples, ports.Input), true, token);
                if (result.Status == DiagnosticRunStatus.Passed) completed.TrySetResult();
                else if (result.Status == DiagnosticRunStatus.Canceled && token.IsCancellationRequested) completed.TrySetCanceled(lifetime.IsCancellationRequested ? lifetime : token);
                else completed.TrySetException(result.Error ?? new InvalidOperationException(result.Detail));
            }
            catch (Exception error) { completed.TrySetException(error); }
        }, lifetime);
        return completed.Task;
    }
}

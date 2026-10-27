using System.Collections.Immutable;

namespace OBP.Game.Diagnostics;

internal sealed record CaptureDiagnosticPorts(ICaptureRecorderDiagnosticFixture Recorder, ICaptureEquipmentActions Equipment,
    IBugReporterDiagnosticFixture Reports, Func<IMovementStateDiagnosticFixture> MovementStates,
    ITrilogyPlayerDiagnosticFixture Player, ITrilogyActorDiagnosticFixture Actors,
    IDiagnosticNavigationActions Navigation, Func<CancellationToken, Task> FusionRecording, Func<bool> Isolated);
internal sealed record CaptureDiagnosticAuthorities(Func<ImmutableArray<DiagnosticSourceRequirement>> Current,
    ImmutableArray<DiagnosticSourceRequirement> Trilogy);

internal sealed class CaptureDiagnosticHarness(CaptureDiagnosticAuthorities authorities, CaptureDiagnosticPorts ports,
    MovementScenarioPorts movement, ICaptureDiagnosticInputFactory inputs, ICaptureDiagnosticTime time,
    IDiagnosticAcceptanceReporter acceptance, DiagnosticScenarioRunner runner, HostOperationScope operations,
    Action<string, int> quit, Action<string> log, Action<string> error)
{
    internal IDiagnosticAcceptanceReporter Acceptance => acceptance;
    internal void StartRecording(bool fusion) => Start("playtest-smoke", context => fusion ? ports.FusionRecording(context.Run.CancellationToken)
        : new PlaytestRecordingScenario(context, ports.Recorder).RunAsync(), "playtest-smoke-failed", 2, false);
    internal void StartReports() => Start("bug-report-smoke", context => new BugReporterScenario(context, ports.Recorder, ports.Reports).RunAsync(), "bug-report-smoke-fail", 1, true);
    internal void StartReconciliation() => Start("reconciliation-capture", context => Reconciliation(context).RunAsync(), "reconciliation-capture-fail", 2, false, "capture-route");
    internal void StartTrilogy() => Start("trilogy-gameplay-smoke", context => new TrilogyGameplayScenario(context, ports.Player, ports.Actors, ports.Navigation, ports.Equipment).RunAsync(), "trilogy-gameplay-smoke-fail", 1, true);
    private ReconciliationCaptureScenario Reconciliation(CaptureDiagnosticContext context) => new(context, ports.Recorder, ports.Navigation, ports.Equipment, acceptance);
    private DiagnosticScenarioDefinition Definition(string id) => new(id + "/v1", DiagnosticSourceAccess.ExplicitSourceDiagnostic,
        // Report replays poll process frames while a child imports its world. Headless
        // process frames are unpaced; their 90-second wall-clock deadlines own this wait.
        DiagnosticSetup.AssetFreeFixture, id is "reconciliation-capture" or "finished-capture-continuation" or "trilogy-gameplay-smoke"
            ? authorities.Trilogy : authorities.Current(), TimeSpan.FromMinutes(30), id == "bug-report-smoke" ? int.MaxValue : 108_000);
    private IDiagnosticScenario Scenario(DiagnosticScenarioDefinition definition, Func<CaptureDiagnosticContext, Task> execute) =>
        new CaptureOwnedScenario(definition, inputs, time, log, async context =>
        {
            if (!ports.Isolated()) throw new DiagnosticUnavailableException("Capture diagnostics require --no-profile or an isolated temporary profile/inbox under captures or the temporary directory.");
            await execute(context);
        });
    internal void Start(string id, Func<CaptureDiagnosticContext, Task> execute, string failureReason, int failureCode, bool terminal, string? evidence = null)
    {
        if (operations.Stopping) return;
        operations.Start(id, async token =>
        {
            var result = await runner.RunAsync(Scenario(Definition(id), context => AwaitCommitAsync(context, execute, terminal)), true, token);
            Finish(result, id, failureReason, failureCode, terminal, evidence, token);
        });
    }
    private async Task AwaitCommitAsync(CaptureDiagnosticContext context, Func<CaptureDiagnosticContext, Task> execute, bool terminal)
    {
        await execute(context);
        // The writer closes at its process callback. Keep terminal controls held
        // until then so lease cleanup does not append a new release to the tape.
        while (!terminal && !ports.Recorder.Read().Finished)
            await context.Run.WaitAsync(DiagnosticFramePhase.Process);
    }
    private void Finish(DiagnosticRunResult result, string id, string failureReason, int failureCode, bool terminal, string? evidence, CancellationToken token)
    {
        if (operations.Stopping || result.Status == DiagnosticRunStatus.Canceled && token.IsCancellationRequested) return;
        if (result.Status == DiagnosticRunStatus.Passed)
        {
            if (terminal) { quit(id + "-pass", 0); }
            return;
        }
        var failure = result.Error ?? new InvalidOperationException(result.Detail);
        if (evidence is not null) acceptance.Fail(evidence, failure, failureReason);
        else { error("[" + id + "] FAIL: " + failure); quit(failureReason, failureCode); }
    }
    internal async Task VerifyFinishedContinuationAsync()
    {
        var result = await runner.RunAsync(Scenario(Definition("finished-capture-continuation"), context => Reconciliation(context).VerifyFinishedContinuationAsync()), true, operations.Token);
        if (result.Status == DiagnosticRunStatus.Canceled && operations.Token.IsCancellationRequested)
            operations.Token.ThrowIfCancellationRequested();
        if (result.Status != DiagnosticRunStatus.Passed) throw result.Error ?? new InvalidOperationException(result.Detail);
    }
    internal IDiagnosticScenario CreateMovement(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        Scenario(definition, async context =>
        {
            var driver = context.Run.Own(new MovementDiagnosticDriver(context.Run, movement.Input, movement.Physical));
            await new MovementStateScenario(context, new(movement, driver), ports.MovementStates()).RunAsync();
        }), new("movement-state-smoke-pass", 0), new("movement-state-smoke-fail", 3));
    internal async Task<DiagnosticRunStatus> CheckMovementCancellationAsync(CancellationToken token)
    {
        var result = await runner.RunAsync(CreateMovement(Definition("movement-state-smoke")), true, token);
        if (result.Status is not (DiagnosticRunStatus.Passed or DiagnosticRunStatus.Canceled)) throw result.Error ?? new InvalidOperationException(result.Detail);
        return result.Status;
    }
}
public sealed class CaptureOwnedScenario(DiagnosticScenarioDefinition definition, ICaptureDiagnosticInputFactory inputs,
    ICaptureDiagnosticTime time, Action<string> log, Func<CaptureDiagnosticContext, Task> execute) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition => definition;
    public Task RunAsync(DiagnosticRunContext run) => execute(new(run, run.Own(inputs.Acquire()), time, log));
}

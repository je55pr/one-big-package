using OBP.Godot;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticExit(string Reason, int Code);

/// <summary>Temporary, invocation-only adapter. Each Phase 1 suite replaces its callback with focused typed ports.</summary>
public sealed class LegacyDiagnosticScenario(DiagnosticScenarioDefinition definition,
    Func<DiagnosticRunContext, Task> execute) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public DiagnosticExit? Exit { get; private set; }
    private int _started;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Create a fresh diagnostic instance for each run.");
        using var terminal = ApplicationQuitDeferral.Begin((reason, code) =>
        {
            if (Exit is null || code != 0) Exit = new(reason, code);
        });
        using var cancellation = LegacyDiagnosticCancellation.Bind(run.CancellationToken);
        await execute(run);
        if (Exit is null) run.CancellationToken.ThrowIfCancellationRequested();
        if (Exit is null) throw new InvalidOperationException("Legacy diagnostic completed without reporting an outcome: " + Definition.Id);
        if (Exit.Code != 0) throw new InvalidOperationException("Diagnostic reported failure: " + Exit.Reason);
    }
}

/// <summary>Temporary cancellation-only bridge for retained movement helpers; retired by #339. No host/service access.</summary>
internal static class LegacyDiagnosticCancellation
{
    private static readonly AsyncLocal<CancellationToken> Token = new();
    public static CancellationToken Current => Token.Value;
    public static bool IsOwned(OperationCanceledException error) => Current.IsCancellationRequested && error.CancellationToken == Current;
    public static IDisposable Bind(CancellationToken token)
    {
        var previous = Token.Value; Token.Value = token;
        return new Restore(previous);
    }
    private sealed class Restore(CancellationToken previous) : IDisposable
    { public void Dispose() => Token.Value = previous; }
}

/// <summary>Supervise selected diagnostics and commit their process outcome after execution/cleanup finishes.</summary>
public static class DiagnosticScenarioDispatch
{
    public static void Start(IReadOnlyList<LegacyDiagnosticScenario> scenarios, HostOperationScope operations,
        DiagnosticScenarioRunner runner, Action<string, int> quit, Action<string> logError)
    {
        if (scenarios.Count == 0) return;
        operations.Start("diagnostic-scenarios", async token =>
        {
            using var batch = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pending = scenarios.Select((scenario, index) => (Task: runner.RunAsync(scenario, explicitlyRequested: true, batch.Token), Index: index))
                .ToDictionary(entry => entry.Task, entry => entry.Index);
            (int Index, DiagnosticRunResult Result)? failure = null;
            try
            {
                while (pending.Count > 0)
                {
                    var completedTask = await Task.WhenAny(pending.Keys);
                    int index = pending[completedTask]; pending.Remove(completedTask);
                    var result = await completedTask;
                    if (failure is not null || result.Status == DiagnosticRunStatus.Passed) continue;
                    failure = (index, result);
                    batch.Cancel();
                }
            }
            finally
            {
                if (pending.Count > 0)
                {
                    batch.Cancel();
                    await Task.WhenAll(pending.Keys);
                }
            }
            if (operations.Stopping) return;
            if (failure is { } rejected)
            {
                logError("[diagnostic] " + rejected.Result.Id + ": " + (rejected.Result.Error?.ToString() ?? rejected.Result.Detail));
                var failed = scenarios[rejected.Index].Exit;
                quit(failed is { Code: > 0 } ? failed.Reason : "diagnostic-scenario-failed", failed is { Code: > 0 } ? failed.Code : rejected.Result.ExitCode);
                return;
            }
            var completed = scenarios.Select(scenario => scenario.Exit).First(exit => exit is not null)!;
            quit(completed.Reason, completed.Code);
        }, lifetime: CancellationToken.None);
    }
}

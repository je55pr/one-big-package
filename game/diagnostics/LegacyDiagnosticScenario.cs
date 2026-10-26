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
        if (Exit is null) throw new InvalidOperationException("Legacy diagnostic completed without reporting an outcome: " + Definition.Id);
        if (Exit.Code != 0) throw new InvalidOperationException("Diagnostic reported failure: " + Exit.Reason);
    }
}

/// <summary>Temporary cancellation-only bridge for retained movement helpers; retired by #339. No host/service access.</summary>
internal static class LegacyDiagnosticCancellation
{
    private static readonly AsyncLocal<CancellationToken> Token = new();
    public static CancellationToken Current => Token.Value;
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
            var results = await Task.WhenAll(scenarios.Select(scenario => runner.RunAsync(scenario, explicitlyRequested: true, token)));
            if (operations.Stopping) return;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Status == DiagnosticRunStatus.Passed) continue;
                logError("[diagnostic] " + results[i].Id + ": " + (results[i].Error?.ToString() ?? results[i].Detail));
                var failed = scenarios[i].Exit;
                quit(failed is { Code: > 0 } ? failed.Reason : "diagnostic-scenario-failed", failed is { Code: > 0 } ? failed.Code : results[i].ExitCode);
                return;
            }
            var completed = scenarios.Select(scenario => scenario.Exit).First(exit => exit is not null)!;
            quit(completed.Reason, completed.Code);
        }, lifetime: CancellationToken.None);
    }
}

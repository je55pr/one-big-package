using OBP.Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Commits a typed scenario's process outcome only after assertions and cleanup finish. Repair success may intentionally exit 2.</summary>
public sealed class DiagnosticProcessScenario(IDiagnosticScenario scenario, DiagnosticExit success, DiagnosticExit failure,
    bool requirePolicyExit = false) : IDiagnosticScenario, IDiagnosticProcessOutcome
{
    private int _started;
    public DiagnosticScenarioDefinition Definition => scenario.Definition;
    public DiagnosticExit? Exit { get; private set; }
    public DiagnosticExit FailureExit => failure;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Create a fresh diagnostic instance for each run.");
        DiagnosticExit? policyExit = null;
        using var terminal = ApplicationQuitDeferral.Begin((reason, code) =>
        {
            if (policyExit is null || code != success.Code) policyExit = new(reason, code);
        });
        try
        {
            await scenario.RunAsync(run);
            run.CancellationToken.ThrowIfCancellationRequested();
            if (requirePolicyExit && policyExit is null || policyExit is not null && policyExit.Code != success.Code)
                throw new InvalidOperationException("Unexpected application outcome during " + Definition.Id + ": " + policyExit);
            Exit = policyExit ?? success;
        }
        catch (OperationCanceledException error) when (run.CancellationToken.IsCancellationRequested && error.CancellationToken == run.CancellationToken)
        { throw; }
        catch
        {
            Exit = failure;
            throw;
        }
    }
}

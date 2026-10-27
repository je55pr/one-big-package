using System.Collections.Immutable;

namespace OBP.Game.Diagnostics;

internal sealed class FusionWeaponDiagnosticHarness(MovementScenarioPorts movement, Rac1WeaponScenarioPorts rac1, IWeaponDiagnosticTransientFixture transients,
    IFusionWeaponDiagnosticFixture fixture, ImmutableArray<DiagnosticSourceRequirement> authorities,
    DiagnosticScenarioRunner runner, HostOperationScope operations, Action<string, int> quit, Action<string> error)
{
    private DiagnosticScenarioDefinition Definition(string id, int exit) => new(id + "/v1", DiagnosticSourceAccess.ExplicitSourceDiagnostic,
        DiagnosticSetup.AssetFreeFixture, authorities, TimeSpan.FromMinutes(30), 108_000, FailureExitCode: exit);
    internal void Start() => DiagnosticScenarioDispatch.Start([new DiagnosticProcessScenario(
        new WeaponDiagnosticScenario(new MovementDiagnosticScenario(Definition("fusion-smoke", 3), movement, context => new FusionWeaponsScenario(context, rac1, fixture).RunAsync()), transients),
        new("fusion-smoke-pass", 0), new("fusion-smoke-fail", 3))], operations, runner, quit, error);
    internal Task RunRecordingAsync(CancellationToken lifetime)
    {
        lifetime.ThrowIfCancellationRequested();
        var completed = new TaskCompletionSource();
        operations.Start("fusion-record", async token =>
        {
            try
            {
                var result = await runner.RunAsync(new WeaponDiagnosticScenario(new MovementDiagnosticScenario(Definition("fusion-record", 2), movement,
                    context => new FusionWeaponRecordingScenario(context, rac1, fixture).RunAsync()), transients), true, token);
                if (result.Status == DiagnosticRunStatus.Passed) completed.TrySetResult();
                else if (result.Status == DiagnosticRunStatus.Canceled && token.IsCancellationRequested) completed.TrySetCanceled(token);
                else
                {
                    error("[fusion-record] FAIL: " + (result.Error?.ToString() ?? result.Detail));
                    quit("fusion-record-fail", 2); completed.TrySetException(result.Error ?? new InvalidOperationException(result.Detail));
                }
            }
            catch (Exception exception) { completed.TrySetException(exception); throw; }
        }, lifetime);
        return completed.Task;
    }
}

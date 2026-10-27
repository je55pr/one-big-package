using System.Numerics;

namespace OBP.Game.Diagnostics;

public sealed record FusionLabTraversalPose(float X, float Z, long ProcessFrame);
public interface IFusionLabTraversalFixture
{
    IDisposable Drive(Func<FusionLabTraversalPose, Vector2> input);
    FusionLabTraversalPose Read();
    Task<bool> ReloadDeferredAsync(CancellationToken token);
}

/// <summary>Input-driven entrance/hub/chamber traversal; only the explicitly requested fixture subscribes to frames.</summary>
public sealed class FusionLabTraversalScenario(IFusionLabTraversalFixture fixture, Action<string> log, Action<string> error) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = new("fusion-lab-traversal/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(90), 5400);
    public async Task RunAsync(DiagnosticRunContext run)
    {
        int stage = 0, snapshots = 0;
        Task<bool>? reload = null;
        using var input = fixture.Drive(pose =>
        {
            if (run.CancellationToken.IsCancellationRequested) return Vector2.Zero;
            if (stage == 0 && pose.Z > -5)
            {
                stage = 1;
                log($"[fusion-smoke] entered hub at z={pose.Z:0.00}");
            }
            if (stage == 1 && pose.Z > 14)
            {
                stage = 2;
                log($"[fusion-smoke] entered test chamber at z={pose.Z:0.00}; returning");
            }
            if (stage == 2 && pose.Z < -13)
            {
                stage = 3;
                log($"[fusion-smoke] returned to entrance at z={pose.Z:0.00}; staging reload");
                reload = fixture.ReloadDeferredAsync(run.CancellationToken);
            }
            return stage switch { 0 or 1 => new(0, -1), 2 => new(0, 1), _ => Vector2.Zero };
        });
        while (reload is null)
        {
            await run.WaitAsync(DiagnosticFramePhase.Process);
            if (reload is not null) break;
            var pose = fixture.Read();
            if (pose.ProcessFrame % 120 == 0)
                log($"[fusion-smoke] stage={stage} x={pose.X:0.00} z={pose.Z:0.00} elapsed={++snapshots * 2}s");
        }
        if (await reload)
            log("[fusion-smoke] PASS: entrance -> hub -> chamber -> entrance, unload and reload via normal world host");
        else
            error("[fusion-smoke] FAIL: traversal succeeded, but re-entry after teardown failed");
    }
}

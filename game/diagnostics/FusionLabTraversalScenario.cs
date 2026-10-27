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
        int snapshots = 0;
        var route = new Traversal(fixture, run.CancellationToken, log);
        using var input = fixture.Drive(route.Sample);
        Task<bool>? reload = route.Reload;
        while (reload is null)
        {
            await run.WaitAsync(DiagnosticFramePhase.Process);
            reload = route.Reload;
            if (reload is not null) break;
            var pose = fixture.Read();
            if (pose.ProcessFrame % 120 == 0)
                log($"[fusion-smoke] stage={route.Stage} x={pose.X:0.00} z={pose.Z:0.00} elapsed={++snapshots * 2}s");
        }
        if (await reload)
            log("[fusion-smoke] PASS: entrance -> hub -> chamber -> entrance, unload and reload via normal world host");
        else
        {
            error("[fusion-smoke] FAIL: traversal succeeded, but re-entry after teardown failed");
            throw new InvalidOperationException("Fusion Lab re-entry after teardown failed.");
        }
    }
    private sealed class Traversal(IFusionLabTraversalFixture fixture, CancellationToken token, Action<string> log)
    {
        public int Stage { get; private set; }
        public Task<bool>? Reload { get; private set; }
        public Vector2 Sample(FusionLabTraversalPose pose)
        {
            if (token.IsCancellationRequested) return Vector2.Zero;
            if (Stage == 0 && pose.Z > -5)
            {
                Stage = 1;
                log($"[fusion-smoke] entered hub at z={pose.Z:0.00}");
            }
            if (Stage == 1 && pose.Z > 14)
            {
                Stage = 2;
                log($"[fusion-smoke] entered test chamber at z={pose.Z:0.00}; returning");
            }
            if (Stage == 2 && pose.Z < -13)
            {
                Stage = 3;
                log($"[fusion-smoke] returned to entrance at z={pose.Z:0.00}; staging reload");
                Reload = fixture.ReloadDeferredAsync(token);
            }
            return Stage switch { 0 or 1 => new(0, -1), 2 => new(0, 1), _ => Vector2.Zero };
        }
    }
}

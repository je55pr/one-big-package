using OBP.RAC2;

namespace OBP.Game.Diagnostics;

public sealed record StressRetentionSnapshot(DiagnosticWorldSnapshot World, ulong? SceneRoot, ulong? Player, int CampaignLevel);
public sealed record StressCounterSnapshot(int Level, double MemoryMb, double Objects, double Nodes, double Orphans, int Descendants, bool? OnFloor);
public interface IStressWorldFixture
{
    StressRetentionSnapshot ReadRetention();
    bool RejectImport(string missingPath);
    void ShowSelector();
    void OpenDestination(string destinationId);
    StressCounterSnapshot ReadCounters();
}

/// <summary>Explicit CLI lifecycle exercise using immutable world witnesses and the ordinary navigation route.</summary>
public sealed class StressSwitchCliService(IStressWorldFixture world, IDiagnosticFrameClock frames,
    Action<string, int> quit, Action<string> log, Action<string> error)
{
    public async Task RunStressSwitchAsync(string sequence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tokens = sequence.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = new List<string>();
        double baseMem = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string token = tokens[i];
            if (token == "__bad-import")
            {
                var retained = world.ReadRetention();
                bool rejected = world.RejectImport(Path.Combine(Path.GetTempPath(), "obp-world-does-not-exist-" + Guid.NewGuid().ToString("N") + ".iso"));
                if (!rejected || retained != world.ReadRetention())
                {
                    error("[stress] FAIL failed import destroyed the active world or campaign state");
                    quit("stress-import-retention-failed", 1);
                    return;
                }
                log("[stress] PASS rejected bad import, retained world/player/campaign");
                continue;
            }
            int? level = token.StartsWith("rac", StringComparison.OrdinalIgnoreCase) ? null : Rac2PlanetCatalogue.Resolve(token);
            string? destination = token.StartsWith("rac", StringComparison.OrdinalIgnoreCase) ? token : level is null ? null : $"rac2:LEVEL{level.Value}";
            if (destination is null)
            {
                error($"[stress] unknown planet '{token}' — skipped");
                continue;
            }
            // Preserve the every-third token bounce, including skipped/failed-import token positions.
            if (i > 0 && i % 3 == 0)
            {
                world.ShowSelector();
                for (int f = 0; f < 10; f++) await frames.NextAsync(DiagnosticFramePhase.Process, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            world.OpenDestination(destination);
            for (int f = 0; f < 45; f++) await frames.NextAsync(DiagnosticFramePhase.Process, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await frames.NextAsync(DiagnosticFramePhase.Process, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var counters = world.ReadCounters();
            if (i == 0) baseMem = counters.MemoryMb;
            string row = $"#{i,2} {token,-10} L{counters.Level,-2} | mem {counters.MemoryMb,7:0.0} MB (+{counters.MemoryMb - baseMem,6:0.0}) | " +
                $"objects {counters.Objects,7:0} nodes {counters.Nodes,6:0} orphans {counters.Orphans,4:0} | " +
                $"worldRoot desc {counters.Descendants,5} | onFloor {counters.OnFloor}";
            rows.Add(row);
            log("[stress] " + row);
        }
        cancellationToken.ThrowIfCancellationRequested();
        log("[stress] ================ summary ================");
        foreach (string row in rows) log("[stress] " + row);
        quit("stress-switch-complete", 0);
    }
}

using Godot;
using OBP.Godot;
using OBP.RAC2;

namespace OneBigPackage;

/// <summary>Developer world-hop lifecycle stress diagnostics.</summary>
public partial class OBPGame
{
    /// <summary>
    /// Phase 14 lifecycle stress test: load each planet in <paramref name="seq"/>
    /// through the generic path, let it settle, and log node / object / memory
    /// counts so a leak (orphan nodes, duplicated meshes, creeping memory) shows
    /// up as a trend. Headless — quits when done.
    /// </summary>
    private async System.Threading.Tasks.Task RunStressSwitchAsync(string seq)
    {
        var tokens = seq.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var rows = new System.Collections.Generic.List<string>();
        double baseMem = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            string tok = tokens[i];
            if (tok == "__bad-import")
            {
                var retainedWorld = _world;
                var retainedRoot = _worldHost.Root;
                var retainedPlayer = _player;
                int retainedLevel = _rac1CampaignSession.Campaign.CurrentLevel;
                var activeDestination = _activeDestination;
                bool rejected = activeDestination is not null && retainedRoot is not null &&
                    !EnterProviderWorld(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obp-world-does-not-exist-" + System.Guid.NewGuid().ToString("N") + ".iso"),
                        activeDestination);
                if (!rejected || retainedWorld != _world || retainedRoot != _worldHost.Root ||
                    retainedPlayer != _player || retainedLevel != _rac1CampaignSession.Campaign.CurrentLevel)
                {
                    GD.PrintErr("[stress] FAIL failed import destroyed the active world or campaign state");
                    ApplicationLifecycle.RequestQuit(this, "stress-import-retention-failed", 1);
                    return;
                }
                GD.Print("[stress] PASS rejected bad import, retained world/player/campaign");
                continue;
            }

            int? lvl = tok.StartsWith("rac", System.StringComparison.OrdinalIgnoreCase)
                ? null : GcPlanetCatalogue.Resolve(tok);
            string? destinationId = tok.StartsWith("rac", System.StringComparison.OrdinalIgnoreCase)
                ? tok : lvl is null ? null : $"rac2:LEVEL{lvl.Value}";
            if (destinationId is null)
            {
                GD.PrintErr($"[stress] unknown planet '{tok}' — skipped");
                continue;
            }

            // Every third hop, bounce through the neutral Worlds browser. This
            // exercises the same teardown/navigation path as interactive back.
            if (i > 0 && i % 3 == 0)
            {
                ShowDestinationSelector();
                for (int f = 0; f < 10; f++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }

            OpenDestinationFromBootstrap(destinationId);
            for (int f = 0; f < 45; f++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            double mem = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;
            double objs = Performance.GetMonitor(Performance.Monitor.ObjectCount);
            double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            if (i == 0)
            {
                baseMem = mem;
            }

            string row = $"#{i,2} {tok,-10} L{(_world?.LevelId ?? -1),-2} | mem {mem,7:0.0} MB (+{mem - baseMem,6:0.0}) | " +
                         $"objects {objs,7:0} nodes {nodes,6:0} orphans {orphans,4:0} | " +
                         $"worldRoot desc {CountDescendants(_worldRoot),5} | onFloor {_player?.IsOnFloor()}";
            rows.Add(row);
            GD.Print("[stress] " + row);
        }

        GD.Print("[stress] ================ summary ================");
        foreach (var r in rows)
        {
            GD.Print("[stress] " + r);
        }

        ApplicationLifecycle.RequestQuit(this, "stress-switch-complete", 0);
    }

    private static int CountDescendants(Node n)
    {
        int c = n.GetChildCount();
        foreach (var child in n.GetChildren())
        {
            c += CountDescendants(child);
        }

        return c;
    }
}

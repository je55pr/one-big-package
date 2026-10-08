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
            int? lvl = GcPlanetCatalogue.Resolve(tok);
            if (lvl is null)
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

            OpenDestinationFromBootstrap($"rac2:LEVEL{lvl.Value}");
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

            string row = $"#{i,2} {tok,-10} L{lvl,-2} | mem {mem,7:0.0} MB (+{mem - baseMem,6:0.0}) | " +
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

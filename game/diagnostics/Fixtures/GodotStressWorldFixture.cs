using Godot;

namespace OBP.Game.Diagnostics;

internal sealed class GodotStressWorldFixture(Func<StressRetentionSnapshot> retention, Func<string, bool> rejectImport,
    Action selector, Action<string> open, Node worldRoot, Func<int> level, Func<bool?> onFloor) : IStressWorldFixture
{
    public StressRetentionSnapshot ReadRetention() => retention();
    public bool RejectImport(string missingPath) => rejectImport(missingPath);
    public void ShowSelector() => selector();
    public void OpenDestination(string destinationId) => open(destinationId);
    public StressCounterSnapshot ReadCounters() => new(level(),
        Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0,
        Performance.GetMonitor(Performance.Monitor.ObjectCount), Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
        Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), CountDescendants(worldRoot), onFloor());
    private static int CountDescendants(Node node)
    {
        int count = node.GetChildCount();
        foreach (var child in node.GetChildren()) count += CountDescendants(child);
        return count;
    }
}

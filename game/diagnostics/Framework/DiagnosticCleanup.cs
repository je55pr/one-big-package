namespace OBP.Game.Diagnostics;

/// <summary>Attempts every owned restoration and retains each failure for the scenario runner.</summary>
public static class DiagnosticCleanup
{
    public static void Run(string phase, params Action[] restorations)
    {
        List<Exception> failures = [];
        foreach (var restore in restorations)
        {
            try { restore(); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException(phase, failures);
    }
}

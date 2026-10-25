namespace OBP.Game;

/// <summary>Desktop isolation for automated runs; independent of gameplay/parity mode.</summary>
public static class AutomationRunPolicy
{
    public static bool IsAutomated(IReadOnlyList<string> userArgs, IReadOnlyList<string> engineArgs)
    {
        // Detect the flag convention, not an ever-growing list of individual
        // tests. New smoke flags must be safe before their dispatcher knows them.
        if (userArgs.Any(arg => arg == "--automation" || arg == "--smoke" || arg == "--source-setup-check" ||
            arg.StartsWith("--", StringComparison.Ordinal) && arg.EndsWith("-smoke", StringComparison.Ordinal))) return true;
        if (engineArgs.Any(arg => arg is "--headless" or "--quit" or "--quit-after")) return true;
        if (engineArgs.Any(arg => (arg.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".gd", StringComparison.OrdinalIgnoreCase)) &&
            Path.GetFileName(arg).Contains("smoke", StringComparison.OrdinalIgnoreCase))) return true;
        if (userArgs.Any(arg => arg is "--capture-frame" or "--shots" or "--stress-switch" or "--replay-forward" or "--replay-playtest")) return true;
        if (userArgs.Contains("--replay-bug-report") && !userArgs.Contains("--playtest-inspect-marker")) return true;
        for (int i = 0; i + 1 < userArgs.Count; i++)
            if (userArgs[i] == "--test-scene" && userArgs[i + 1].Contains("smoke", StringComparison.OrdinalIgnoreCase)) return true;
        // Human play and manual input recording retain normal desktop controls.
        return false;
    }
}

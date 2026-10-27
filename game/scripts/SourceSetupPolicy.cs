namespace OBP.Game;

/// <summary>Explicit diagnostic source access; independent of player provider/parity and desktop isolation.</summary>
public static class SourceSetupPolicy
{
    public static SourceAccessMode Resolve(CommandLineArgs args, IReadOnlyList<string> userArgs, IReadOnlyList<string> engineArgs) =>
        IsDiagnostic(args, userArgs, engineArgs) ? SourceAccessMode.ExplicitDiagnostic : SourceAccessMode.OrdinaryPlay;
    public static bool IsDiagnostic(CommandLineArgs args, IReadOnlyList<string> userArgs, IReadOnlyList<string> engineArgs) =>
        !args.ReconciliationSmoke && !args.SourceSetupCheck && !args.OrdinaryUiSmoke && !args.EffectsSmoke &&
        (args.DiagnosticSources || args.NativeParityRequested || args.DiagnosticPlayer || args.TestSceneExplicit ||
        args.ShotsPath is not null || args.CaptureFrame is not null || args.StressSwitch is not null || args.ProfileSmoke is not null ||
        userArgs.Any(argument => argument.StartsWith("--", StringComparison.Ordinal) && argument.EndsWith("-smoke", StringComparison.Ordinal)) ||
        engineArgs.Any(argument => (argument.EndsWith(".gd", StringComparison.OrdinalIgnoreCase) || argument.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)) &&
            Path.GetFileName(argument).Contains("smoke", StringComparison.OrdinalIgnoreCase)));
}

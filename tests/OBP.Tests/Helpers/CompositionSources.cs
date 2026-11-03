namespace OBP.Tests;

/// <summary>
/// Source of the application composition layer: the Godot root plus the composition
/// files that bind owned components to narrow ports. Ownership tests that assert how
/// something is wired read this union, so moving a binding between composition files
/// never weakens the assertion, while forbidden text stays forbidden everywhere.
/// </summary>
internal static class CompositionSources
{
    private static readonly string[] Files =
    [
        "game/scripts/OBPGame.cs",
        "game/diagnostics/ApplicationDiagnostics.cs",
        "game/scripts/Rac1WorldComposition.cs",
        "game/scripts/Rac2WorldComposition.cs",
        "game/scripts/CompositionBase.cs",
    ];

    private static readonly Lazy<string> Union = new(() => string.Join("\n", Files.Select(Read)));
    private static readonly Lazy<string> DiagnosticText = new(() => Read("game/diagnostics/ApplicationDiagnostics.cs"));

    public static string Application => Union.Value;

    public static string Diagnostics => DiagnosticText.Value;

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoPaths.Root, relative));
}

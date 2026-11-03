namespace OBP.Game.Diagnostics;

/// <summary>Resolves diagnostic reproduction file names inside the captures/repros directory only.</summary>
internal static class DiagnosticReproPath
{
    public static string Resolve(string capturesRoot, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.AsSpan().IndexOfAny('/', '\\', ':') >= 0 || name != Path.GetFileName(name) || name is "." or "..")
            throw new ArgumentException("A reproduction name must be a plain file name.", nameof(name));
        string directory = Path.GetFullPath(Path.Combine(capturesRoot, "repros"));
        string path = Path.GetFullPath(Path.Combine(directory, name));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("A reproduction path must stay inside captures/repros.", nameof(name));
        return path;
    }
}

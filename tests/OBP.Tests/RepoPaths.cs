namespace OBP.Tests;

/// <summary>Locates repo-relative resources (manifests, fixtures) from the test bin dir.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Manifests => Path.Combine(Root, "research", "manifests");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
                Directory.Exists(Path.Combine(dir.FullName, "research")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OBP repo root from the test bin directory.");
    }
}

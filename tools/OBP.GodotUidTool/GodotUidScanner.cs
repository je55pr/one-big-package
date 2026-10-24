using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace OBP.GodotUidTool;

/// <summary>
/// Validate Godot C# script UID sidecars without assigning any new UID.
/// Godot's own pinned .NET editor remains the sole UID generator.
/// </summary>
public static class GodotUidScanner
{
    private static readonly string[] HostFolders = ["game/scripts", "game/diagnostics"];
    private static readonly Regex UidPattern = new("^uid://[a-z0-9]+$", RegexOptions.CultureInvariant);

    public sealed record ScanResult(
        IReadOnlyList<string> Sources,
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Errors,
        IReadOnlyDictionary<string, byte[]> OriginalSidecars);

    public static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    public static ScanResult Scan(string root)
    {
        string[] sources = HostFolders
            .SelectMany(folder => Enumerate(root, folder, "*.cs"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] sidecars = HostFolders
            .SelectMany(folder => Enumerate(root, folder, "*.cs.uid"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        List<string> missing = [];
        List<string> errors = [];
        Dictionary<string, string> seen = new(StringComparer.Ordinal);
        Dictionary<string, byte[]> originals = new(StringComparer.Ordinal);

        foreach (string sidecar in sidecars)
        {
            if (!File.Exists(sidecar[..^4]))
                errors.Add($"Orphan UID {Relative(root, sidecar)}: restore its script or move the sidecar with the renamed .cs file (git mv).");
        }

        foreach (string source in sources)
        {
            string sidecar = source + ".uid";
            if (!File.Exists(sidecar))
            {
                missing.Add(sidecar);
                continue;
            }
            byte[] bytes = File.ReadAllBytes(sidecar);
            originals.Add(sidecar, bytes);
            string uid = Encoding.UTF8.GetString(bytes).Trim();
            if (!UidPattern.IsMatch(uid))
            {
                errors.Add($"Malformed UID in {Relative(root, sidecar)}: expected uid:// followed by lowercase letters/digits. Restore the committed value.");
                continue;
            }
            if (!seen.TryAdd(uid, sidecar))
                errors.Add($"Duplicate UID {uid}: {Relative(root, seen[uid])} and {Relative(root, sidecar)}. Restore the original UID; do not automatically regenerate it.");
        }

        return new ScanResult(sources, missing, errors, originals);
    }

    private static IEnumerable<string> Enumerate(string root, string folder, string pattern)
    {
        string directory = Path.Combine(root, folder);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            : [];
    }

    public static IReadOnlyList<string> Untracked(string root, ScanResult scan)
    {
        string output = RunCapture("git", ["-C", root, "ls-files", "--cached", "-z", "--", .. HostFolders], root);
        HashSet<string> tracked = output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        return scan.Sources
            .Select(source => source + ".uid")
            .Where(sidecar => File.Exists(sidecar) && !tracked.Contains(Relative(root, sidecar)))
            .Select(sidecar => $"Untracked UID {Relative(root, sidecar)}: git add {Relative(root, sidecar)} and commit it.")
            .ToArray();
    }

    public static string RunCapture(string command, IEnumerable<string> arguments, string cwd)
    {
        using Process process = Launch(command, arguments, cwd, redirect: true);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{command} exited {process.ExitCode}: {error.Trim()}");
        return output;
    }

    public static void RunInteractive(string command, IEnumerable<string> arguments, string cwd)
    {
        using Process process = Launch(command, arguments, cwd, redirect: false);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(command)} exited {process.ExitCode}.");
    }

    private static Process Launch(string command, IEnumerable<string> arguments, string cwd, bool redirect)
    {
        ProcessStartInfo start = new(command)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            CreateNoWindow = redirect,
        };
        foreach (string arg in arguments)
            start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {command}.");
    }
}

using System.Text.Json;

namespace OBP.GodotUidTool;

/// <summary>
/// Validate, then ask the pinned Godot .NET editor to generate missing UIDs.
/// Never fabricate or rewrite an existing valid UID.
/// </summary>
public static class GodotUidCommand
{
    public static int Run(string[] args, TextWriter output, TextWriter errors)
    {
        string root = Directory.GetCurrentDirectory();
        string? godotOverride = null;
        bool check = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--check":
                    check = true;
                    break;
                case "--sync":
                    check = false;
                    break;
                case "--root" when i + 1 < args.Length:
                    root = Path.GetFullPath(args[++i]);
                    break;
                case "--godot" when i + 1 < args.Length:
                    godotOverride = Path.GetFullPath(args[++i]);
                    break;
                case "--help":
                case "-h":
                    output.WriteLine("Usage: dotnet run --project tools/OBP.GodotUidTool -- [--check|--sync] [--root PATH] [--godot PATH]");
                    output.WriteLine("Run tools/bootstrap-godot.ps1 or tools/bootstrap-godot.sh before generating missing IDs. Commit the generated sidecars.");
                    return 0;
                default:
                    errors.WriteLine($"Unknown option {args[i]}. Run --help for usage.");
                    return 1;
            }
        }

        GodotUidScanner.ScanResult scan = GodotUidScanner.Scan(root);
        foreach (string diagnostic in scan.Errors)
            errors.WriteLine("ERROR: " + diagnostic);
        if (scan.Errors.Count > 0)
        {
            errors.WriteLine("UID sync refused to alter existing IDs. Correct these issues and rerun.");
            return 1;
        }

        if (check)
        {
            List<string> problems = scan.Missing
                .Select(file => $"Missing UID {GodotUidScanner.Relative(root, file)}: run tools/sync-godot-uids.ps1 (or .sh) and commit it.")
                .Concat(GodotUidScanner.Untracked(root, scan))
                .ToList();
            foreach (string diagnostic in problems)
                errors.WriteLine("ERROR: " + diagnostic);
            if (problems.Count > 0) return 1;
            output.WriteLine($"Godot UID check OK: {scan.Sources.Count} scripts, unique tracked sidecars, no orphans.");
            return 0;
        }

        if (scan.Missing.Count == 0)
        {
            output.WriteLine($"Godot UIDs already synchronized ({scan.Sources.Count} scripts). No Godot import needed.");
            return 0;
        }

        string godot = ResolvePinnedGodot(root, godotOverride, out string expectedVersion);
        if (!File.Exists(godot))
            throw new FileNotFoundException($"Pinned Godot .NET editor not found: {godot}. Run tools/bootstrap-godot.ps1 or tools/bootstrap-godot.sh.", godot);
        string version = GodotUidScanner.RunCapture(godot, ["--version"], root).Trim();
        if (!version.StartsWith(expectedVersion.Split('-')[0], StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected pinned Godot {expectedVersion}, but {godot} reported {version}.");

        output.WriteLine($"Generating {scan.Missing.Count} missing UID sidecar(s) using pinned Godot {expectedVersion}:");
        foreach (string file in scan.Missing)
            output.WriteLine("  " + GodotUidScanner.Relative(root, file));
        output.Flush();

        bool changedExisting = false;
        try
        {
            // Godot needs a compiled C# host for the project autoload scripts.
            GodotUidScanner.RunInteractive("dotnet", ["build", "game/OneBigPackage.csproj", "-c", "Debug", "--nologo"], root);
            GodotUidScanner.RunInteractive(godot, ["--headless", "--path", "game", "--editor", "--import", "--quit"], root);
        }
        finally
        {
            // Also restore valid IDs if build/import fails partway through.
            foreach ((string file, byte[] bytes) in scan.OriginalSidecars)
            {
                if (File.Exists(file) && File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))
                    continue;
                File.WriteAllBytes(file, bytes);
                errors.WriteLine($"ERROR: Godot modified existing UID; original bytes restored: {GodotUidScanner.Relative(root, file)}");
                changedExisting = true;
            }
        }

        GodotUidScanner.ScanResult after = GodotUidScanner.Scan(root);
        foreach (string diagnostic in after.Errors)
            errors.WriteLine("ERROR: " + diagnostic);
        foreach (string missing in after.Missing)
            errors.WriteLine($"ERROR: Godot did not generate {GodotUidScanner.Relative(root, missing)}. Check the C# build/import output.");
        if (changedExisting || after.Errors.Count > 0 || after.Missing.Count > 0)
            return 1;

        output.WriteLine($"Generated {scan.Missing.Count} UID sidecar(s). Stage them with git add, then run --check and commit.");
        return 0;
    }

    private static string ResolvePinnedGodot(string root, string? godotOverride, out string version)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "godot-toolchain.json")));
        JsonElement godot = manifest.RootElement.GetProperty("godot");
        version = godot.GetProperty("version").GetString()!;
        if (godotOverride is not null) return godotOverride;
        string? platform = OperatingSystem.IsWindows() ? "win64"
            : OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                System.Runtime.InteropServices.Architecture.X64 ? "linux_x86_64" : null;
        if (platform is null)
            throw new PlatformNotSupportedException("No pinned Godot editor for this OS/architecture. Supply --godot PATH to the matching .NET editor.");
        string relativeExe = godot.GetProperty("artifacts").GetProperty(platform).GetProperty("exe").GetString()!;
        return Path.Combine(root, ".tools", "godot", relativeExe.Replace('/', Path.DirectorySeparatorChar));
    }
}

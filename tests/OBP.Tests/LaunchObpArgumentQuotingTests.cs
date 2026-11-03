using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OBP.Tests;

public sealed class LaunchObpArgumentQuotingTests
{
    private static readonly string[] Cases =
    [
        @"C:\Users\A B\one big package\game",
        @"C:\path with space\trailing\",
        "say \"hi\" there",
        "ends with quote\"",
        "backslash before quote a\\\"b",
        "two backslashes before quote a\\\\\"b",
        "trailing backslashes tail\\\\",
        "",
        "--fixed-fps",
        "plain",
    ];

    /// <summary>Runs the real quoting statement from launch-obp.ps1 and parses the joined command line
    /// with the Windows CRT rules, so the child process would receive exactly the original arguments.</summary>
    [SkippableFact]
    public async Task LaunchObpQuotingPreservesSpacesEmbeddedQuotesAndTrailingBackslashes()
    {
        string[] shellNames = OperatingSystem.IsWindows()
            ? ["pwsh.exe", "powershell.exe"]
            : ["pwsh"];
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        string? shell = shellNames.SelectMany(name => directories.Select(directory => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists);
        Skip.If(shell is null, "PowerShell is not installed.");

        string[] statements = File.ReadAllLines(Path.Combine(RepoPaths.Root, "tools", "launch-obp.ps1"))
            .Where(line => line.TrimStart().StartsWith("$quotedGameArgs = ", StringComparison.Ordinal)).ToArray();
        Assert.Single(statements);
        Assert.Contains("Start-Process -FilePath $gameExe -ArgumentList $quotedGameArgs",
            File.ReadAllText(Path.Combine(RepoPaths.Root, "tools", "launch-obp.ps1")), StringComparison.Ordinal);

        string directory = Path.Combine(Path.GetTempPath(), "obp launch quoting " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.json"), script = Path.Combine(directory, "quote.ps1");
            File.WriteAllText(input, JsonSerializer.Serialize(Cases));
            File.WriteAllText(script, string.Join('\n',
                "$ErrorActionPreference = 'Stop'",
                "$gameArgs = [string[]](ConvertFrom-Json -InputObject (Get-Content -LiteralPath '" + input.Replace("'", "''") + "' -Raw))",
                statements[0],
                "ConvertTo-Json -InputObject @($quotedGameArgs) -Compress"));

            var start = new ProcessStartInfo(shell!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await error);

            string[] quoted = JsonSerializer.Deserialize<string[]>(await output)!;
            Assert.Equal(Cases.Length, quoted.Length);
            Assert.Equal(Cases, ParseWindowsCommandLine(string.Join(' ', quoted)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Microsoft C runtime / CommandLineToArgvW rules for the characters this launcher can emit.
    private static List<string> ParseWindowsCommandLine(string line)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, inArgument = false;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\')
            {
                int slashes = 0;
                while (i < line.Length && line[i] == '\\') { slashes++; i++; }
                if (i < line.Length && line[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 1) { current.Append('"'); i++; }
                }
                else current.Append('\\', slashes);
                inArgument = true;
            }
            else if (c == '"') { inQuotes = !inQuotes; inArgument = true; i++; }
            else if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (inArgument) { arguments.Add(current.ToString()); current.Clear(); inArgument = false; }
                i++;
            }
            else { current.Append(c); inArgument = true; i++; }
        }
        if (inArgument) arguments.Add(current.ToString());
        return arguments;
    }
}

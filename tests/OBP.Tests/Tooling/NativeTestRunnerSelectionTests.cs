using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OBP.Tests;

public sealed class NativeTestRunnerSelectionTests
{
    [Theory]
    [InlineData("tools/uya-shock-cannon-native-trace.py")]
    [InlineData("tools/trilogy-skeleton-native-trace.py")]
    [InlineData("tools/rac1-veldin-spawn-probe.ps1")]
    public void FocusedRunnerFiltersResolveToClassesWithDiscoveredTestMethods(string path)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RepoPaths.Root)) + Path.DirectorySeparatorChar;
        string scriptPath = Path.GetFullPath(Path.Combine(root, path));
        Assert.StartsWith(root, scriptPath, StringComparison.Ordinal);
        string script = File.ReadAllText(scriptPath);
        Assert.Contains("RunConfiguration.TreatNoTestsAsError=true", script);
        if (path.EndsWith(".py", StringComparison.Ordinal)) Assert.Contains("cwd=ROOT", script);
        var filter = Assert.Single(Regex.Matches(script, @"FullyQualifiedName~(?<type>[A-Za-z0-9_.]+)",
            RegexOptions.None, TimeSpan.FromSeconds(1)).Cast<Match>());
        string name = filter.Groups["type"].Value;
        var selected = Assert.Single(Assembly.GetExecutingAssembly().GetTypes(), type => type.Name == name || type.FullName == name);
        Assert.Contains(selected.GetMethods(), method => method.GetCustomAttributesData()
            .Any(attribute => typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoSelectedTestsOnlyFailsWhenTheRunnerRequestsIt(bool requireTests)
    {
        // Execute the installed test platform; no retail fixture or nested build is needed.
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(host)
        {
            WorkingDirectory = RepoPaths.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "vstest", typeof(NativeTestRunnerSelectionTests).Assembly.Location,
            "--TestCaseFilter:FullyQualifiedName=OBP.Tests.DeliberatelyAbsentRunnerCase", "--",
            "RunConfiguration.TreatNoTestsAsError=" + requireTests.ToString().ToLowerInvariant() })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the test platform.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
        string transcript = await output + await error;
        Assert.Contains("No test matches", transcript);
        if (requireTests) Assert.NotEqual(0, process.ExitCode);
        else Assert.Equal(0, process.ExitCode);
    }
}

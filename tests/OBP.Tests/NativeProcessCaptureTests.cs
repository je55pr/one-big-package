using System.Diagnostics;
using System.Text;

namespace OBP.Tests;

public sealed class NativeProcessCaptureTests
{
    [SkippableTheory]
    [InlineData("powershell.exe", 0)]
    [InlineData("powershell.exe", 2)]
    [InlineData("powershell.exe", 17)]
    [InlineData("pwsh.exe", 0)]
    [InlineData("pwsh.exe", 2)]
    [InlineData("pwsh.exe", 17)]
    public async Task CapturesBothPipesAndPreservesArgumentsAndExitCodesUnderStopOnError(string shell, int exitCode)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "The replay launcher uses the Windows Godot toolchain.");
        shell = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp native capture " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string child = Path.Combine(directory, "child with spaces.ps1");
            string stdout = Path.Combine(directory, "stdout.log"), stderr = Path.Combine(directory, "stderr.log");
            const string message = "Ratchet \u2713 space & apostrophe ' quote \" trailing\\";
            File.WriteAllText(child, """
                param([string]$Message, [int]$ReturnCode)
                [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
                [Console]::Out.WriteLine($Message)
                [Console]::Out.WriteLine(('o' * 70000))
                [Console]::Error.WriteLine('expected native diagnostic')
                [Console]::Error.WriteLine(('e' * 70000))
                exit $ReturnCode
                """);
            string script = $"""
                $ErrorActionPreference = 'Stop'
                $PSNativeCommandUseErrorActionPreference = $true
                . {Quote(Path.Combine(RepoPaths.Root, "tools", "obp-native-process.ps1"))}
                $code = Invoke-ObpNativeProcessCapture -FilePath {Quote(shell)} -ArgumentList @(
                    '-NoProfile', '-NonInteractive', '-File', {Quote(child)}, '-Message', {Quote(message)}, '-ReturnCode', '{exitCode}'
                ) -StandardOutputPath {Quote(stdout)} -StandardErrorPath {Quote(stderr)}
                [Console]::Out.WriteLine(('captured-exit=' + $code))
                """;
            var result = await Run(shell, script);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("captured-exit=" + exitCode, result.Output);
            Assert.Empty(result.Error);
            Assert.Equal(message, File.ReadLines(stdout).First());
            Assert.Contains("expected native diagnostic", File.ReadAllText(stderr));
            Assert.True(new FileInfo(stdout).Length > 70000 && new FileInfo(stderr).Length > 70000);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [SkippableFact]
    public async Task NativeLaunchErrorsRemainFatal()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "The replay launcher uses the Windows Godot toolchain.");
        string script = $"""
            $ErrorActionPreference = 'Stop'
            . {Quote(Path.Combine(RepoPaths.Root, "tools", "obp-native-process.ps1"))}
            Invoke-ObpNativeProcessCapture -FilePath 'obp-nonexistent-native-process.exe' -ArgumentList @('probe') `
                -StandardOutputPath 'unused.stdout.log' -StandardErrorPath 'unused.stderr.log'
            """;
        var result = await Run(RequireShell("powershell.exe"), script);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.Error);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string RequireShell(string name)
    {
        string? path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        Skip.If(path is null, name + " is not installed.");
        return path!;
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(string shell, string script)
    {
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference = 'SilentlyContinue'\n" + script)));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout, await stderr);
    }
}

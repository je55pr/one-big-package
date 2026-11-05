using System.Diagnostics;
using System.Text;

namespace OBP.Tests;

public sealed class SmokeProcessCleanupTests
{
    [SkippableTheory]
    [InlineData("powershell.exe", "cancel-stream")]
    [InlineData("powershell.exe", "cancel-buffered")]
    [InlineData("powershell.exe", "callback")]
    [InlineData("pwsh", "cancel-stream")]
    [InlineData("pwsh", "cancel-buffered")]
    [InlineData("pwsh", "callback")]
    public async Task StoppedPipelinesAndCallbackFailuresTerminateTheirNativeChild(string shell, string fault)
    {
        shell = Shell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp process cleanup " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string pid = Path.Combine(directory, "child.pid"), child = Path.Combine(directory, "child.ps1");
            File.WriteAllText(child, $"[IO.File]::WriteAllText({Quote(pid)},[string]$PID); [Console]::Out.WriteLine('ready'); Start-Sleep -Seconds 90");
            string callback = fault == "cancel-buffered" ? "" : fault == "callback"
                ? " -OnOutput { param($line) throw 'original callback failure' }" : " -OnOutput { param($line) }";
            string invocation = $". {Quote(Path.Combine(RepoPaths.Root, "tools/obp-native-process.ps1"))}; " +
                $"Invoke-ObpNativeProcessCapture -FilePath {Quote(shell)} -ArgumentList @('-NoProfile','-NonInteractive','-File',{Quote(child)}) " +
                $"-StandardOutputPath {Quote(Path.Combine(directory, "out.log"))} -StandardErrorPath {Quote(Path.Combine(directory, "err.log"))}" + callback;
            var result = await Run(shell, $$$"""
                $ErrorActionPreference='Stop'
                $pipeline=[PowerShell]::Create()
                $child=$null
                try {
                    [void]$pipeline.AddScript({{{Quote(invocation)}}})
                    $pending=$pipeline.BeginInvoke()
                    $deadline=[DateTime]::UtcNow.AddSeconds(15)
                    $childId=0
                    while($childId -le 0) {
                        if(Test-Path -LiteralPath {{{Quote(pid)}}}){
                            [void][int]::TryParse([IO.File]::ReadAllText({{{Quote(pid)}}}),[ref]$childId)
                        }
                        if([DateTime]::UtcNow -gt $deadline){throw 'Native child did not start.'}
                        if($childId -le 0){Start-Sleep -Milliseconds 20}
                    }
                    try { $child=[Diagnostics.Process]::GetProcessById($childId) }
                    catch [ArgumentException] { } # A callback failure may already have reaped the child.
                    if({{{Quote(fault)}}} -ne 'callback'){$pipeline.Stop()}
                    $failure=''
                    try{$null=$pipeline.EndInvoke($pending)}catch{$failure=$_.Exception.ToString()}
                    $failure += ($pipeline.Streams.Error | Out-String)
                    if({{{Quote(fault)}}} -eq 'callback' -and $failure -notmatch 'original callback failure'){
                        throw ('The original callback failure was lost: '+$failure)
                    }
                    if($child -and -not $child.WaitForExit(5000)){throw 'Native child survived pipeline termination.'}
                    Write-Output 'cleanup-verified'
                } finally {
                    if($child){if(-not $child.HasExited){$child.Kill()};$child.Dispose()}
                    $pipeline.Dispose()
                }
                """);
            Assert.True(result.Code == 0, result.Text);
            Assert.Contains("cleanup-verified", result.Text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [SkippableTheory]
    [InlineData("powershell.exe")]
    [InlineData("pwsh")]
    public async Task RetentionBoundsCompletedHistoriesAndPreservesActiveAndRecentFailureLogs(string shell)
    {
        shell = Shell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp log retention " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = await Run(shell, $$$"""
                $ErrorActionPreference='Stop'
                . {{{Quote(Path.Combine(RepoPaths.Root, "tools/obp-smoke-process.ps1"))}}}
                $root={{{Quote(directory)}}}
                for($index=0;$index -lt 53;$index++){
                    $path=Join-Path $root ([guid]::NewGuid().ToString('N'))
                    $null=New-Item -ItemType Directory $path
                    Set-Content (Join-Path $path '.complete') 'completed'
                    Set-Content (Join-Path $path 'stderr.log') 'failure evidence'
                    (Get-Item -LiteralPath $path).LastWriteTimeUtc=[DateTime]::UtcNow.AddMinutes(-$index)
                    if($index -eq 0){$recent=$path}
                    if($index -eq 52){(Get-Item -LiteralPath $path).LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-15);$old=$path}
                }
                $active=Join-Path $root ([guid]::NewGuid().ToString('N'))
                $foreign=Join-Path $root 'unrelated-directory'
                $null=New-Item -ItemType Directory $active,$foreign
                Set-Content (Join-Path $foreign '.complete') 'unrelated'
                Remove-ObpSmokeHistory -Root $root
                if(@(Get-ChildItem -LiteralPath $root -Directory).Count -ne 52){throw 'Completed history count was not bounded.'}
                if(Test-Path -LiteralPath $old){throw 'Expired history survived.'}
                foreach($kept in $active,$foreign,$recent){if(-not(Test-Path -LiteralPath $kept)){throw 'Protected history was removed.'}}
                if([IO.File]::ReadAllText((Join-Path $recent 'stderr.log')) -notmatch 'failure evidence'){throw 'Recent failure log was lost.'}
                Write-Output 'retention-verified'
                """);
            Assert.True(result.Code == 0, result.Text);
            Assert.Contains("retention-verified", result.Text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string Shell(string name)
    {
        Skip.If(name == "powershell.exe" && !OperatingSystem.IsWindows(), "Windows PowerShell compatibility case.");
        string file = name == "pwsh" && OperatingSystem.IsWindows() ? "pwsh.exe" : name;
        string? executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.Combine(path, file)).FirstOrDefault(File.Exists);
        Skip.If(executable is null, file + " is not installed.");
        return executable!;
    }

    private static async Task<(int Code, string Text)> Run(string shell, string script)
    {
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}

using OBP.Game;

namespace OBP.Tests;

public sealed class BugReportReplayProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-bug-child-" + Guid.NewGuid().ToString("N"));
    private string Result => Path.Combine(_directory, "result.json");

    public BugReportReplayProcessTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAfterSpawnOrDuringWaitStopsTheChildAndRemovesItsResult(bool duringSpawn)
    {
        using var cancellation = new CancellationTokenSource();
        bool running = true, stopped = false;
        var ports = new BugReportProcessPorts(_ =>
        {
            File.WriteAllText(Result, "{\"Result\":\"matched\"}");
            if (duringSpawn) cancellation.Cancel();
            return 42;
        }, _ => running, id => { Assert.Equal(42, id); stopped = true; running = false; },
        token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, () => 0);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => BugReportReplayProcess.RunAsync(new(Result, []), ports, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(stopped);
        Assert.False(running);
        Assert.False(File.Exists(Result));
    }

    [Theory]
    [InlineData("matched")]
    [InlineData("diverged")]
    [InlineData("invalid")]
    public async Task CompletedReproductionRetainsItsResultAndOutcome(string outcome)
    {
        File.WriteAllText(Result, "{\"Result\":\"" + outcome + "\"}");
        var ports = new BugReportProcessPorts(arguments => { Assert.Equal(new[] { "--playtest-inspect-marker" }, arguments); return 42; },
            _ => false, _ => Assert.Fail("An exited child must not be stopped."), _ => Task.CompletedTask, () => 0);
        var result = await BugReportReplayProcess.RunAsync(new(Result, ["--playtest-inspect-marker"]), ports, CancellationToken.None);
        Assert.Equal(outcome, result.Outcome);
        Assert.True(File.Exists(Result));
    }

    [Fact]
    public async Task UnreadableChildResultIsRemovedWithoutHidingTheReadFailure()
    {
        File.WriteAllText(Result, "incomplete JSON");
        var ports = new BugReportProcessPorts(_ => 42, _ => false, _ => Assert.Fail("Unexpected stop."), _ => Task.CompletedTask, () => 0);
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => BugReportReplayProcess.RunAsync(new(Result, []), ports, CancellationToken.None));
        Assert.False(File.Exists(Result));
    }

    [Fact]
    public async Task DeadlineStopsTheChildAndCleansUp()
    {
        ulong time = 0;
        bool running = true;
        var ports = new BugReportProcessPorts(_ => 42, _ => running, _ => running = false,
            _ => { time = 30 * 60 * 1000; File.WriteAllText(Result, "partial"); return Task.CompletedTask; }, () => time);
        await Assert.ThrowsAsync<IOException>(() => BugReportReplayProcess.RunAsync(new(Result, []), ports, CancellationToken.None));
        Assert.False(running);
        Assert.False(File.Exists(Result));
    }

    [Fact]
    public async Task FailedChildCleanupRemainsAnExplicitFailureAndStillRemovesPartialResults()
    {
        using var cancellation = new CancellationTokenSource();
        ulong time = 0;
        var ports = new BugReportProcessPorts(_ => { File.WriteAllText(Result, "partial"); cancellation.Cancel(); return 42; },
            _ => true, _ => { }, _ => { time += 5000; return Task.CompletedTask; }, () => time);
        await Assert.ThrowsAsync<BugReportChildCleanupException>(() => BugReportReplayProcess.RunAsync(new(Result, []), ports, cancellation.Token));
        Assert.False(File.Exists(Result));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

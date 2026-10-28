using OBP.Game;

namespace OBP.Tests;

public sealed class BugReportUiWorkTests
{
    [Fact]
    public async Task ClosingAndReopeningCancelsViewWorkAndRejectsItsLateResult()
    {
        using var host = new HostOperationScope();
        using var view = new BugReportUiWork(host);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken retired = default;
        bool published = false;
        view.Open();
        view.Start("bug-export", token => { retired = token; return release.Task; }, _ => published = true, _ => Assert.Fail("Unexpected failure."));
        view.Close();
        view.Open();
        Assert.True(retired.IsCancellationRequested);
        Assert.False(host.Token.IsCancellationRequested);
        release.SetResult(1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (host.PendingCount > 0) await Task.Delay(1, deadline.Token);
        Assert.False(host.Stopping);
        Assert.False(published);
        Assert.Empty(host.TakeFailures());
    }

    [Fact]
    public async Task ClosingViewAndStoppingHostDrainsTheAdmittedSaveWithoutPublishingIntoAnotherView()
    {
        using var host = new HostOperationScope();
        using var view = new BugReportUiWork(host);
        var commit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool saved = false, published = false;
        view.Open();
        view.Start("bug-report-save", async _ => { await commit.Task; saved = true; return 1; },
            _ => published = true, _ => Assert.Fail("Unexpected failure."), committedSave: true);
        view.Close(); view.Open();
        var stopping = host.StopAsync();
        Assert.False(stopping.IsCompleted);
        commit.SetResult(1);
        await stopping;
        Assert.True(saved);
        Assert.False(published);
        Assert.Equal(0, host.PendingCount);
        Assert.Empty(host.TakeFailures());
    }

    [Fact]
    public async Task AFailedWriteDuringShutdownRequiresFailureExit()
    {
        using var host = new HostOperationScope();
        using var view = new BugReportUiWork(host);
        var commit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.Open();
        view.Start("bug-report-save", _ => commit.Task, _ => Assert.Fail("Unexpected result."),
            _ => Assert.Fail("Retired view received a callback."), committedSave: true);
        view.Dispose();
        var stopping = host.StopAsync();
        commit.SetException(new IOException("publication failed"));
        await stopping;
        var failure = Assert.Single(host.TakeFailures());
        Assert.Equal("bug-report-save", failure.Name);
        Assert.True(failure.RequiresFailureExit(hostStopping: true));
    }

    [Fact]
    public async Task AFailureAfterViewRetirementIsRetainedByTheHostWithoutTouchingTheReplacementView()
    {
        using var host = new HostOperationScope();
        using var view = new BugReportUiWork(host);
        var io = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.Open();
        view.Start("bug-export", _ => io.Task, _ => Assert.Fail("Unexpected result."), _ => Assert.Fail("Retired failure callback."));
        view.Close(); view.Open();
        io.SetException(new IOException("late archive failure"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (host.PendingCount > 0) await Task.Delay(1, deadline.Token);
        var failure = Assert.Single(host.TakeFailures());
        Assert.True(failure.Superseded);
        Assert.False(failure.RequiresFailureExit(hostStopping: false));
        Assert.True(failure.RequiresFailureExit(hostStopping: true));
    }

    [Fact]
    public async Task ActiveViewReceivesTheFailureAndCanRetry()
    {
        using var host = new HostOperationScope();
        using var view = new BugReportUiWork(host);
        var error = new IOException("disk full");
        Exception? shown = null;
        bool saved = false;
        view.Open();
        view.Start<int>("bug-report-save", _ => throw error, _ => Assert.Fail("Unexpected result."), failure => shown = failure, committedSave: true);
        view.Start("bug-report-save", _ => Task.FromResult(1), _ => saved = true, _ => Assert.Fail("Unexpected failure."), committedSave: true);
        Assert.Same(error, shown);
        Assert.True(saved);
        await host.StopAsync();
        Assert.Empty(host.TakeFailures());
    }
}

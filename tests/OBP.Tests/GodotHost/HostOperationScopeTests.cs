using OBP.Game;

namespace OBP.Tests;

public sealed class HostOperationScopeTests
{
    // Bound a broken drain while allowing heavily loaded test runners.
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromMinutes(2);
    [Fact]
    public async Task ShutdownCancelsAndDrainsEveryAdmittedJobOnce()
    {
        using var owner = new HostOperationScope();
        int cleanup = 0;
        for (int index = 0; index < 3; index++)
            owner.Start("pending", async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { Interlocked.Increment(ref cleanup); }
            });
        Assert.Equal(3, owner.PendingCount);
        var stopped = owner.StopAsync();
        Assert.Same(stopped, owner.StopAsync());
        await stopped.WaitAsync(DeadlockTimeout, CancellationToken.None);
        Assert.Equal(3, cleanup);
        Assert.Equal(0, owner.PendingCount);
        Assert.Empty(owner.TakeFailures());
        Assert.Throws<ObjectDisposedException>(() => owner.Start("late", _ => Task.CompletedTask));
    }

    [Fact]
    public async Task FailureBeforeAndAfterAwaitIsObservedRatherThanLost()
    {
        using var owner = new HostOperationScope();
        var synchronous = new IOException("synchronous failure");
        var asynchronous = new IOException("async failure");
        owner.Start("sync", _ => throw synchronous);
        owner.Start("async", async _ => { await Task.Yield(); throw asynchronous; });
        await owner.StopAsync().WaitAsync(DeadlockTimeout, CancellationToken.None);
        var failures = owner.TakeFailures();
        Assert.Equal(2, failures.Count);
        Assert.Same(synchronous, failures.Single(failure => failure.Name == "sync").Error);
        Assert.Same(asynchronous, failures.Single(failure => failure.Name == "async").Error);
        Assert.Empty(owner.TakeFailures());
    }

    [Fact]
    public async Task SupersededUiWorkCannotPublishButDoesNotCancelUnrelatedRootWork()
    {
        using var owner = new HostOperationScope();
        using var ui = new CancellationTokenSource();
        var io = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool published = false, rootCompleted = false;
        owner.Start("ui-import", async token => { await io.Task; token.ThrowIfCancellationRequested(); published = true; }, ui.Token);
        owner.Start("cross-world-report", async _ => { await io.Task; rootCompleted = true; });
        ui.Cancel();
        Assert.False(owner.Token.IsCancellationRequested);
        io.SetResult();
        await owner.StopAsync().WaitAsync(DeadlockTimeout, CancellationToken.None);
        Assert.False(published);
        Assert.True(rootCompleted);
        Assert.Empty(owner.TakeFailures());
    }

    [Fact]
    public async Task UnrelatedCancellationAndFaultAfterSupersessionRemainVisible()
    {
        using var owner = new HostOperationScope();
        using var ui = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Start("late-fault", async _ => { await fault.Task; throw new IOException("late IO fault"); }, ui.Token);
        owner.Start("wrong-token", _ => Task.FromCanceled(unrelated.Token));
        ui.Cancel(); fault.SetResult();
        await owner.StopAsync().WaitAsync(DeadlockTimeout, CancellationToken.None);
        var failures = owner.TakeFailures();
        Assert.Equal(2, failures.Count);
        Assert.True(failures.Single(failure => failure.Name == "late-fault").Superseded);
        Assert.False(failures.Single(failure => failure.Name == "late-fault").RequiresFailureExit(hostStopping: false));
        Assert.True(failures.Single(failure => failure.Name == "late-fault").RequiresFailureExit(hostStopping: true));
        Assert.IsAssignableFrom<OperationCanceledException>(failures.Single(failure => failure.Name == "wrong-token").Error);
    }

    [Fact]
    public async Task JobCanRequestShutdownBeforeItsFirstAwaitWithoutSelfDeadlock()
    {
        using var owner = new HostOperationScope();
        Task? shutdown = null;
        owner.Start("quit-request", _ => { shutdown = owner.StopAsync(); return Task.CompletedTask; });
        await shutdown!.WaitAsync(DeadlockTimeout, CancellationToken.None);
        Assert.Empty(owner.TakeFailures());
        Assert.Equal(0, owner.PendingCount);
    }

    [Fact]
    public async Task ThrowingCancellationCallbackIsReportedAndOtherJobsStillDrain()
    {
        using var owner = new HostOperationScope();
        owner.Start("bad-cancel-hook", async token =>
        {
            var pending = Task.Delay(Timeout.Infinite, token);
            using var registration = token.Register(() => throw new InvalidOperationException("hook failed"));
            await pending;
        });
        await owner.StopAsync().WaitAsync(DeadlockTimeout, CancellationToken.None);
        Assert.Equal(0, owner.PendingCount);
        Assert.IsType<AggregateException>(Assert.Single(owner.TakeFailures()).Error);
    }

    [Fact]
    public async Task FailedQuitWriteRemainsObservableAndDoesNotSkipTheFollowingFlush()
    {
        using var owner = new HostOperationScope();
        await owner.StopAsync().WaitAsync(DeadlockTimeout, CancellationToken.None);
        var error = new IOException("Injected report-save failure.");
        await owner.ObserveQuitWriteAsync("report-save", Task.FromException(error)).WaitAsync(DeadlockTimeout, CancellationToken.None);
        bool flushed = false;
        Task Flush() { flushed = true; return Task.CompletedTask; }
        await owner.ObserveQuitWriteAsync("recording-flush", Flush()).WaitAsync(DeadlockTimeout, CancellationToken.None);
        Assert.True(flushed);
        var failure = Assert.Single(owner.TakeFailures());
        Assert.Same(error, failure.Error);
        Assert.True(failure.RequiresFailureExit(owner.Stopping));
    }
}

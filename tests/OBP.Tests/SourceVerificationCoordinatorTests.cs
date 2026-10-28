using OBP.Core;
using OBP.Game;

namespace OBP.Tests;

public sealed class SourceVerificationCoordinatorTests
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromMinutes(2);
    [Fact]
    public async Task LateOptionalHashCannotPublishAfterReplacementOrCancelCriticalHostWork()
    {
        var stamp = new VerificationSourceStamp(ObpSourceGame.Rac2, "fixture", "synthetic-private-source", 12, 34);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var publications = new List<string>();
        using var critical = new HostOperationScope();
        using var optional = new SourceVerificationCoordinator(_ => stamp, (_, _, _) =>
        {
            entered.TrySetResult(); release.Wait(CancellationToken.None); return "old source verified";
        }, () => "fixture", (text, _) => publications.Add(text));
        optional.Start(ObpSourceGame.Rac2);
        try
        {
            await entered.Task.WaitAsync(DeadlockTimeout, CancellationToken.None);
            stamp = stamp with { Written = 35 };
            optional.Tick(0);
            Assert.Single(publications); Assert.Contains("canceled", publications[0], StringComparison.Ordinal);
            Task stop = optional.StopAsync(); Assert.False(stop.IsCompleted);
            Assert.False(critical.Stopping);
            bool ran = false; critical.Start("capture", _ => { ran = true; return Task.CompletedTask; }); Assert.True(ran);
            release.Set(); await stop.WaitAsync(DeadlockTimeout, CancellationToken.None);
            optional.Tick(1); Assert.Single(publications);
            Assert.Empty(critical.TakeFailures());
        }
        finally { release.Set(); await optional.StopAsync(); }
    }

    [Fact]
    public async Task OptionalHashFailureIsReportedOnceAndNeverBecomesACriticalFailure()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publications = new List<(string, bool)>();
        using var owner = new SourceVerificationCoordinator(_ => new(ObpSourceGame.Rac2, "fixture", "synthetic", 1, 1),
            (_, _, _) => { completed.TrySetResult(); throw new InvalidOperationException("fixture hash failure"); }, () => "fixture",
            (text, error) => publications.Add((text, error)));
        owner.Start(ObpSourceGame.Rac2);
        await completed.Task.WaitAsync(DeadlockTimeout, CancellationToken.None);
        using var deadline = new CancellationTokenSource(DeadlockTimeout);
        while (!publications.Any(value => value.Item2))
        {
            owner.Tick(0);
            await Task.Delay(1, deadline.Token);
        }
        owner.Tick(1);
        await owner.StopAsync();
        Assert.Single(publications, value => value.Item1.Contains("fixture hash failure", StringComparison.Ordinal) && value.Item2);
    }

    [Fact]
    public async Task UndrainedFailureFromAnOldJobCannotCancelOrReportAgainstItsReplacement()
    {
        int calls = 0;
        var publications = new List<string>();
        using var release = new ManualResetEventSlim();
        using var deadline = new CancellationTokenSource(DeadlockTimeout);
        using var owner = new SourceVerificationCoordinator(_ => new(ObpSourceGame.Rac2, "fixture", "synthetic", 1, 1),
            (_, _, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new IOException("old attachment failed");
                release.Wait(CancellationToken.None); return "replacement verified";
            }, () => "fixture", (text, _) => publications.Add(text));
        try
        {
            owner.Start(ObpSourceGame.Rac2);
            while (owner.PendingCount != 0) await Task.Delay(1, deadline.Token);
            owner.Start(ObpSourceGame.Rac2); owner.Tick(0);
            Assert.DoesNotContain(publications, text => text.Contains("old attachment", StringComparison.Ordinal));
            release.Set();
            while (owner.PendingCount != 0) await Task.Delay(1, deadline.Token);
            owner.Tick(1);
            Assert.Contains(publications, text => text.Contains("replacement verified", StringComparison.Ordinal));
        }
        finally { release.Set(); await owner.StopAsync(); }
    }
}

using OBP.Core;
using OBP.Game;

namespace OBP.Tests;

public sealed class SourceVerificationCoordinatorTests
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromMinutes(2);
    [Fact]
    public async Task LateOptionalHashCannotPublishAfterReplacement()
    {
        var stamp = new VerificationSourceStamp(ObpSourceGame.Rac2, "fixture", "synthetic-private-source", 12, 34);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var publications = new List<string>();
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
            release.Set(); await stop.WaitAsync(DeadlockTimeout, CancellationToken.None);
            optional.Tick(1); Assert.Single(publications);
        }
        finally { release.Set(); await optional.StopAsync(); }
    }

    [Fact]
    public async Task OptionalHashFailureIsReportedOnce()
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

    [Fact]
    public async Task ProgressPublishesOnlyWhenTheMessageChangesAndResetsForANewJob()
    {
        var source = new VerificationSourceStamp(ObpSourceGame.Rac2, "fixture", "synthetic", 1, 1);
        var entered = new TaskCompletionSource<IProgress<double>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        string summary = "first world";
        var publications = new List<string>();
        using var owner = new SourceVerificationCoordinator(_ => source, (_, progress, _) =>
        {
            entered.TrySetResult(progress);
            release.Wait(CancellationToken.None);
            return "verified";
        }, () => summary, (text, _) => publications.Add(text));
        try
        {
            owner.Start(ObpSourceGame.Rac2);
            var progress = await entered.Task.WaitAsync(DeadlockTimeout, CancellationToken.None);
            owner.Tick(0); owner.Tick(1);
            Assert.Single(publications);
            progress.Report(.003); owner.Tick(2);
            Assert.Single(publications);
            progress.Report(.01); owner.Tick(3);
            Assert.Equal(2, publications.Count);
            summary = "second world"; owner.Tick(4); owner.Tick(5);
            Assert.Equal(3, publications.Count);
            Assert.Contains("second world", publications[^1], StringComparison.Ordinal);
            owner.Start(ObpSourceGame.Rac2); owner.Tick(6);
            Assert.Equal(4, publications.Count);
            Assert.Contains("(0%)", publications[^1], StringComparison.Ordinal);
        }
        finally { release.Set(); await owner.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerificationDistinguishesUnreadableFilesFromInvalidContent(bool invalidContent)
    {
        string path = Path.Combine(Path.GetTempPath(), "obp-verification-" + Guid.NewGuid().ToString("N") + ".iso");
        try
        {
            if (invalidContent) File.WriteAllBytes(path, new byte[65536]);
            string outcome = SourceVerificationFiles.VerifyRac2(new(ObpSourceGame.Rac2, "fixture", path, 65536, 0),
                new Progress<double>(), CancellationToken.None);
            Assert.StartsWith(invalidContent ? "⚠ disc does not match" : "⚠ could not read the disc image", outcome);
        }
        finally { File.Delete(path); }
    }
}

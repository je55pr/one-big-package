using OBP.Core;
using OBP.Game;

namespace OBP.Tests;

public sealed class SourceVerificationOperationTests
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromMinutes(2);
    private static VerificationSourceStamp Source => new(ObpSourceGame.Rac2, "fixture-v1", "synthetic-source", 42, 123);

    [Fact]
    public async Task RetiredAttachmentCannotPublishItsLateVerificationIntoTheReplacement()
    {
        using var jobs = new HostOperationScope();
        using var old = new SourceVerificationOperation(Source);
        using var replacement = new SourceVerificationOperation(Source with { Written = 124 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        jobs.Start("verify", async token =>
        {
            try
            {
                await old.RunAsync((progress, _) =>
                {
                    progress.Report(.5); entered.SetResult();
                    release.Wait(CancellationToken.None); // Deliberately finish late, like synchronous IO that cannot be interrupted.
                    return "old attachment verified";
                }, token);
            }
            finally { finished.TrySetResult(); }
        }, old.Retirement);
        try
        {
            await entered.Task.WaitAsync(DeadlockTimeout, CancellationToken.None);
            old.Dispose();
            release.Set();
            await finished.Task.WaitAsync(DeadlockTimeout, CancellationToken.None);
        }
        finally { release.Set(); }
        await jobs.StopAsync().WaitAsync(DeadlockTimeout, CancellationToken.None);
        Assert.Null(old.TakeOutcome());
        Assert.Null(replacement.TakeOutcome());
        Assert.Equal(0, replacement.Progress);
        Assert.Empty(jobs.TakeFailures());
    }

    [Fact]
    public async Task CurrentAttachmentDeliversProgressAndOneOutcome()
    {
        using var operation = new SourceVerificationOperation(Source);
        await operation.RunAsync((progress, _) => { progress.Report(1); return "verified"; }, CancellationToken.None);
        Assert.True(operation.Matches(Source));
        Assert.Equal(1, operation.Progress);
        Assert.Equal("verified", operation.TakeOutcome());
        Assert.Null(operation.TakeOutcome());
    }

    [Fact]
    public void AttachmentBuildPathLengthAndWriteStampAllParticipateInAdmission()
    {
        using var operation = new SourceVerificationOperation(Source);
        Assert.True(operation.Matches(Source));
        Assert.False(operation.Matches(null));
        Assert.False(operation.Matches(Source with { Game = ObpSourceGame.Rac3 }));
        Assert.False(operation.Matches(Source with { Build = "other" }));
        Assert.False(operation.Matches(Source with { Path = "replacement" }));
        Assert.False(operation.Matches(Source with { Length = 43 }));
        Assert.False(operation.Matches(Source with { Written = 124 }));
        operation.Dispose(); operation.Dispose();
        Assert.False(operation.Matches(Source));
    }

    [Fact]
    public async Task PollingBoundsMetadataReadsButAlwaysRevalidatesAnOutcome()
    {
        using var operation = new SourceVerificationOperation(Source);
        int reads = 0;
        var current = Source;
        VerificationSourceStamp Read() { reads++; return current; }
        operation.Poll(0, Read);
        operation.Poll(1, Read);
        operation.Poll(249, Read);
        Assert.Equal(1, reads);
        operation.Poll(250, Read);
        Assert.Equal(2, reads);
        await operation.RunAsync((_, _) => "verified", CancellationToken.None);
        current = Source with { Written = 124 };
        var result = operation.Poll(251, Read);
        Assert.Equal(3, reads);
        Assert.True(result.SourceChanged);
        Assert.Null(result.Outcome);
    }
}

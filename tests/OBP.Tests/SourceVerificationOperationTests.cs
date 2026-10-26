using OBP.Core;
using OBP.Game;

namespace OBP.Tests;

public sealed class SourceVerificationOperationTests
{
    private static VerificationSourceStamp Source => new(ObpSourceGame.Rac2, "fixture-v1", "synthetic-source", 42, 123);

    [Fact]
    public async Task RetiredAttachmentCannotPublishItsLateVerificationIntoTheReplacement()
    {
        using var jobs = new HostOperationScope();
        using var old = new SourceVerificationOperation(Source);
        using var replacement = new SourceVerificationOperation(Source with { Written = 124 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        jobs.Start("verify", token => old.RunAsync((progress, _) =>
        {
            progress.Report(.5); entered.SetResult(); release.Wait(); return "old attachment verified";
        }, token), old.Retirement);
        await entered.Task;
        old.Dispose();
        release.Set();
        await jobs.StopAsync();
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
}

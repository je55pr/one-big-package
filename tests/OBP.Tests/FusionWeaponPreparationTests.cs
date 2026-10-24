using OneBigPackage;

namespace OBP.Tests;

public sealed class FusionWeaponPreparationTests
{
    [Fact]
    public async Task SlowPreparationDoesNotBlockCallerAndReportsProgress()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var preparation = new FusionWeaponPreparation((progress, token) =>
        {
            progress.Report("Verifying source… 25%");
            entered.SetResult();
            release.Wait(token);
            return new FusionWeaponData([]);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(preparation.Completion.IsCompleted);
            Assert.Equal("Verifying source… 25%", preparation.Status);
        }
        finally { release.Set(); }
        var result = await preparation.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(FusionWeaponSession.DefaultLoadoutId, result.Configuration(isolated: false).LoadoutId);
        Assert.Equal(FusionWeaponSession.LoadoutId, result.Configuration(isolated: true).LoadoutId);
        Assert.Equal(3, result.Problems.Count);
    }

    [Fact]
    public async Task RetiringPreparationCancelsPendingWorkWithoutWaitingForIt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var preparation = new FusionWeaponPreparation((_, token) =>
        {
            entered.SetResult();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Canceled preparation must not produce assets.");
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        preparation.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await preparation.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}

using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignSaveBatcherFailureTests
{
    [Fact]
    public void InFlightFailureWithNewerSnapshotRetriesOnIdleFrames()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int attempts = 0;
        int savedBolts = -1;
        var batcher = new Rac1CampaignSaveBatcher(snapshot =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Test never released first write");
                throw new IOException("First write failed");
            }
            Interlocked.Exchange(ref savedBolts, snapshot.Bolts);
        });
        var campaign = new Rac1CampaignState();
        var weapons = Rac1WeaponInventory.CreateOpeningLoadout();
        Rac1CampaignSaveEnvelope Capture(int bolts) =>
            Rac1CampaignSavePolicy.Capture(campaign, weapons, new Rac1BoltWallet(bolts));

        try
        {
            batcher.MarkDirty();
            Assert.True(batcher.Tick(1, () => Capture(1)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            batcher.MarkDirty();
            Assert.True(batcher.Tick(1, () => Capture(2)));
            Assert.False(batcher.Dirty);

            release.Set();
            Assert.True(SpinWait.SpinUntil(() => batcher.Failure is IOException, 15000));
            Assert.Equal(1, Volatile.Read(ref attempts));

            // No further mutations or explicit FlushNow. Idle gameplay frames
            // must restart the failed writer and persist the newest snapshot.
            for (int i = 0; i < 40; i++)
                batcher.Tick(.25, () => Capture(99));

            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref savedBolts) == 2, 15000));
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.False(batcher.Dirty);
            Assert.Null(batcher.Failure);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void FailedWriteIsNotRetriedOnEveryFrame()
    {
        int attempts = 0;
        var batcher = new Rac1CampaignSaveBatcher(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new IOException("disk unavailable");
        });
        var state = new Rac1CampaignState();
        var weapons = Rac1WeaponInventory.CreateOpeningLoadout();
        Rac1CampaignSaveEnvelope Capture() => Rac1CampaignSavePolicy.Capture(state, weapons);
        batcher.MarkDirty();
        batcher.Tick(1, Capture);
        Assert.True(SpinWait.SpinUntil(() => batcher.Failure is IOException, 15000));
        Assert.Equal(1, Volatile.Read(ref attempts));

        for (int i = 0; i < 10; i++) batcher.Tick(.01, Capture);
        Assert.Equal(1, Volatile.Read(ref attempts));
        batcher.Tick(1, Capture);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref attempts) == 2, 15000));
    }
}

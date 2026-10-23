using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignSaveBatcherTests
{
    private static Rac1CampaignSaveEnvelope Snapshot() =>
        Rac1CampaignSavePolicy.Capture(
            new Rac1CampaignState(), Rac1WeaponInventory.CreateOpeningLoadout());

    [Fact]
    public void BurstOfMutationsProducesOneBackgroundWriteAfterQuietPeriod()
    {
        int writes = 0;
        var batcher = new Rac1CampaignSaveBatcher(_ => Interlocked.Increment(ref writes));
        for (int i = 0; i < 100; i++)
        {
            batcher.MarkDirty();
            Assert.False(batcher.Tick(.01, Snapshot));
        }

        Assert.Equal(0, Volatile.Read(ref writes));
        Assert.True(batcher.Tick(Rac1CampaignSaveBatcher.QuietSeconds, Snapshot));
        Assert.True(batcher.FlushNow(Snapshot, out _));
        Assert.InRange(Volatile.Read(ref writes), 1, 2);
        Assert.False(batcher.Dirty);
        Assert.Null(batcher.Failure);
    }

    [Fact]
    public void ContinuousMutationsCannotPostponeSaveForever()
    {
        int writes = 0;
        var batcher = new Rac1CampaignSaveBatcher(_ => Interlocked.Increment(ref writes));
        bool queued = false;
        for (int i = 0; i < 35; i++)
        {
            batcher.MarkDirty();
            queued |= batcher.Tick(.1, Snapshot);
        }

        Assert.True(queued);
        Assert.True(batcher.FlushNow(Snapshot, out _));
        Assert.True(Volatile.Read(ref writes) >= 1);
    }

    [Fact]
    public void DurableFlushOrdersOutstandingWriterBeforeLatestSnapshot()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new List<int>();
        var batcher = new Rac1CampaignSaveBatcher(snapshot =>
        {
            lock (written) written.Add(snapshot.Bolts);
            if (snapshot.Bolts == 1)
            {
                entered.Set();
                Assert.True(release.Wait(5000));
            }
        });

        var campaign = new Rac1CampaignState();
        var weapons = Rac1WeaponInventory.CreateOpeningLoadout();
        Rac1CampaignSaveEnvelope Capture(int bolts) =>
            Rac1CampaignSavePolicy.Capture(campaign, weapons, new Rac1BoltWallet(bolts));
        batcher.MarkDirty();
        batcher.Tick(1, () => Capture(1));
        Assert.True(entered.Wait(5000));

        batcher.MarkDirty();
        release.Set();
        Assert.True(batcher.FlushNow(() => Capture(2), out Exception? error));
        Assert.Null(error);
        Assert.Equal(new[] { 1, 2 }, written);
    }

    [Fact]
    public void FailedBackgroundWriteSurfacesErrorAndExitCanRetryLatestState()
    {
        int attempts = 0;
        int savedBolts = -1;
        var batcher = new Rac1CampaignSaveBatcher(snapshot =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new IOException("test write failure");
            savedBolts = snapshot.Bolts;
        });
        var campaign = new Rac1CampaignState();
        var weapons = Rac1WeaponInventory.CreateOpeningLoadout();
        Rac1CampaignSaveEnvelope Capture(int bolts) =>
            Rac1CampaignSavePolicy.Capture(campaign, weapons, new Rac1BoltWallet(bolts));

        batcher.MarkDirty();
        Assert.True(batcher.Tick(1, () => Capture(3)));
        Assert.True(SpinWait.SpinUntil(() => batcher.Failure is IOException, 5000));
        Assert.True(batcher.FlushNow(() => Capture(4), out Exception? error));
        Assert.Null(error);
        Assert.Equal(4, savedBolts);
        Assert.Null(batcher.Failure);
    }

    [Fact]
    public void InvalidFrameDeltaDoesNotAdvanceDirtyClock()
    {
        var batcher = new Rac1CampaignSaveBatcher(_ => { });
        batcher.MarkDirty();
        Assert.Throws<ArgumentOutOfRangeException>(() => batcher.Tick(double.NaN, Snapshot));
        Assert.True(batcher.Dirty);
    }
}

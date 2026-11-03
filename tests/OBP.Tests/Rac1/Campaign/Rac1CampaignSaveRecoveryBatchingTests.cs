using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignSaveRecoveryBatchingTests
{
    [Fact]
    public void DamagedSaveCannotBeOverwrittenByAQueuedProgressMutation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "obp-recovery-batcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rac1-campaign.json");
        byte[] original = "{ this save is broken"u8.ToArray();
        try
        {
            File.WriteAllBytes(path, original);
            var recovery = Rac1CampaignSessionPersistence.Persistent(path).RestoreWithRecovery();
            Assert.False(recovery.Persistence.Enabled);
            Assert.NotNull(recovery.Warning);

            Rac1CampaignSaveBatcher? writer = Rac1CampaignSaveWriterFactory.CreateIfEnabled(recovery.Persistence);
            var state = recovery.Restored.Campaign;
            state.ApplyProgressionEvent(0x25);
            Rac1CampaignSaveEnvelope snapshot = Rac1CampaignSavePolicy.Capture(
                state, Rac1WeaponInventory.CreateOpeningLoadout());

            // A queued mutation must never bypass the disabled persistence state.
            if (writer is not null)
            {
                writer.MarkDirty();
                writer.Tick(5, () => snapshot);
                writer.FlushNow(() => snapshot, out _);
            }

            Assert.Null(writer);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingSaveStillGetsAWriterAfterSuccessfulRecovery()
    {
        string path = Path.Combine(Path.GetTempPath(), "obp-not-created-" + Guid.NewGuid().ToString("N"), "save.json");
        var recovery = Rac1CampaignSessionPersistence.Persistent(path).RestoreWithRecovery();
        Assert.True(recovery.Persistence.Enabled);
        Assert.Null(recovery.Warning);
        Assert.NotNull(Rac1CampaignSaveWriterFactory.CreateIfEnabled(recovery.Persistence));
        Assert.False(File.Exists(path));
    }
}

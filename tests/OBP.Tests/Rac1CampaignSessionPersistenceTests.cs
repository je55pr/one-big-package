using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignSessionPersistenceTests
{
    [Fact]
    public void EphemeralSessionIgnoresStaleHostSaveAndNeverWritesIt()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "campaign.json");
            SeedStaleSave(path);

            string staleJson = File.ReadAllText(path);
            var persistence = Rac1CampaignSessionPersistence.Ephemeral();
            Rac1CampaignRestoreResult restored = persistence.Restore();

            Assert.False(persistence.Enabled);
            Assert.Null(persistence.SavePath);
            Assert.Equal(Rac1CampaignRestoreKind.DefaultedMissingState, restored.Kind);
            Assert.Equal(0, restored.Campaign.CurrentLevel);
            Assert.Equal(0, restored.Campaign.AdmittedDestinationCount);
            Assert.True(restored.Weapons.Owns(Rac1WeaponId.Wrench));
            Assert.Equal(Rac1WeaponId.FirstRanged, restored.Weapons.Equipped);
            Assert.True(restored.Weapons.OwnsFirstRanged);
            Assert.Equal(20, restored.Weapons.FirstRangedAmmo);
            Assert.Equal(
                Rac1DestinationDiscoveryResult.Discovered,
                restored.Campaign.ApplyProgressionEvent(0x25));
            Assert.True(restored.Weapons.TryEquip(Rac1WeaponId.FirstRanged));
            Assert.True(restored.Weapons.TryUseEquipped());

            Assert.False(persistence.PersistIfEnabled(restored.Campaign, restored.Weapons));
            Assert.Equal(staleJson, File.ReadAllText(path));

            Rac1CampaignRestoreResult stale = Rac1CampaignSaveFile.LoadOrDefault(path);
            Assert.Equal(1, stale.Campaign.CurrentLevel);
            Assert.Equal(5, stale.Weapons.FirstRangedAmmo);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PersistentSessionLoadsAndWritesOnlyWhenExplicitlyConstructed()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "campaign.json");
            SeedStaleSave(path);

            var persistence = Rac1CampaignSessionPersistence.Persistent(path);
            Rac1CampaignRestoreResult restored = persistence.Restore();

            Assert.True(persistence.Enabled);
            Assert.Equal(Path.GetFullPath(path), persistence.SavePath);
            Assert.Equal(Rac1CampaignRestoreKind.RestoredCurrentSchema, restored.Kind);
            Assert.Equal(1, restored.Campaign.CurrentLevel);
            Assert.Equal(5, restored.Weapons.FirstRangedAmmo);

            Assert.True(restored.Weapons.TryEquip(Rac1WeaponId.FirstRanged));
            Assert.True(restored.Weapons.TryUseEquipped());
            Assert.True(persistence.PersistIfEnabled(restored.Campaign, restored.Weapons));

            Rac1CampaignRestoreResult reloaded = Rac1CampaignSaveFile.LoadOrDefault(path);
            Assert.Equal(1, reloaded.Campaign.CurrentLevel);
            Assert.Equal(4, reloaded.Weapons.FirstRangedAmmo);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingSaveKeepsPersistenceEnabled()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "missing.json");
            var attempt = Rac1CampaignSessionPersistence.Persistent(path).RestoreWithRecovery();
            Assert.True(attempt.Persistence.Enabled);
            Assert.Null(attempt.Warning);
            Assert.Equal(Rac1CampaignRestoreKind.DefaultedMissingState, attempt.Restored.Kind);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOrUnsupportedSaveIsPreservedAndFallsBackToEphemeral(bool unsupported)
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "campaign.json");
            if (unsupported)
            {
                SeedStaleSave(path);
                string valid = File.ReadAllText(path);
                File.WriteAllText(path, valid.Replace(
                    "\"SchemaVersion\": 2", "\"SchemaVersion\": 900", StringComparison.Ordinal));
            }
            else
            {
                File.WriteAllText(path, "{ invalid JSON");
            }

            string damagedContents = File.ReadAllText(path);
            var attempt = Rac1CampaignSessionPersistence.Persistent(path).RestoreWithRecovery();

            Assert.False(attempt.Persistence.Enabled);
            Assert.NotNull(attempt.Warning);
            Assert.Contains("original file is unchanged", attempt.Warning);
            Assert.Equal(Rac1CampaignRestoreKind.DefaultedMissingState, attempt.Restored.Kind);
            Assert.Equal(0, attempt.Restored.Campaign.CurrentLevel);
            Assert.False(attempt.Persistence.PersistIfEnabled(attempt.Restored.Campaign, attempt.Restored.Weapons));
            Assert.Equal(damagedContents, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnreadableSavePathRecoversWithoutOverwriting()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            // An existing directory cannot be read as the requested save file.
            // This exercises a repeatable IO/permission failure on Windows and Unix.
            var attempt = Rac1CampaignSessionPersistence.Persistent(directory).RestoreWithRecovery();
            Assert.False(attempt.Persistence.Enabled);
            Assert.NotNull(attempt.Warning);
            Assert.True(Directory.Exists(directory));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void SeedStaleSave(string path)
    {
        var campaign = new Rac1CampaignState();
        Assert.Equal(
            Rac1DestinationDiscoveryResult.Discovered,
            campaign.ApplyProgressionEvent(0x25));
        Assert.True(campaign.BeginTravel(1));

        Rac1WeaponInventory weapons = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        Assert.True(weapons.TryEquip(Rac1WeaponId.FirstRanged));
        Assert.True(weapons.TryUseEquipped());
        Rac1CampaignSaveFile.Save(path, campaign, weapons);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "obp-rac1-campaign-session-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

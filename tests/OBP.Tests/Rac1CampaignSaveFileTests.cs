using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignSaveFileTests
{
    [Fact]
    public void MissingFileDefaultsWithoutCreatingHostState()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "campaign.json");

            Rac1CampaignRestoreResult restored = Rac1CampaignSaveFile.LoadOrDefault(path);

            Assert.Equal(Rac1CampaignRestoreKind.DefaultedMissingState, restored.Kind);
            Assert.Equal(0, restored.Campaign.CurrentLevel);
            Assert.Equal(0, restored.Campaign.AdmittedDestinationCount);
            Assert.True(restored.Weapons.OwnsFirstRanged);
            Assert.Equal(6, restored.Weapons.FirstRangedAmmo);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MultiPlanetStateRoundTripsThroughObpHostFile()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "campaign.json");
            var campaign = new Rac1CampaignState();
            Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, campaign.ApplyProgressionEvent(0x25));
            Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, campaign.ApplyProgressionEvent(0x26));
            Assert.True(campaign.BeginTravel(1));
            Assert.True(campaign.BeginTravel(2));
            Assert.True(campaign.BeginTravel(1));
            var weapons = Rac1WeaponInventory.CreateOpeningVeldinWitness();
            Assert.True(weapons.TryEquip(Rac1WeaponId.FirstRanged));
            Assert.True(weapons.TryUseEquipped());
            Assert.True(weapons.TryUseEquipped());

            Rac1CampaignSaveFile.Save(path, campaign, weapons);
            Rac1CampaignRestoreResult restored = Rac1CampaignSaveFile.LoadOrDefault(path);

            Assert.Equal(Rac1CampaignRestoreKind.RestoredCurrentSchema, restored.Kind);
            Assert.Equal(1, restored.Campaign.CurrentLevel);
            Assert.Equal(2, restored.Campaign.AdmittedDestinationCount);
            Assert.Equal(new[] { 1, 2 }, restored.Campaign.GalacticMap.Take(2));
            Assert.Equal(Rac1LevelVisitState.Visited, restored.Campaign.GetLevelState(1));
            Assert.Equal(Rac1LevelVisitState.Visited, restored.Campaign.GetLevelState(2));
            Assert.Equal(4, restored.Weapons.FirstRangedAmmo);
            Assert.True(restored.Weapons.OwnsFirstRanged);
            Assert.Equal(Rac1WeaponId.Wrench, restored.Weapons.Equipped);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "obp-rac1-campaign-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignSavePolicyTests
{
    [Fact]
    public void MissingCampaignPayloadDefaultsToRecoveredOpeningState()
    {
        Rac1CampaignRestoreResult restored =
            Rac1CampaignSavePolicy.RestoreOrDefault(null);

        Assert.Equal(
            Rac1CampaignRestoreKind.DefaultedMissingState,
            restored.Kind);
        Assert.Equal(0, restored.Campaign.CurrentLevel);
        Assert.Equal(0, restored.Campaign.AdmittedDestinationCount);
        Assert.Equal(
            Rac1LevelVisitState.Visited,
            restored.Campaign.GetLevelState(0));
        Assert.All(restored.Campaign.VisitedPlanets, value => Assert.Equal(0, value));
        Assert.All(restored.Campaign.GalacticMap, value => Assert.Equal(0, value));
    }

    [Fact]
    public void LegacyUnversionedSnapshotMigratesWithoutChangingCampaignMeaning()
    {
        var campaign = new Rac1CampaignState();
        Assert.True(campaign.AdmitDestination(3));
        Assert.True(campaign.AdmitDestination(1));
        Assert.True(campaign.BeginTravel(3));
        Assert.True(campaign.MarkLevelComplete(3));

        Rac1CampaignPersistentState legacy = campaign.CapturePersistentState();
        Rac1CampaignSaveEnvelope migrated =
            Rac1CampaignSavePolicy.MigrateLegacyUnversioned(legacy);
        Rac1CampaignRestoreResult restored =
            Rac1CampaignSavePolicy.RestoreLegacyUnversioned(legacy);

        Assert.Equal(Rac1CampaignSavePolicy.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Same(legacy, migrated.Campaign);
        Assert.Equal(
            Rac1CampaignRestoreKind.MigratedLegacyUnversioned,
            restored.Kind);
        Assert.Equal(campaign.CurrentLevel, restored.Campaign.CurrentLevel);
        Assert.Equal(campaign.VisitedPlanets, restored.Campaign.VisitedPlanets);
        Assert.Equal(campaign.GalacticMap, restored.Campaign.GalacticMap);
        Assert.Equal(campaign.LevelStates, restored.Campaign.LevelStates);
    }

    [Fact]
    public void UnlockOrderAndRevisitTravelSurviveVersionedRoundTrip()
    {
        var campaign = new Rac1CampaignState();
        Assert.Equal(
            Rac1DestinationDiscoveryResult.Discovered,
            campaign.ApplyProgressionEvent(0x27));
        Assert.Equal(
            Rac1DestinationDiscoveryResult.Discovered,
            campaign.ApplyProgressionEvent(0x25));
        Assert.True(campaign.BeginTravel(3));

        Rac1CampaignSaveEnvelope save = Rac1CampaignSavePolicy.Capture(campaign);
        Rac1CampaignRestoreResult restored =
            Rac1CampaignSavePolicy.RestoreOrDefault(save);
        var travel = new Rac1PlanetTravelSession(restored.Campaign);

        Assert.Equal(
            Rac1CampaignRestoreKind.RestoredCurrentSchema,
            restored.Kind);
        Assert.Equal(new[] { 3, 1 }, restored.Campaign.GalacticMap.Take(2));
        Assert.True(travel.TrySelectDestination(1));
        Assert.Equal(
            Rac1PlanetTravelStartResult.Started,
            travel.BeginSelectedTravel());
        Assert.Equal(3, restored.Campaign.CurrentLevel);
        travel.CommitLevelEntry();
        travel.FinishLevelEntry();

        Assert.Equal(1, restored.Campaign.CurrentLevel);
        Assert.Equal(new[] { 3, 1 }, restored.Campaign.GalacticMap.Take(2));
        Assert.Equal(2, restored.Campaign.AdmittedDestinationCount);

        travel.OpenPlanetMap();
        Assert.True(travel.TrySelectDestination(3));
        Assert.Equal(
            Rac1PlanetTravelStartResult.Started,
            travel.BeginSelectedTravel());
        travel.CommitLevelEntry();
        travel.FinishLevelEntry();

        Assert.Equal(3, restored.Campaign.CurrentLevel);
        Assert.Equal(new[] { 3, 1 }, restored.Campaign.GalacticMap.Take(2));
        Assert.Equal(2, restored.Campaign.AdmittedDestinationCount);
        Assert.Equal(1, restored.Campaign.VisitedPlanets[1]);
        Assert.Equal(1, restored.Campaign.VisitedPlanets[3]);
    }

    [Fact]
    public void UnknownSchemaFailsClosedInsteadOfGuessingMigration()
    {
        var campaign = new Rac1CampaignState();
        var save = new Rac1CampaignSaveEnvelope(
            Rac1CampaignSavePolicy.CurrentSchemaVersion + 1,
            campaign.CapturePersistentState());

        Assert.Throws<NotSupportedException>(
            () => Rac1CampaignSavePolicy.RestoreOrDefault(save));
    }
}

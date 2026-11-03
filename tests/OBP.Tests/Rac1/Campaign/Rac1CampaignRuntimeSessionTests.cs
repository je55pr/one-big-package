using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignRuntimeSessionTests
{
    [Fact]
    public void PlanetMapSnapshotUsesRecoveredAdmissionOrderAndKeepsCurrentSeparate()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(3);
        campaign.AdmitDestination(1);
        var session = CreateSession(campaign);

        Rac1PlanetMapSnapshot map = session.Travel.OpenPlanetMap();

        Assert.Equal(0, map.CurrentLevel);
        Assert.Equal(0, map.SelectedDestination);
        Assert.Equal(new[] { 3, 1 }, map.UnlockedDestinations);
        Assert.Equal(0, campaign.CurrentLevel);
        Assert.False(session.Travel.TransitionActive);
    }

    [Fact]
    public void OrdinaryTravelPreservesCampaignAndWeaponInventoryAcrossLateEntry()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        var weapons = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            equipped: Rac1WeaponId.FirstRanged,
            firstRangedAmmo: 3);
        var session = new Rac1CampaignRuntimeSession(campaign, weapons);

        Assert.True(weapons.TryUseEquipped());
        Assert.Equal(2, weapons.FirstRangedAmmo);
        var mapBefore = campaign.GalacticMap.ToArray();
        var discoveredBefore = campaign.VisitedPlanets.ToArray();

        Assert.Equal(
            Rac1PlanetTravelStartResult.Started,
            session.BeginTravel(1));
        Assert.Equal(0, campaign.CurrentLevel);
        Assert.Equal(1, session.Travel.LoaderDestination);
        Assert.Equal(
            Rac1LevelEntryKind.CampaignTravel,
            session.CommitLoadedLevel(1));

        Assert.Equal(1, campaign.CurrentLevel);
        Assert.Equal(Rac1LevelVisitState.Visited, campaign.GetLevelState(1));
        Assert.Equal(mapBefore, campaign.GalacticMap);
        Assert.Equal(discoveredBefore, campaign.VisitedPlanets);
        Assert.Same(weapons, session.Weapons);
        Assert.Equal(Rac1WeaponId.FirstRanged, session.Weapons.Equipped);
        Assert.Equal(2, session.Weapons.FirstRangedAmmo);
        Assert.True(session.Travel.TransitionActive);

        session.FinishLoadedLevel(Rac1LevelEntryKind.CampaignTravel);

        Assert.False(session.Travel.TransitionActive);
        Assert.Equal(1, campaign.CurrentLevel);
        Assert.Same(weapons, session.Weapons);
    }

    [Fact]
    public void ExternalHostLoadDoesNotInventCampaignTravelOrDiscovery()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        var persistent = campaign.CapturePersistentState();
        var session = CreateSession(campaign);

        Assert.Equal(
            Rac1LevelEntryKind.ExternalHostLoad,
            session.CommitLoadedLevel(7));
        session.FinishLoadedLevel(Rac1LevelEntryKind.ExternalHostLoad);

        Rac1CampaignPersistentState after = campaign.CapturePersistentState();
        Assert.Equal(persistent.CurrentLevel, after.CurrentLevel);
        Assert.Equal(persistent.VisitedPlanets.ToArray(), after.VisitedPlanets.ToArray());
        Assert.Equal(persistent.GalacticMap.ToArray(), after.GalacticMap.ToArray());
        Assert.Equal(persistent.LevelStates.ToArray(), after.LevelStates.ToArray());
        Assert.False(session.Travel.TransitionActive);
    }

    [Fact]
    public void MismatchedLoaderTargetCannotCommitCampaignCurrentLevel()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        campaign.AdmitDestination(2);
        var session = CreateSession(campaign);

        Assert.Equal(
            Rac1PlanetTravelStartResult.Started,
            session.BeginTravel(1));

        Assert.Throws<InvalidOperationException>(
            () => session.CommitLoadedLevel(2));
        Assert.Equal(0, campaign.CurrentLevel);
        Assert.True(session.Travel.TransitionActive);
        Assert.False(session.Travel.CurrentLevelCommitted);
    }

    [Fact]
    public void FailedHostLoadCanAbandonOnlyUncommittedTransfer()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        var weapons = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            equipped: Rac1WeaponId.FirstRanged,
            firstRangedAmmo: 2);
        var session = new Rac1CampaignRuntimeSession(campaign, weapons);

        Assert.Equal(
            Rac1PlanetTravelStartResult.Started,
            session.BeginTravel(1));
        session.AbandonUncommittedHostLoad();

        Assert.Equal(0, campaign.CurrentLevel);
        Assert.False(session.Travel.TransitionActive);
        Assert.Equal(0, session.Travel.SelectedDestination);
        Assert.Same(weapons, session.Weapons);
        Assert.Equal(2, session.Weapons.FirstRangedAmmo);

        Assert.Equal(
            Rac1PlanetTravelStartResult.Started,
            session.BeginTravel(1));
        session.CommitLoadedLevel(1);
        Assert.Throws<InvalidOperationException>(
            () => session.AbandonUncommittedHostLoad());
    }

    private static Rac1CampaignRuntimeSession CreateSession(
        Rac1CampaignState campaign) =>
        new(
            campaign,
            new Rac1WeaponInventory(
                ownsFirstRanged: true,
                firstRangedAmmo: 6));
}

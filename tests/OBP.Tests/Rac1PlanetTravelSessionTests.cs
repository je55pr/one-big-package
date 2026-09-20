using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1PlanetTravelSessionTests
{
    [Fact]
    public void PlanetMapSelectionStartsFromCurrentLevel()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        Assert.True(campaign.BeginTravel(1));

        var session = new Rac1PlanetTravelSession(campaign);

        Assert.Equal(1, session.SelectedDestination);
        Assert.False(session.TransitionActive);
        Assert.Null(session.ActiveTarget);
        Assert.Null(session.PendingDestinationStorage);
        Assert.Null(session.LoaderDestination);

        campaign.AdmitDestination(2);
        Assert.True(session.TrySelectDestination(2));
        session.OpenPlanetMap();
        Assert.Equal(1, session.SelectedDestination);
    }

    [Fact]
    public void TravelKeepsSourceCurrentUntilLateCommitThenClearsOnlyActiveMeaning()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        campaign.AdmitDestination(2);
        Assert.True(campaign.BeginTravel(1));
        var mapBefore = campaign.GalacticMap.ToArray();
        var visitedBefore = campaign.VisitedPlanets.ToArray();
        var session = new Rac1PlanetTravelSession(campaign);

        Assert.True(session.TrySelectDestination(2));
        Assert.Equal(Rac1PlanetTravelStartResult.Started, session.BeginSelectedTravel());

        Assert.True(session.TransitionActive);
        Assert.Equal(1, session.SourceLevel);
        Assert.Equal(2, session.ActiveTarget);
        Assert.Equal(2, session.PendingDestinationStorage);
        Assert.Equal(2, session.LoaderDestination);
        Assert.Equal(1, campaign.CurrentLevel);
        Assert.Equal(mapBefore, campaign.GalacticMap);
        Assert.Equal(visitedBefore, campaign.VisitedPlanets);

        session.CommitLevelEntry();

        Assert.True(session.TransitionActive);
        Assert.True(session.CurrentLevelCommitted);
        Assert.Equal(2, campaign.CurrentLevel);
        Assert.Equal(mapBefore, campaign.GalacticMap);
        Assert.Equal(visitedBefore, campaign.VisitedPlanets);

        session.FinishLevelEntry();

        Assert.False(session.TransitionActive);
        Assert.False(session.CurrentLevelCommitted);
        Assert.Null(session.ActiveTarget);
        Assert.Null(session.SourceLevel);
        Assert.Equal(2, session.PendingDestinationStorage);
        Assert.Equal(2, session.LoaderDestination);

        session.OpenPlanetMap();
        Assert.Equal(2, session.SelectedDestination);
    }

    [Fact]
    public void CurrentSelectionDoesNotStartAWorldTransition()
    {
        var campaign = new Rac1CampaignState();
        var session = new Rac1PlanetTravelSession(campaign);

        Assert.Equal(0, session.SelectedDestination);
        Assert.Equal(
            Rac1PlanetTravelStartResult.AlreadyCurrentLevel,
            session.BeginSelectedTravel());
        Assert.False(session.TransitionActive);
        Assert.Equal(0, campaign.CurrentLevel);
    }

    [Fact]
    public void UnadmittedDestinationCannotBecomePendingTarget()
    {
        var campaign = new Rac1CampaignState();
        var session = new Rac1PlanetTravelSession(campaign);

        Assert.False(session.TrySelectDestination(1));
        Assert.Equal(0, session.SelectedDestination);
        Assert.False(session.TransitionActive);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(19)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void SelectionRejectsIdsOutsideNativeLoaderTable(int destinationId)
    {
        var session = new Rac1PlanetTravelSession(new Rac1CampaignState());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.TrySelectDestination(destinationId));
    }

    [Fact]
    public void CommitAndCleanupRequireTheirNativeOrder()
    {
        var campaign = new Rac1CampaignState();
        campaign.AdmitDestination(1);
        var session = new Rac1PlanetTravelSession(campaign);

        Assert.Throws<InvalidOperationException>(() => session.CommitLevelEntry());
        Assert.Throws<InvalidOperationException>(() => session.FinishLevelEntry());

        Assert.True(session.TrySelectDestination(1));
        Assert.Equal(Rac1PlanetTravelStartResult.Started, session.BeginSelectedTravel());
        Assert.Throws<InvalidOperationException>(() => session.FinishLevelEntry());

        session.CommitLevelEntry();
        Assert.Throws<InvalidOperationException>(() => session.CommitLevelEntry());
        session.FinishLevelEntry();
    }
}

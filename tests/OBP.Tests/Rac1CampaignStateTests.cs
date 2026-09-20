using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignStateTests
{
    [Fact]
    public void OpeningStateMatchesRecoveredRetailSnapshot()
    {
        var state = new Rac1CampaignState();

        Assert.Equal(0, state.CurrentLevel);
        Assert.Equal(0, state.AdmittedDestinationCount);
        Assert.Equal(Rac1CampaignState.NativeLevelCapacity, state.VisitedPlanets.Count);
        Assert.Equal(Rac1CampaignState.NativeLevelCapacity, state.GalacticMap.Count);
        Assert.Equal(Rac1CampaignState.NativeLevelCapacity, state.LevelStates.Count);
        Assert.All(state.VisitedPlanets, value => Assert.Equal(0, value));
        Assert.All(state.GalacticMap, value => Assert.Equal(0, value));
        Assert.Equal(Rac1LevelVisitState.Visited, state.GetLevelState(0));
        Assert.False(state.CanSelectDestination(0));
        for (int levelId = 1; levelId < Rac1CampaignState.NativeDestinationCount; levelId++)
            Assert.Equal(Rac1LevelVisitState.Unvisited, state.GetLevelState(levelId));
        Assert.Equal(Rac1LevelVisitState.Unvisited, state.LevelStates[19]);
    }

    [Fact]
    public void DestinationDiscoveryEventsMapExactRetailRange()
    {
        for (int eventId = Rac1CampaignState.FirstDestinationDiscoveryEvent;
             eventId <= Rac1CampaignState.LastDestinationDiscoveryEvent;
             eventId++)
        {
            Assert.True(Rac1CampaignState.TryResolveDestinationDiscoveryEvent(eventId, out int destinationId));
            Assert.Equal(eventId - 0x24, destinationId);
        }

        Assert.False(Rac1CampaignState.TryResolveDestinationDiscoveryEvent(0x24, out _));
        Assert.False(Rac1CampaignState.TryResolveDestinationDiscoveryEvent(0x37, out _));
    }

    [Fact]
    public void ProgressionDiscoveryDoesNotTravelOrCompleteLevel()
    {
        var state = new Rac1CampaignState();

        Assert.Equal(
            Rac1DestinationDiscoveryResult.Discovered,
            state.ApplyProgressionEvent(0x27));
        Assert.Equal(
            Rac1DestinationDiscoveryResult.AlreadyDiscovered,
            state.ApplyProgressionEvent(0x27));
        Assert.Equal(
            Rac1DestinationDiscoveryResult.NotDestinationDiscoveryEvent,
            state.ApplyProgressionEvent(0x24));

        Assert.Equal(0, state.CurrentLevel);
        Assert.Equal(1, state.AdmittedDestinationCount);
        Assert.Equal(1, state.VisitedPlanets[3]);
        Assert.Equal(3, state.GalacticMap[0]);
        Assert.Equal(Rac1LevelVisitState.Unvisited, state.GetLevelState(3));
    }

    [Fact]
    public void AdmitDestinationIsOrderedAndIdempotent()
    {
        var state = new Rac1CampaignState();

        Assert.True(state.AdmitDestination(3));
        Assert.True(state.AdmitDestination(1));
        Assert.False(state.AdmitDestination(3));

        Assert.Equal(2, state.AdmittedDestinationCount);
        Assert.Equal(1, state.VisitedPlanets[1]);
        Assert.Equal(1, state.VisitedPlanets[3]);
        Assert.Equal(3, state.GalacticMap[0]);
        Assert.Equal(1, state.GalacticMap[1]);
        Assert.All(state.GalacticMap.Skip(2), value => Assert.Equal(0, value));
    }

    [Fact]
    public void UndiscoveredDestinationCannotBeSelectedOrTraveledTo()
    {
        var state = new Rac1CampaignState();

        Assert.False(state.CanSelectDestination(1));
        Assert.False(state.BeginTravel(1));
        Assert.Equal(0, state.CurrentLevel);
        Assert.Equal(Rac1LevelVisitState.Unvisited, state.GetLevelState(1));
    }

    [Fact]
    public void AdmittedDestinationTravelPersistsCurrentLevelAndVisitState()
    {
        var state = new Rac1CampaignState();

        Assert.True(state.AdmitDestination(1));
        Assert.True(state.BeginTravel(1));

        Assert.Equal(1, state.CurrentLevel);
        Assert.True(state.CanSelectDestination(1));
        Assert.Equal(Rac1LevelVisitState.Visited, state.GetLevelState(1));
    }

    [Fact]
    public void CompletionIsIndependentFromVisitAndAdmissionState()
    {
        var state = new Rac1CampaignState();
        state.AdmitDestination(1);
        state.BeginTravel(1);

        Assert.True(state.MarkLevelComplete(1));
        Assert.False(state.MarkLevelComplete(1));

        Assert.Equal(Rac1LevelVisitState.Completed, state.GetLevelState(1));
        Assert.Equal(1, state.CurrentLevel);
        Assert.Equal(1, state.AdmittedDestinationCount);
        Assert.Equal(1, state.VisitedPlanets[1]);
        Assert.Equal(1, state.GalacticMap[0]);

        Assert.True(state.BeginTravel(1));
        Assert.Equal(Rac1LevelVisitState.Completed, state.GetLevelState(1));
    }

    [Fact]
    public void PersistentSnapshotRoundTripsAndContinuesDiscoveryOrdering()
    {
        var state = new Rac1CampaignState();
        Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, state.ApplyProgressionEvent(0x27));
        Assert.True(state.BeginTravel(3));
        Assert.True(state.MarkLevelComplete(3));
        Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, state.ApplyProgressionEvent(0x25));

        Rac1CampaignPersistentState snapshot = state.CapturePersistentState();
        Rac1CampaignState restored = Rac1CampaignState.RestorePersistentState(snapshot);

        Assert.Equal(state.CurrentLevel, restored.CurrentLevel);
        Assert.Equal(state.VisitedPlanets, restored.VisitedPlanets);
        Assert.Equal(state.GalacticMap, restored.GalacticMap);
        Assert.Equal(state.LevelStates, restored.LevelStates);

        Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, restored.ApplyProgressionEvent(0x29));
        Assert.Equal(3, restored.AdmittedDestinationCount);
        Assert.Equal(5, restored.GalacticMap[2]);
        Assert.Equal(3, restored.CurrentLevel);
        Assert.Equal(Rac1LevelVisitState.Completed, restored.GetLevelState(3));
        Assert.Equal(Rac1LevelVisitState.Unvisited, restored.GetLevelState(5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Rac1CampaignState.NativeDestinationCount)]
    [InlineData(Rac1CampaignState.NativeLevelCapacity)]
    public void InvalidLevelIdsAreRejected(int levelId)
    {
        var state = new Rac1CampaignState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.AdmitDestination(levelId));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.CanSelectDestination(levelId));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginTravel(levelId));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.MarkLevelComplete(levelId));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetLevelState(levelId));
    }
}

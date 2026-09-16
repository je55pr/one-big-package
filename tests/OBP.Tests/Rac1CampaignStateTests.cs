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
        for (int levelId = 1; levelId < Rac1CampaignState.NativeLevelCapacity; levelId++)
            Assert.Equal(Rac1LevelVisitState.Unvisited, state.GetLevelState(levelId));
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

    [Theory]
    [InlineData(-1)]
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

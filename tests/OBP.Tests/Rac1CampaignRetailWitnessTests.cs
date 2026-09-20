using System.Text.Json;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1CampaignRetailWitnessTests
{
    private static JsonElement Witness()
    {
        string path = Path.Combine(
            RepoPaths.Root,
            "research",
            "generated",
            "rac1-campaign-travel-witness.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    [Fact]
    public void WitnessFreezesRecoveredRuntimeOwnersAndSerializationBlocks()
    {
        JsonElement witness = Witness();
        JsonElement runtime = witness.GetProperty("runtime");
        JsonElement descriptors = runtime.GetProperty("gameDescriptors");

        Assert.Equal("0x0015ed84", descriptors.GetProperty("currentLevel").GetProperty("runtimeAddress").GetString());
        Assert.Equal(4, descriptors.GetProperty("currentLevel").GetProperty("size").GetInt32());
        Assert.Equal(0, descriptors.GetProperty("currentLevel").GetProperty("blockId").GetInt32());

        Assert.Equal("0x0013dd40", descriptors.GetProperty("visitedPlanets").GetProperty("runtimeAddress").GetString());
        Assert.Equal(20, descriptors.GetProperty("visitedPlanets").GetProperty("size").GetInt32());
        Assert.Equal(14, descriptors.GetProperty("visitedPlanets").GetProperty("blockId").GetInt32());

        Assert.Equal("0x0013d510", descriptors.GetProperty("galacticMap").GetProperty("runtimeAddress").GetString());
        Assert.Equal(80, descriptors.GetProperty("galacticMap").GetProperty("size").GetInt32());
        Assert.Equal(20, descriptors.GetProperty("galacticMap").GetProperty("blockId").GetInt32());

        JsonElement perLevel = runtime.GetProperty("perLevelVisitedDescriptor");
        Assert.Equal("0x0013dd58", perLevel.GetProperty("runtimeAddress").GetString());
        Assert.Equal(1, perLevel.GetProperty("size").GetInt32());
        Assert.Equal(3001, perLevel.GetProperty("blockId").GetInt32());
    }

    [Fact]
    public void ModelReproducesRecoveredFourDestinationSaveAndRevisit()
    {
        JsonElement witness = Witness().GetProperty("memoryCard");
        JsonElement blocks = witness.GetProperty("gameBlocks");

        int[] expectedVisited = blocks.GetProperty("visitedPlanets").GetProperty("values")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int[] expectedMap = blocks.GetProperty("galacticMap").GetProperty("values")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int[] expectedLevelStates = witness.GetProperty("levelRecords")
            .EnumerateArray().Select(record => record.GetProperty("visited").GetInt32()).ToArray();

        var state = new Rac1CampaignState();
        foreach (int destination in expectedMap.Where(destination => destination != 0))
            Assert.True(state.AdmitDestination(destination));

        state.MarkLevelComplete(0);
        Assert.True(state.BeginTravel(1));
        state.MarkLevelComplete(1);
        Assert.True(state.BeginTravel(3));
        state.MarkLevelComplete(3);
        Assert.True(state.BeginTravel(2));

        Assert.Equal(blocks.GetProperty("currentLevel").GetProperty("value").GetInt32(), state.CurrentLevel);
        Assert.Equal(expectedVisited, state.VisitedPlanets.Select(value => (int)value));
        Assert.Equal(expectedMap, state.GalacticMap.Select(value => (int)value));
        Assert.Equal(expectedLevelStates, state.LevelStates.Select(value => (int)value));

        int[] revisit = witness.GetProperty("smallestSpecificRevisit")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Assert.Equal(new[] { 2, 1, 2 }, revisit);
        Assert.True(state.BeginTravel(revisit[1]));
        Assert.True(state.BeginTravel(revisit[2]));
        Assert.Equal(2, state.CurrentLevel);
        Assert.Equal(expectedVisited, state.VisitedPlanets.Select(value => (int)value));
        Assert.Equal(expectedMap, state.GalacticMap.Select(value => (int)value));
    }

    [Fact]
    public void RetailBoundaryKeepsSerializedSlotNineteenButRejectsItAsDestination()
    {
        Assert.Equal(20, Rac1CampaignState.NativeLevelCapacity);
        Assert.Equal(19, Rac1CampaignState.NativeDestinationCount);

        var state = new Rac1CampaignState();
        Assert.Equal(Rac1LevelVisitState.Unvisited, state.LevelStates[19]);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.AdmitDestination(19));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginTravel(19));
    }
}

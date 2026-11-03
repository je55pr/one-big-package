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
    public void WitnessSeparatesControllerCodeRoutingFromCompiledMissionAdmission()
    {
        JsonElement runtime = Witness().GetProperty("runtime");
        JsonElement opening = runtime.GetProperty("openingState");
        JsonElement code = runtime.GetProperty("code");
        JsonElement router = code.GetProperty("controllerCodeRouter");
        JsonElement missionObject = code.GetProperty("compiledMissionObject");

        Assert.Equal(0, opening.GetProperty("currentLevel").GetInt32());
        Assert.All(opening.GetProperty("visitedPlanets").EnumerateArray(), value => Assert.Equal(0, value.GetInt32()));
        Assert.All(opening.GetProperty("galacticMap").EnumerateArray(), value => Assert.Equal(0, value.GetInt32()));
        Assert.Equal(1, opening.GetProperty("perLevelState")[0].GetInt32());

        Assert.Equal("0x002831c0", router.GetProperty("routine").GetString());
        Assert.Equal("0x0013c940", router.GetProperty("padStateBase").GetString());
        Assert.Equal("0x001ba4a8", router.GetProperty("inputBuffer").GetString());
        Assert.Equal(20, router.GetProperty("inputSymbolCount").GetInt32());
        Assert.Equal("0x001b9ba0", router.GetProperty("matchTable").GetString());
        Assert.Equal(new[] { 5, 8, 10, 11 }, router.GetProperty("opaqueSerializedBlockIds")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray());
        Assert.Equal(0x25, router.GetProperty("firstEvent").GetInt32());
        Assert.Equal(0x36, router.GetProperty("lastEvent").GetInt32());
        Assert.Equal(0x24, router.GetProperty("destinationOffset").GetInt32());
        JsonElement[] events = router.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(18, events.Length);
        for (int index = 0; index < events.Length; index++)
        {
            Assert.Equal(0x25 + index, events[index].GetProperty("event").GetInt32());
            Assert.Equal(1 + index, events[index].GetProperty("destination").GetInt32());
        }

        Assert.Equal(750, missionObject.GetProperty("classId").GetInt32());
        Assert.Equal("0x2ee", missionObject.GetProperty("classIdHex").GetString());
        Assert.Equal("0x002d5de8", missionObject.GetProperty("updateRoutine").GetString());
        Assert.Equal("0x001e9f60", missionObject.GetProperty("stateTable").GetString());
        Assert.Equal(12, missionObject.GetProperty("stateCount").GetInt32());
        Assert.Equal(4, missionObject.GetProperty("destinationPVarOffset").GetInt32());
        Assert.Equal("0x0013dd40", missionObject.GetProperty("visitedPlanets").GetString());
        Assert.Equal(
            new[] { "VisitedPlanets[d]", "player distance", "player state" },
            missionObject.GetProperty("conditions").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
        Assert.Equal("0x002d6a20", missionObject.GetProperty("state8AdmissionCall").GetString());
        Assert.Equal("0x002607d0", missionObject.GetProperty("admissionRoutine").GetString());

        JsonElement initialAdmission = code.GetProperty("initialAdmission");
        Assert.Equal("0x0023d160", initialAdmission.GetProperty("currentLevelLoad").GetString());
        Assert.Equal("0x0023d164", initialAdmission.GetProperty("zeroSkipBranch").GetString());
        Assert.Equal("0x0023d16c", initialAdmission.GetProperty("admissionCall").GetString());
        Assert.Equal("0x00293414", code.GetProperty("completionStateStore").GetString());
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
    public void PersistentModelRestoresRecoveredMemoryCardBlocksExactly()
    {
        JsonElement witness = Witness().GetProperty("memoryCard");
        JsonElement blocks = witness.GetProperty("gameBlocks");

        byte[] visited = blocks.GetProperty("visitedPlanets").GetProperty("values")
            .EnumerateArray().Select(value => checked((byte)value.GetInt32())).ToArray();
        int[] map = blocks.GetProperty("galacticMap").GetProperty("values")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Rac1LevelVisitState[] levelStates = witness.GetProperty("levelRecords")
            .EnumerateArray()
            .Select(record => (Rac1LevelVisitState)record.GetProperty("visited").GetByte())
            .ToArray();

        var persistent = new Rac1CampaignPersistentState(
            blocks.GetProperty("currentLevel").GetProperty("value").GetInt32(),
            visited,
            map,
            levelStates);
        Rac1CampaignState state = Rac1CampaignState.RestorePersistentState(persistent);

        Assert.Equal(2, state.CurrentLevel);
        Assert.Equal(4, state.AdmittedDestinationCount);
        Assert.Equal(visited, state.VisitedPlanets);
        Assert.Equal(map, state.GalacticMap);
        Assert.Equal(levelStates, state.LevelStates);

        Rac1CampaignPersistentState captured = state.CapturePersistentState();
        Assert.Equal(visited, captured.VisitedPlanets);
        Assert.Equal(map, captured.GalacticMap);
        Assert.Equal(levelStates, captured.LevelStates);

        Assert.Equal(Rac1DestinationDiscoveryResult.Discovered, state.ApplyProgressionEvent(0x29));
        Assert.Equal(5, state.GalacticMap[4]);
        Assert.Equal(2, state.CurrentLevel);
        Assert.Equal(Rac1LevelVisitState.Unvisited, state.GetLevelState(5));
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

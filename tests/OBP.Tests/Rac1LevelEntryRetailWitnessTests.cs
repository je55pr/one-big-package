using System.Text.Json;

namespace OBP.Tests;

public sealed class Rac1LevelEntryRetailWitnessTests
{
    private static JsonElement Witness()
    {
        string path = Path.Combine(
            RepoPaths.Root,
            "research",
            "generated",
            "rac1-level-entry-witness.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    [Fact]
    public void WitnessFreezesDefaultAuthoredMobyEntrySeed()
    {
        JsonElement seed = Witness()
            .GetProperty("runtime")
            .GetProperty("entrySeed");

        Assert.Equal(
            "0x00244110..0x00245c27",
            seed.GetProperty("levelInitRoutine").GetString());
        Assert.Equal(
            "0x00244b08",
            seed.GetProperty("mobyPopulationCall").GetString());
        Assert.Equal(
            "0x00241940..0x00243d37",
            seed.GetProperty("mobyPopulationRoutine").GetString());
        Assert.Equal(
            "0x00242828",
            seed.GetProperty("gameplayMobyBlockPointerLoad").GetString());
        Assert.Equal(
            "0x00242a7c",
            seed.GetProperty("liveMobyInitializerCall").GetString());
        Assert.Equal(
            new[] { "0x00242ad8", "0x00242ae0", "0x00242aec" },
            seed.GetProperty("positionCopies")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            new[] { "0x00242af4", "0x00242afc", "0x00242b08" },
            seed.GetProperty("rotationCopies")
                .EnumerateArray()
                .Select(value => value.GetString()));

        JsonElement directRefs = seed.GetProperty("campaignFieldDirectRefs");
        foreach (JsonProperty field in directRefs.EnumerateObject())
        {
            Assert.Empty(field.Value.GetProperty("levelInit").EnumerateArray());
            Assert.Empty(field.Value.GetProperty("mobyPopulation").EnumerateArray());
        }
    }

    [Fact]
    public void WitnessSeparatesPersistentCampaignFromTransientTravelState()
    {
        JsonElement descriptors = Witness()
            .GetProperty("runtime")
            .GetProperty("saveDescriptors");

        Dictionary<int, JsonElement> game = descriptors.GetProperty("game")
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("blockId").GetInt32(),
                row => row);

        Assert.Equal(
            "0x0015ed84",
            game[0].GetProperty("runtimeAddress").GetString());
        Assert.Equal(
            "0x0013dd40",
            game[14].GetProperty("runtimeAddress").GetString());
        Assert.Equal(
            "0x0013d510",
            game[20].GetProperty("runtimeAddress").GetString());

        JsonElement perLevel = descriptors.GetProperty("level")
            .EnumerateArray()
            .Single(row => row.GetProperty("blockId").GetInt32() == 3001);
        Assert.Equal(
            "0x0013dd58",
            perLevel.GetProperty("runtimeAddress").GetString());
        Assert.Equal(1, perLevel.GetProperty("size").GetInt32());

        JsonElement transient =
            descriptors.GetProperty("transientTravelDescriptorMatches");
        Assert.Empty(
            transient.GetProperty("selectedDestination").EnumerateArray());
        Assert.Empty(
            transient.GetProperty("pendingDestination").EnumerateArray());
        Assert.Empty(
            transient.GetProperty("travelActive").EnumerateArray());
    }

    [Fact]
    public void WitnessKeepsOpeningAndRevisitProgressSeparateFromEntrySeed()
    {
        JsonElement witness = Witness();
        JsonElement opening = witness
            .GetProperty("runtime")
            .GetProperty("openingCampaignState");

        Assert.Equal(0, opening.GetProperty("currentLevel").GetInt32());
        Assert.All(
            opening.GetProperty("visitedPlanets").EnumerateArray(),
            value => Assert.Equal(0, value.GetInt32()));
        Assert.All(
            opening.GetProperty("galacticMap").EnumerateArray(),
            value => Assert.Equal(0, value.GetInt32()));
        Assert.Equal(
            1,
            opening.GetProperty("perLevelVisited")[0].GetInt32());

        JsonElement card = witness.GetProperty("memoryCard");
        Assert.Equal(2, card.GetProperty("currentLevel").GetInt32());
        Assert.Equal(
            new[] { 2, 1, 2 },
            card.GetProperty("smallestSpecificRevisit")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        Assert.Equal(
            new[] { 2, 2, 1, 2 },
            card.GetProperty("perLevelVisited")
                .EnumerateArray()
                .Take(4)
                .Select(value => value.GetInt32()));
    }
}

using System.Text.Json;

namespace OBP.Tests;

public sealed class Rac1PlanetTravelRetailWitnessTests
{
    private static JsonElement ReadJson(string fileName)
    {
        string path = Path.Combine(RepoPaths.Root, "research", "generated", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    [Fact]
    public void LiveLoaderTableMatchesRetailDiscIndexExactly()
    {
        JsonElement runtime = ReadJson("rac1-campaign-travel-witness.json")
            .GetProperty("runtime");
        JsonElement loader = runtime.GetProperty("levelLoader");
        JsonElement census = ReadJson("rac1-level-index-census.json");

        Assert.Equal("0x00137b80", loader.GetProperty("discIndexBase").GetString());
        Assert.Equal("0x28c8", loader.GetProperty("levelTableOffset").GetString());
        Assert.Equal("0x0013a448", loader.GetProperty("levelTableAddress").GetString());
        Assert.Equal("0x0013a4e0", loader.GetProperty("levelHeaderBuffer").GetString());
        Assert.Equal(0, loader.GetProperty("loadedHeaderNativeLevelId").GetInt32());
        Assert.Equal(0x2434, loader.GetProperty("loadedHeaderSize").GetInt32());

        JsonElement[] live = loader.GetProperty("levelTable").EnumerateArray().ToArray();
        JsonElement[] disc = census.GetProperty("levels").EnumerateArray().ToArray();
        Assert.Equal(19, live.Length);
        Assert.Equal(live.Length, disc.Length);

        for (int index = 0; index < live.Length; index++)
        {
            Assert.Equal(index, live[index].GetProperty("index").GetInt32());
            Assert.Equal(index, disc[index].GetProperty("tableSlot").GetInt32());
            Assert.Equal(index, disc[index].GetProperty("levelId").GetInt32());
            Assert.Equal(
                disc[index].GetProperty("headerLba").GetInt32(),
                live[index].GetProperty("headerLba").GetInt32());
            Assert.Equal(
                disc[index].GetProperty("tableRawSecondWord").GetInt32(),
                live[index].GetProperty("rawSecondWord").GetInt32());
        }
    }

    [Fact]
    public void WitnessFreezesSelectedPendingCommitAndCleanupBoundaries()
    {
        JsonElement runtime = ReadJson("rac1-campaign-travel-witness.json")
            .GetProperty("runtime");
        JsonElement opening = runtime.GetProperty("openingState");
        JsonElement travel = runtime.GetProperty("code").GetProperty("planetTravel");

        Assert.Equal(0, opening.GetProperty("selectedDestination").GetInt32());
        Assert.Equal(0, opening.GetProperty("pendingDestinationStorage").GetInt32());
        Assert.Equal(0, opening.GetProperty("travelActive").GetInt32());

        Assert.Equal("0x002762e8", travel.GetProperty("shipMapCurrentLevelLoad").GetString());
        Assert.Equal("0x00276320", travel.GetProperty("shipMapSelectedStore").GetString());
        Assert.Equal("0x00276d38", travel.GetProperty("shipTravelCall").GetString());
        Assert.Equal("0x00276d3c", travel.GetProperty("shipTravelSelectedLoad").GetString());
        Assert.Equal("0x0028ed8c", travel.GetProperty("travelActiveSet").GetString());
        Assert.Equal("0x0028ee8c", travel.GetProperty("pendingDestinationStore").GetString());
        Assert.Equal("0x0024d51c", travel.GetProperty("sourceSnapshotStore").GetString());
        Assert.Equal(
            "0x0024d528",
            travel.GetProperty("temporaryTargetCurrentLevelStore").GetString());
        Assert.Equal(
            "0x0024d628",
            travel.GetProperty("sourceCurrentLevelRestore").GetString());
        Assert.Equal("0x00293034", travel.GetProperty("loaderSelectorStore").GetString());
        Assert.Equal("0x0029341c", travel.GetProperty("pendingCommitLoad").GetString());
        Assert.Equal("0x00293434", travel.GetProperty("currentLevelCommit").GetString());
        Assert.Equal("0x00293444", travel.GetProperty("levelLoaderCall").GetString());
        Assert.Equal("0x00291df8", travel.GetProperty("travelActiveClear").GetString());
    }
}

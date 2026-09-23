using System.Text.Json;

namespace OBP.Tests;

public sealed class Rac1NpcInteractableCensusTests
{
    private static JsonElement ReadRoot(string fileName)
    {
        string path = Path.Combine(RepoPaths.Root, "research", "generated", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static JsonElement FindByOClass(JsonElement array, int oClass)
    {
        foreach (JsonElement row in array.EnumerateArray())
        {
            if (row.GetProperty("oClass").GetInt32() == oClass)
                return row;
        }

        throw new Xunit.Sdk.XunitException($"Missing oClass {oClass}.");
    }

    [Fact]
    public void CheckedInWitnessPinsClass750CampaignAdmissionBoundary()
    {
        JsonElement root = ReadRoot("rac1-npc-interactable-census.json");
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("SCUS-97199", root.GetProperty("authority").GetProperty("serial").GetString());        Assert.Equal("static-only", root.GetProperty("authority").GetProperty("method").GetString());

        JsonElement row = root.GetProperty("progressionMoby");
        Assert.Equal(750, row.GetProperty("oClass").GetInt32());
        Assert.Equal(208, row.GetProperty("pVarBytes").GetInt32());
        Assert.Equal(19, row.GetProperty("highLodPackets").GetInt32());
        Assert.Equal(43, row.GetProperty("joints").GetInt32());
        Assert.Equal(5, row.GetProperty("ordinarySequenceSlots").GetInt32());

        JsonElement[] placements = row.GetProperty("placements").EnumerateArray().ToArray();
        Assert.Equal([1, 3, 6, 6, 7, 10, 10, 17],
            placements.Select(x => x.GetProperty("level").GetInt32()).ToArray());
        Assert.Equal([0, 4, 5, 5, 8, 11, 12, 18],
            placements.Select(x => x.GetProperty("destinationAtPVar04").GetInt32()).ToArray());

        JsonElement admission = row.GetProperty("compiledAdmissionWitness");
        Assert.Equal("0x002d5de8", admission.GetProperty("registeredUpdateRoutine").GetString());
        Assert.Equal("0x001e9f60", admission.GetProperty("stateTable").GetString());
        Assert.Equal(12, admission.GetProperty("stateCount").GetInt32());
        Assert.Equal(4, admission.GetProperty("destinationPVarOffset").GetInt32());
        Assert.Equal(8, admission.GetProperty("admissionState").GetInt32());
        Assert.Equal("0x002d6a20", admission.GetProperty("state8AdmissionCall").GetString());
        Assert.Equal("0x002607d0", admission.GetProperty("admissionRoutine").GetString());
    }

    [Fact]
    public void CheckedInWitnessPinsOneOffActorCandidatesWithoutResolvingPublicLabels()
    {        JsonElement actors = ReadRoot("rac1-npc-interactable-census.json").GetProperty("actorCandidates");
        var expected = new[]
        {
            new { OClass = 890, Level = 3, PVar = 560, Packets = 28, Joints = 92, Update = "0x002da870" },
            new { OClass = 774, Level = 1, PVar = 416, Packets = 30, Joints = 90, Update = "0x002ff118" },
            new { OClass = 1283, Level = 8, PVar = 384, Packets = 30, Joints = 90, Update = "0x003065d8" },
            new { OClass = 919, Level = 5, PVar = 384, Packets = 30, Joints = 92, Update = "0x00317470" },
            new { OClass = 1144, Level = 8, PVar = 416, Packets = 37, Joints = 96, Update = "0x00305270" },
            new { OClass = 1130, Level = 8, PVar = 400, Packets = 33, Joints = 92, Update = "0x00302ce8" },
            new { OClass = 1190, Level = 4, PVar = 336, Packets = 36, Joints = 83, Update = "0x002e3078" },
        };

        Assert.Equal(expected.Length, actors.GetArrayLength());
        foreach (var item in expected)
        {
            JsonElement row = FindByOClass(actors, item.OClass);
            Assert.Equal(item.Level, row.GetProperty("level").GetInt32());
            Assert.Equal($"LEVEL{item.Level}", row.GetProperty("nativeLevel").GetString());
            Assert.Equal(item.PVar, row.GetProperty("pVarBytes").GetInt32());
            Assert.Equal(item.Packets, row.GetProperty("highLodPackets").GetInt32());
            Assert.Equal(item.Joints, row.GetProperty("joints").GetInt32());
            Assert.Equal(item.Update, row.GetProperty("registeredUpdateRoutine").GetString());
        }

        JsonElement unresolved = FindByOClass(actors, 1190);
        Assert.Equal("unresolved-contradictory-public-labels",
            unresolved.GetProperty("semanticGrade").GetString());
        Assert.Equal(["Fred", "Lieutenant"],
            unresolved.GetProperty("corroborativeLabels").EnumerateArray()
                .Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public void CheckedInWitnessSeparatesAcquisitionObjectsAndVendorBoundary()
    {
        JsonElement root = ReadRoot("rac1-npc-interactable-census.json");
        JsonElement objects = root.GetProperty("acquisitionInteractables");
        Assert.Equal([18, 1005, 1016, 1120, 1290, 1354, 1428],
            objects.EnumerateArray().Select(x => x.GetProperty("oClass").GetInt32()).Order().ToArray());

        JsonElement helmet = FindByOClass(objects, 1290);
        Assert.Equal("LEVEL9", helmet.GetProperty("nativeLevel").GetString());
        Assert.Equal(19, helmet.GetProperty("authoredTableLevelCount").GetInt32());

        JsonElement vendor = root.GetProperty("vendorSpeechBank");
        Assert.Equal("0x1a0", vendor.GetProperty("tocOffset").GetString());
        Assert.Equal(37, vendor.GetProperty("entryCount").GetInt32());
        Assert.Equal("Fanfare_Vendor", vendor.GetProperty("finalMetadataLabel").GetString());
        Assert.Equal(JsonValueKind.Null, vendor.GetProperty("visibleVendorOClass").ValueKind);
    }

    [Fact]
    public void WitnessStructuralFactsAgreeWithAuthoredMobyCensus()
    {
        JsonElement witness = ReadRoot("rac1-npc-interactable-census.json");
        JsonElement classes = ReadRoot("rac1-authored-moby-census.json").GetProperty("classes");
        void Check(JsonElement row, int expectedInstances)
        {
            int id = row.GetProperty("oClass").GetInt32();
            JsonElement cls = FindByOClass(classes, id);
            Assert.Equal(expectedInstances, cls.GetProperty("totalInstances").GetInt32());
            Assert.Equal([row.GetProperty("pVarBytes").GetInt32()],
                cls.GetProperty("pVarByteSizes").EnumerateArray().Select(x => x.GetInt32()).ToArray());
            Assert.Equal([row.GetProperty("highLodPackets").GetInt32()],
                cls.GetProperty("highLodPacketCounts").EnumerateArray().Select(x => x.GetInt32()).ToArray());
            Assert.Equal([row.GetProperty("joints").GetInt32()],
                cls.GetProperty("jointCounts").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        }

        JsonElement progression = witness.GetProperty("progressionMoby");
        Check(progression, 8);
        JsonElement progressionClass = FindByOClass(classes, 750);
        Assert.Equal(
            progression.GetProperty("authoredTableLevels").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            progressionClass.GetProperty("levelTableIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal([1, 3, 6, 7, 10, 17],
            progressionClass.GetProperty("levels").EnumerateArray()
                .Where(x => x.GetProperty("instances").GetInt32() > 0)
                .Select(x => x.GetProperty("level").GetInt32()).ToArray());

        foreach (JsonElement row in witness.GetProperty("actorCandidates").EnumerateArray())
        {
            Check(row, 1);
            int level = row.GetProperty("level").GetInt32();
            JsonElement cls = FindByOClass(classes, row.GetProperty("oClass").GetInt32());            JsonElement placed = cls.GetProperty("levels").EnumerateArray()
                .Single(x => x.GetProperty("level").GetInt32() == level);
            Assert.Equal(1, placed.GetProperty("instances").GetInt32());
        }

        foreach (JsonElement row in witness.GetProperty("acquisitionInteractables").EnumerateArray())
        {
            Check(row, 1);
            int level = row.GetProperty("level").GetInt32();
            JsonElement cls = FindByOClass(classes, row.GetProperty("oClass").GetInt32());
            JsonElement placed = cls.GetProperty("levels").EnumerateArray()
                .Single(x => x.GetProperty("level").GetInt32() == level);
            Assert.Equal(1, placed.GetProperty("instances").GetInt32());
        }

        JsonElement class1290 = FindByOClass(classes, 1290);
        Assert.Equal(19, class1290.GetProperty("levelTableOccurrences").GetInt32());
        Assert.Equal(1, class1290.GetProperty("totalInstances").GetInt32());
    }
}

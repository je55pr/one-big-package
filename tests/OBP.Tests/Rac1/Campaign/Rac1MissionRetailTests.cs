using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1MissionRetailTests
{
    [Fact]
    public void WitnessContainsCompleteDistinctObservationRosters()
    {
        using var witness = Rac1MissionProgressionTests.Witness();
        RequireRosters(witness.RootElement);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("infobots")]
    public void WitnessRejectsRepeatedNumericObservationIdentities(string cohort)
    {
        using var witness = Rac1MissionProgressionTests.Witness();
        var root = JsonNode.Parse(witness.RootElement.GetRawText())!;
        var rows = root[cohort]!.AsArray();
        rows[1] = rows[0]!.DeepClone();
        if (cohort == "code")
            foreach (string address in new[] { "start", "end" })
                rows[1]![address] = rows[1]![address]!.GetValue<string>().ToUpperInvariant();
        using var repeated = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => RequireRosters(repeated.RootElement));
    }

    [Theory]
    [InlineData("code-range")]
    [InlineData("code-name")]
    [InlineData("programs")]
    [InlineData("infobots")]
    public void WitnessRejectsDistinctReplacementIdentities(string cohort)
    {
        using var witness = Rac1MissionProgressionTests.Witness();
        RequireRosters(witness.RootElement);
        var root = JsonNode.Parse(witness.RootElement.GetRawText())!;
        if (cohort == "code-range")
        {
            var rows = root["code"]!.AsArray();
            int index = rows.Select((row, index) => (row, index)).Single(pair => pair.row!["name"]!.GetValue<string>() == "infobotDispatch").index;
            var replacement = rows.First(row => row!["name"]!.GetValue<string>() == "infobotUpdate")!.DeepClone();
            replacement["end"] = replacement["start"]!.DeepClone();
            replacement["sha256"] = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
            rows[index] = replacement;
        }
        else if (cohort == "code-name") root["code"]![0]!["name"] = "missing-code-observation";
        else root[cohort]![0]!["index"] = root[cohort]![0]!["index"]!.GetValue<int>() + 1;
        using var replaced = JsonDocument.Parse(root.ToJsonString());
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => RequireRosters(replaced.RootElement));
    }

    private static void RequireRosters(JsonElement witness)
    {
        Assert.Equal(new[] { 0, 1 }, witness.GetProperty("overlays").EnumerateArray()
            .Select(row => row.GetProperty("level").GetInt32()).Order());
        // Preserve the independent probe definitions in tools/OBP.Rac1MissionProbe/Program.cs.
        (int Level, string Name, int Start, int End)[] code =
        [
            (0, "infobotUpdate", 0x2d5de8, 0x2d6bf0), (0, "infobotDispatch", 0x1e9f60, 0x1e9f90),
            (0, "talkUpdateInitStep", 0x266448, 0x266d48), (0, "VeldinActor", 0x2d9dc8, 0x2d9f78),
            (0, "destinationAdmission", 0x2607d0, 0x260860), (0, "missionExit", 0x28eeb0, 0x28eee0),
            (0, "talkerRegistration", 0x242c14, 0x242c34), (0, "lateLoaderCommit", 0x2933c4, 0x29344c),
            (0, "entryAdmission", 0x23d160, 0x23d178), (1, "PlumberProgression", 0x2ff118, 0x2ff3d0),
            (1, "talkUpdateInitStep", 0x27b028, 0x27b930), (1, "destinationAdmission", 0x2756d0, 0x275760),
        ];
        Assert.Equal(code.Order(), witness.GetProperty("code").EnumerateArray()
            .Select(row => (row.GetProperty("level").GetInt32(), row.GetProperty("name").GetString()!, Address(row, "start"), Address(row, "end"))).Order());
        Assert.Equal(new[] { (0, 165), (1, 901) }, witness.GetProperty("programs").EnumerateArray()
            .Select(row => (row.GetProperty("level").GetInt32(), row.GetProperty("index").GetInt32())).Order());
        Assert.Equal(new[] { (1, 874), (3, 908), (6, 373), (6, 374), (7, 386), (10, 482), (10, 483), (17, 440) },
            witness.GetProperty("infobots").EnumerateArray()
                .Select(row => (row.GetProperty("level").GetInt32(), row.GetProperty("index").GetInt32())).Order());
    }

    private static int Address(JsonElement row, string name) =>
        int.Parse(row.GetProperty(name).GetString()![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [SkippableFact]
    public void MissionWitness_MatchesOriginalCodeAuthoredActorsAndCompiledTalkPrograms()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        using var witness = Rac1MissionProgressionTests.Witness();
        RequireRosters(witness.RootElement);
        var levels = Rac1DiscIndex.Read(disc).Levels;
        var overlays = new Dictionary<int, Rac1ExecutableOverlay>();
        foreach (var row in witness.RootElement.GetProperty("overlays").EnumerateArray())
        {
            int id = row.GetProperty("level").GetInt32();
            var overlay = Rac1ExecutableOverlay.Read(disc, levels.Single(l => l.LevelId == id));
            Assert.Equal(row.GetProperty("sha256").GetString(), overlay.Sha256);
            overlays.Add(id, overlay);
        }
        foreach (var row in witness.RootElement.GetProperty("code").EnumerateArray())
        {
            byte[] hash = SHA256.HashData(overlays[row.GetProperty("level").GetInt32()].Read(Address(row, "start"), Address(row, "end") - Address(row, "start")));
            Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexString(hash).ToLowerInvariant());
        }
        foreach (var row in witness.RootElement.GetProperty("programs").EnumerateArray())
        {
            int id = row.GetProperty("level").GetInt32();
            var actors = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, levels.Single(l => l.LevelId == id))).MobyInstances;
            var actor = actors.Single(a => a.Index == row.GetProperty("index").GetInt32());
            Assert.Equal(row.GetProperty("oClass").GetInt32(), actor.OClass);
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(actor.RawRecord.AsSpan(0x74)));
            var actual = Rac1TalkProgram.Read(overlays[id], id, actor)!;
            var expected = Rac1MissionProgressionTests.Program(id);
            Assert.Equal(expected.GlobalTalkerIndex, actual.GlobalTalkerIndex);
            Assert.Equal(expected.Records.OrderBy(p => p.Key), actual.Records.OrderBy(p => p.Key));
            Assert.Equal(expected.BoltPrices.OrderBy(p => p.Key), actual.BoltPrices.OrderBy(p => p.Key));
        }
        var infobots = new List<(int Level, int Index, Rac1InfobotAuthored Authored)>();
        foreach (var level in levels)
            foreach (var actor in Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances.Where(a => a.OClass == 750))
                infobots.Add((level.LevelId, actor.Index, Rac1InfobotAuthored.Read(actor)));
        var rows = witness.RootElement.GetProperty("infobots").EnumerateArray().ToArray();
        Assert.Equal(rows.Length, infobots.Count);
        foreach (var row in rows)
        {
            var actual = infobots.Single(a => a.Level == row.GetProperty("level").GetInt32() && a.Index == row.GetProperty("index").GetInt32()).Authored;
            var expected = row.GetProperty("authored").Deserialize<Rac1InfobotAuthored>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal(expected.PatrolPaths, actual.PatrolPaths);
            Assert.Equal(expected with { PatrolPaths = actual.PatrolPaths }, actual);
        }
    }
}

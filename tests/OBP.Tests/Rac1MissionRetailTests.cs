using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;

namespace OBP.Tests;

public sealed class Rac1MissionRetailTests
{
    [SkippableFact]
    public void MissionWitness_MatchesOriginalCodeAuthoredActorsAndCompiledTalkPrograms()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        using var witness = Rac1MissionProgressionTests.Witness();
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
            int Address(string name) => int.Parse(row.GetProperty(name).GetString()![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte[] hash = SHA256.HashData(overlays[row.GetProperty("level").GetInt32()].Read(Address("start"), Address("end") - Address("start")));
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

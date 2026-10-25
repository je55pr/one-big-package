using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OneBigPackage;

namespace OBP.Tests;

public sealed class CrossWorldReplayTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-shared-replay-" + Guid.NewGuid().ToString("N"));
    private static PlaytestRunHeader Header(string destination) => new(2, destination, "fixture-world", Guid.NewGuid(), "fixture-engine",
        "fixture-platform", "fixture-physics", 60, 1440, 960, 720, DateTimeOffset.UnixEpoch, JsonSerializer.SerializeToElement(new { }),
        JsonSerializer.SerializeToElement(new { }), new(new Dictionary<string, PlaytestValue> { ["world.destination"] = new(Text: destination) }))
    {
        PlayerProfile = JsonSerializer.Deserialize<JsonElement>((ObpProfileTests.Profile() with { Destination = destination }).Encode()),
        Assemblies = new Dictionary<string, Guid> { ["fixture"] = Guid.NewGuid() },
        Sources = new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 }.Select(game => new ObpBuildIdentity(game, "fixture", Sha256: new string((char)('A' + (int)game), 64))).ToArray(),
        Adapters = [new("obp/shared-player/v1", 1, 60)],
    };
    private static PlaytestGameplayFact Shot(string source, long sequence) => new("shot", sequence,
        JsonSerializer.SerializeToElement(new { Shot = new WeaponShotKey(new(source, 30), 1, sequence) }));

    [Theory]
    [InlineData("rac1:LEVEL0")]
    [InlineData("rac2:LEVEL1")]
    [InlineData("rac3:TABLE1")]
    [InlineData("obp:fusion-lab")]
    public async Task CompletedPrefixSurvivesRestartWithQualifiedCommandsShotsAndExactProfile(string destination)
    {
        string path = Path.Combine(_directory, "run.jsonl.gz"), prefix = Path.Combine(_directory, "prefix.run.jsonl.gz");
        var header = Header(destination);
        PlaytestGameplayFact[] facts = [Shot("rac2", 1), Shot("rac3", 1), Shot("rac2", 2)];
        var step = new PlaytestStep(0, PlaytestStepKind.Physics, 0, 1d / 60, [], new Dictionary<string, float>(), [])
        { Commands = [new("destination", destination), new("equip", "rac2/item/30"), new("quick-slot", "rac3/item/43", 7)], Gameplay = facts };
        using (var writer = new PlaytestRunWriter(path, header))
        {
            writer.Add(step); writer.Mark(new(0, "fixture-marker"));
            await writer.ExportSnapshotAsync(prefix);
            writer.Add(step with { Index = 1, Milliseconds = 16, Gameplay = [Shot("rac3", 2)] });
            await writer.FinishAsync("saved");
        }
        using var reader = new PlaytestRunReader(prefix);
        reader.Header.VerifyEnvironment("fixture-engine", "fixture-platform", "fixture-physics", 60);
        reader.Header.VerifySessionSources(header.Sources.Reverse(), header.Adapters);
        Assert.Equal(destination, ObpProfile.Decode(System.Text.Encoding.UTF8.GetBytes(reader.Header.PlayerProfile!.Value.GetRawText())).Destination);
        var actual = reader.ReadNextStep()!;
        Assert.Equal(step.Commands, actual.Commands);
        Assert.True(PlaytestGameplayFact.Matches(facts, actual.Gameplay));
        Assert.False(PlaytestGameplayFact.Matches(facts.AsEnumerable().Reverse().ToArray(), actual.Gameplay));
        Assert.Null(reader.ReadNextStep()); Assert.Single(reader.Markers);
        Assert.NotNull(reader.End);
    }

    [Fact]
    public void MissingSourceChangedDigestAdapterBackendClockAndUnknownSchemaAreRejected()
    {
        var header = Header("obp:fusion-lab"); header.Validate();
        Assert.Throws<InvalidDataException>(() => header.VerifySessionSources(header.Sources.Take(2), header.Adapters));
        Assert.Throws<InvalidDataException>(() => header.VerifySessionSources(header.Sources.Select(source => source with { Sha256 = new string('F', 64) }), header.Adapters));
        Assert.Throws<InvalidDataException>(() => header.VerifySessionSources(header.Sources, [new("obp/shared-player/v2", 1, 60)]));
        Assert.Throws<InvalidDataException>(() => header.VerifyEnvironment("fixture-engine", "fixture-platform", "other-backend", 60));
        Assert.Throws<InvalidDataException>(() => header.VerifyEnvironment("fixture-engine", "fixture-platform", "fixture-physics", 50));
        Assert.Throws<InvalidDataException>(() => (header with { Version = 99 }).Validate());
        Assert.Throws<InvalidDataException>(() => (header with { Version = 1 }).Validate());
        Assert.Throws<InvalidDataException>(() => (header with { Sources = [header.Sources[0], header.Sources[0]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (header with { PlayerProfile = null }).Validate());
    }

    [Theory]
    [InlineData("rac2:LEVEL1")]
    [InlineData("rac3:TABLE1")]
    [InlineData("obp:fusion-lab")]
    public async Task BugInboxRetainsExactSourcePrerequisitesAndPortableMarkerFromEachWorld(string destination)
    {
        var inbox = new BugReportInbox(Path.Combine(_directory, "inbox")); var id = Guid.NewGuid();
        using var writer = new PlaytestRunWriter(Path.Combine(_directory, "capture.run.jsonl.gz"), Header(destination));
        writer.Add(new(0, PlaytestStepKind.Process, 0, 1d / 60, [], new Dictionary<string, float>(), []));
        writer.Mark(new(0, "F8"));
        var session = await inbox.SaveAsync(id, writer, new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 0, "F8", destination, "Selection and firing", "fixture"));
        var resolved = inbox.Resolve(id, session.Reports[0].Id);
        Assert.Equal(2, resolved.Session.Recording.CaptureVersion); Assert.Equal(3, resolved.Session.Recording.Sources.Count);
        Assert.Single(resolved.Session.Recording.Adapters);
        string export = inbox.Export(id, session.Reports[0].Id);
        Assert.True(File.Exists(export));
    }

    [Theory]
    [InlineData("rac2:LEVEL1")]
    [InlineData("rac3:TABLE1")]
    [InlineData("obp:fusion-lab")]
    public void UniversalBookmarkRequiresAQualifiedProfileAndSafeCompletedPrefix(string destination)
    {
        var state = MovementReproStateTests.Sample() with
        {
            Version = 3,
            Destination = destination,
            Profile = Header(destination).PlayerProfile,
            Replay = new("prefix.run.jsonl.gz", new string('A', 64), 17)
        };
        state.Validate();
        Assert.Throws<InvalidDataException>(() => (state with { Replay = null }).Validate());
        Assert.Throws<InvalidDataException>(() => (state with { Replay = state.Replay with { FileName = "../other.run.jsonl.gz" } }).Validate());
        Assert.Throws<InvalidDataException>(() => (state with { Profile = null }).Validate());
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}

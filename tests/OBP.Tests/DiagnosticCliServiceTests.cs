using OBP.Core;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class DiagnosticCliServiceTests
{
    [Theory]
    [InlineData("rac3:TABLE1", "rac1:LEVEL0", "siberius", "rac3:TABLE1", "rac3-table1")]
    [InlineData(null, "rac1:LEVEL0", "siberius", "rac1:LEVEL0", "rac1-level0")]
    [InlineData(null, null, "siberius", "rac2:LEVEL7", "siberius")]
    [InlineData(null, null, null, "rac2:LEVEL1", "oozla")]
    public async Task ShotsPreserveSourceRegistrationTokenPrecedenceNamesAndDeferredMetadata(string? argument, string? listWorld,
        string? planet, string expectedDestination, string slug)
    {
        using var list = new ShotFile(new(listWorld, [new("top", ShotCamera.TopDown, "kinds", 3), new("orbit", ShotCamera.Orbit, null, 4)]));
        var fixture = new Shots(); var sources = new List<ObpSourceGame>(); var exits = new List<(string, int)>();
        var args = new CommandLineArgs
        {
            ShotsWorld = argument,
            Planet = planet,
            Rac2Level = 1,
            Rac1Iso = "source1",
            Rac2Iso = "source2",
            Rac3Iso = "source3",
            ShotsOut = "relative-output"
        };
        await Service(args, fixture, exits, (source, _) => sources.Add(source)).RunShotsAsync(list.Path, default);
        Assert.Equal([ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3], sources);
        Assert.Equal(expectedDestination, fixture.Destination);
        Assert.Equal([Path.Combine("relative-output", slug + "-top.png"), Path.Combine("relative-output", slug + "-orbit.png")], fixture.Paths);
        Assert.Equal(["apply:top", "postdraw:3", "metadata", "apply:orbit", "postdraw:4", "metadata"], fixture.Events);
        Assert.Equal("shot:top", fixture.Metadata[0]["capture"]);
        var shot = Assert.IsType<Dictionary<string, object?>>(fixture.Metadata[0]["shot"]);
        Assert.Equal("TopDown", shot["camera"]); Assert.Equal("kinds", shot["overlay"]); Assert.Equal(3, shot["settleFrames"]);
        Assert.Equal(("shots-complete", 0), Assert.Single(exits));
    }

    [Fact]
    public async Task BadListAndMissingSourceRetainUsageExitWithoutOpeningAWorld()
    {
        var fixture = new Shots(); var exits = new List<(string, int)>();
        await Service(new(), fixture, exits).RunShotsAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), default);
        Assert.Equal(("shots-list-read-failed", 2), Assert.Single(exits)); Assert.Null(fixture.Destination);
        exits.Clear();
        using var list = new ShotFile(new("oozla", [new("top")]));
        await Service(new(), fixture, exits).RunShotsAsync(list.Path, default);
        Assert.Equal(("shots-missing-gc-source", 2), Assert.Single(exits)); Assert.Null(fixture.Destination);
    }

    [Theory]
    [InlineData(false, true, "shots-world-load-failed")]
    [InlineData(true, false, "shots-capture-failed")]
    public async Task MissingWorldOrFailedSaveRetainsFailureExit(bool loaded, bool saved, string reason)
    {
        using var list = new ShotFile(new("rac1:LEVEL0", [new("top")]));
        var fixture = new Shots { Loaded = loaded, Saved = saved }; var exits = new List<(string, int)>();
        await Service(new(), fixture, exits).RunShotsAsync(list.Path, default);
        Assert.Equal((reason, 1), Assert.Single(exits));
        Assert.Equal(loaded ? 1 : 0, fixture.Metadata.Count);
    }

    [Fact]
    public async Task CanceledShotBatchDrainsWithoutMetadataLaterShotsOrSuccessExit()
    {
        using var list = new ShotFile(new("rac1:LEVEL0", [new("first"), new("second")]));
        var fixture = new Shots { Suspend = true }; var exits = new List<(string, int)>();
        using var operations = new HostOperationScope();
        operations.Start("shots", token => Service(new(), fixture, exits).RunShotsAsync(list.Path, token));
        Assert.Single(fixture.Paths);
        await operations.StopAsync();
        Assert.Empty(fixture.Metadata); Assert.Empty(exits);
        Assert.Equal(0, operations.PendingCount); Assert.Empty(operations.TakeFailures());
    }

    [Fact]
    public async Task BadOutputIsRetainedAsAnOwnedFailure()
    {
        using var list = new ShotFile(new("rac1:LEVEL0", [new("top")]));
        var fixture = new Shots { Failure = new IOException("output unavailable") }; var exits = new List<(string, int)>();
        using var operations = new HostOperationScope();
        operations.Start("shots", token => Service(new(), fixture, exits).RunShotsAsync(list.Path, token));
        await operations.StopAsync();
        Assert.IsType<IOException>(Assert.Single(operations.TakeFailures()).Error);
        Assert.Empty(exits);
    }

    [Fact]
    public async Task StressRetainsTokenIndexedSelectorBounceProcessWaitsAndSummary()
    {
        var world = new StressWorld(); var clock = new Clock(); var exits = new List<(string, int)>(); var messages = new List<string>();
        await new StressSwitchCliService(world, clock, (reason, code) => exits.Add((reason, code)), messages.Add, messages.Add)
            .RunStressSwitchAsync(" oozla ,__bad-import,unknown,rac1:LEVEL0,rac3:TABLE1", default);
        Assert.Equal(["open:rac2:LEVEL1", "bad-import", "selector", "open:rac1:LEVEL0", "open:rac3:TABLE1"], world.Events);
        Assert.Equal(46 * 3 + 10, clock.Waits);
        Assert.All(clock.Phases, phase => Assert.Equal(DiagnosticFramePhase.Process, phase));
        Assert.Contains("[stress] PASS rejected bad import, retained world/player/campaign", messages);
        Assert.Contains(messages, row => row.StartsWith("[stress] # 3 rac1:LEVEL0", StringComparison.Ordinal));
        Assert.Contains("[stress] ================ summary ================", messages);
        Assert.Equal(("stress-switch-complete", 0), Assert.Single(exits));
    }

    [Theory]
    [InlineData("world")]
    [InlineData("root")]
    [InlineData("player")]
    [InlineData("campaign")]
    [InlineData("admitted")]
    [InlineData("missing-world")]
    public async Task StressRejectsFailedImportThatChangesAnyRetainedOwner(string changed)
    {
        var world = new StressWorld { Corrupt = changed }; var exits = new List<(string, int)>();
        await new StressSwitchCliService(world, new Clock(), (reason, code) => exits.Add((reason, code)), _ => { }, _ => { })
            .RunStressSwitchAsync("__bad-import,rac1:LEVEL0", default);
        Assert.Equal(("stress-import-retention-failed", 1), Assert.Single(exits));
        Assert.Equal(["bad-import"], world.Events);
    }

    [Fact]
    public async Task CanceledStressSwitchDrainsItsWaitAndDoesNotEnterTheNextWorld()
    {
        var world = new StressWorld(); var exits = new List<(string, int)>(); var clock = new Clock { Suspend = true };
        using var operations = new HostOperationScope();
        var service = new StressSwitchCliService(world, clock, (reason, code) => exits.Add((reason, code)), _ => { }, _ => { });
        operations.Start("stress-switch", token => service.RunStressSwitchAsync("rac1:LEVEL0,rac3:TABLE1", token));
        Assert.Equal(1, clock.Waits);
        await operations.StopAsync();
        Assert.Equal(["open:rac1:LEVEL0"], world.Events); Assert.Empty(exits);
        Assert.Empty(operations.TakeFailures()); Assert.Equal(0, operations.PendingCount);
    }

    private static ShotListCliService Service(CommandLineArgs args, Shots fixture, List<(string, int)> exits,
        Action<ObpSourceGame, string>? register = null) => new(args, register ?? ((_, _) => { }), fixture,
            (reason, code) => exits.Add((reason, code)), _ => { }, _ => { });

    private sealed class ShotFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
        public ShotFile(ShotList list) => File.WriteAllText(Path, list.ToJson());
        public void Dispose() => File.Delete(Path);
    }
    private sealed class Shots : IShotListCaptureFixture
    {
        public string? Destination;
        public bool Loaded = true, Saved = true, Suspend;
        public Exception? Failure;
        public List<string> Paths { get; } = [];
        public List<string> Events { get; } = [];
        public List<IDictionary<string, object?>> Metadata { get; } = [];
        public bool OpenWorld(string destinationId) { Destination = destinationId; return Loaded; }
        public void Apply(ShotSpec shot) => Events.Add("apply:" + shot.Name);
        public IDictionary<string, object?> ReadMetadata() { Events.Add("metadata"); return new Dictionary<string, object?>(); }
        public async Task<bool> CaptureAsync(string output, int settleFrames, Func<IDictionary<string, object?>> metadata, CancellationToken token)
        {
            Paths.Add(output);
            if (Suspend) await Task.Delay(Timeout.Infinite, token);
            if (Failure is { } failure) throw failure;
            Events.Add("postdraw:" + settleFrames);
            Metadata.Add(metadata());
            return Saved;
        }
    }
    private sealed class Clock : IDiagnosticFrameClock
    {
        public int Waits;
        public bool Suspend;
        public List<DiagnosticFramePhase> Phases { get; } = [];
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)Waits, 0);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Waits++; Phases.Add(phase);
            return Suspend ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;
        }
    }
    private sealed class StressWorld : IStressWorldFixture
    {
        private StressRetentionSnapshot _retained = new(new("rac2:LEVEL1", ObpSourceGame.Rac2, "world", 1), 10, 11, 0);
        public string? Corrupt;
        public List<string> Events { get; } = [];
        public StressRetentionSnapshot ReadRetention() => Corrupt == "missing-world" ? new(new(null, null, null, 0), null, null, 0) : _retained;
        public bool RejectImport(string missingPath)
        {
            Assert.False(File.Exists(missingPath)); Events.Add("bad-import");
            _retained = Corrupt switch
            {
                "world" => _retained with { World = _retained.World with { Generation = 2 } },
                "root" => _retained with { SceneRoot = 12 },
                "player" => _retained with { Player = 12 },
                "campaign" => _retained with { CampaignLevel = 1 },
                _ => _retained,
            };
            return Corrupt is not ("admitted" or "missing-world");
        }
        public void ShowSelector() => Events.Add("selector");
        public void OpenDestination(string destinationId) => Events.Add("open:" + destinationId);
        public StressCounterSnapshot ReadCounters() => new(1, 12, 13, 14, 0, 4, true);
    }
}

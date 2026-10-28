using System.Numerics;
using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.Runtime.Diagnostics;

namespace OBP.Tests;

public sealed class CaptureReplayCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "obp-capture-owner-" + Guid.NewGuid().ToString("N"));
    private static readonly CaptureEnvironment Environment = new("engine", "platform", "physics", 60);
    private static readonly CaptureBuildIdentity Build = new(Guid.Parse("77fb3092-f15b-4c2e-b624-688144a05a17"),
        new SortedDictionary<string, Guid> { ["OBP.Game"] = Guid.Parse("77fb3092-f15b-4c2e-b624-688144a05a17") }, "fixture");
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void CoordinatorCapturesQualifiedHeaderAndNativeObservationsWithoutCreatingUi()
    {
        var fixture = new Fixture(); var owner = fixture.Owner();
        owner.Initialize(new() { RecordPlaytest = "fixture", Destination = "rac2:LEVEL1", FixedSeed = 734 }, () => throw new Exception(), fixture.Install);
        var header = owner.CaptureHeader(); header.Validate();
        Assert.Equal(2, header.Version); Assert.Equal(734, header.Seed); Assert.Equal("rac2:LEVEL1", header.Destination);
        Assert.Equal(fixture.Profile.Encode(), ObpProfile.Decode(System.Text.Encoding.UTF8.GetBytes(header.PlayerProfile!.Value.GetRawText())).Encode());
        Assert.Equal(new[] { "actor/rac2/v1", "obp/shared-player/v1", "obp/weapon-controllers/v1", "rac1/ordinary-player/v1" }, header.Adapters.Select(a => a.Id));
        Assert.Equal(new[] { "OBP.Game" }, header.Assemblies.Keys);
        Assert.Equal(new double[] { 8, 9, 10 }, header.InitialState.Values["player.position"].Numbers);
        Assert.Equal(.002, header.InitialState.Values["player.position"].Tolerance);
        Assert.Equal(.0001, header.InitialState.Values["player.facing"].Tolerance);
        Assert.Equal(new double[] { 4, 0 }, header.InitialState.Values["player.nanotech"].Numbers);
        Assert.Equal(new double[] { 1, 2, 3, 1, 0, 7, 4, 6 }, header.InitialState.Values["actor.rac2.500.2"].Numbers);
        Assert.Equal(new double[] { 17 }, header.InitialState.Values["obp.actor-clock"].Numbers);
        Assert.Equal(Json(new { Owned = 30 }).GetRawText(), header.InitialState.Values["player.inventory"].Text);
        Assert.DoesNotContain("synthetic-private-source", JsonSerializer.Serialize(header), StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHashIgnoresSessionGenerationButPreservesNativeActorStateAndSourceIdentity()
    {
        var fixture = new Fixture(); var owner = fixture.Owner();
        var before = owner.CaptureCheckpoint();
        fixture.Profile = fixture.Profile with { Player = fixture.Profile.Player with { Generation = fixture.Profile.Player.Generation + 1 } };
        var after = owner.CaptureCheckpoint();
        Assert.Null(PlaytestCheckpoint.Compare(before, after, 0));
        fixture.ActorState++;
        Assert.Equal("actor.rac2.500.2", PlaytestCheckpoint.Compare(before, owner.CaptureCheckpoint(), 1)!.Field);
        fixture.ActorState--;
        fixture.Sources = [fixture.Sources[0] with { Sha256 = new string('F', 64) }];
        Assert.NotEqual(before.Values["obp.sources"].Text, owner.CaptureCheckpoint().Values["obp.sources"].Text);
    }

    [Fact]
    public void CommandsAndGameplayFactsUseTheExistingSessionInArrivalOrder()
    {
        var fixture = new Fixture(); var owner = fixture.Owner();
        owner.Initialize(new() { RecordPlaytest = "fixture", Destination = "rac2:LEVEL1" }, () => throw new Exception(), fixture.Install);
        PlaytestCommand[] commands = [new("equip", "rac2/item/30"), new("quick-slot", "", 7), new("gadget", "pack", Enabled: true),
            new("unequip", ""), new("destination", "obp:fusion-lab"), new("effects", "reset")];
        foreach (var command in commands) { owner.ApplyCommand(command); owner.RecordCommand(command); }
        Assert.Equal(new[] { "equip:rac2/item/30", "slot:7:", "gadget:pack:True", "unequip", "destination:obp:fusion-lab", "effects:reset" }, fixture.Actions);
        Assert.Equal(commands, fixture.Session.Commands);
        owner.RecordGameplayFact("shot", new { Source = "rac2", Sequence = 1 });
        owner.RecordGameplayFact("damage", new { Source = "rac3", Sequence = 1 });
        Assert.Equal(new[] { "shot", "damage" }, fixture.Session.Facts.Select(f => f.Kind));
        Assert.All(fixture.Session.Facts, fact => Assert.Equal(17, fact.Tick));
        fixture.Accept = false;
        Assert.Contains("unavailable", Assert.Throws<InvalidDataException>(() => owner.ApplyCommand(commands[0])).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => owner.ApplyCommand(new("effects", "invented")));
        Assert.Equal(7, fixture.Actions.Count);
        fixture.Ordinary = false; owner.RecordCommand(commands[0]); owner.RecordGameplayFact("shot", new { });
        Assert.Equal(commands, fixture.Session.Commands); Assert.Equal(2, fixture.Session.Facts.Count);
    }

    [Fact]
    public void ReadinessRetainsPreparationActorAndOptInEffectAdmission()
    {
        var fixture = new Fixture(); var owner = fixture.Owner();
        owner.Initialize(new() { RecordPlaytest = "fixture", RecordEffects = true, Destination = "rac2:LEVEL1" }, () => throw new Exception(), fixture.Install);
        fixture.Ready = fixture.Ready with { Preparing = true }; Assert.False(owner.WorldReady()); Assert.Equal(0, fixture.EffectPrepares);
        fixture.Ready = fixture.Ready with { Preparing = false }; fixture.EffectReady = new(false, null); Assert.False(owner.WorldReady());
        fixture.EffectReady = new(false, "source effect unavailable"); Assert.Throws<InvalidDataException>(() => owner.WorldReady());
        fixture.EffectReady = new(true, null); fixture.Ready = fixture.Ready with { ActorProblems = ["missing native context"] };
        Assert.Throws<InvalidOperationException>(() => owner.WorldReady());
        fixture.Ready = fixture.Ready with { ActorProblems = [], Lab = true, Placed = 1, Expected = 2 }; Assert.False(owner.WorldReady());
        fixture.Ready = fixture.Ready with { Placed = 2 }; Assert.True(owner.WorldReady());
        var header = owner.CaptureHeader(); Assert.Contains(header.Adapters, a => a.Id == EffectCaptureContract.AdapterPrefix + "fixture-effects");
        Assert.Equal(new double[] { 60 }, header.InitialState.Values["effects.tick"].Numbers);
    }

    [Fact]
    public void ReplacedOrMissingAttachmentsCannotPublishAnotherRecordingCheckpoint()
    {
        var fixture = new Fixture(); var owner = fixture.Owner();
        owner.Initialize(new() { RecordPlaytest = "fixture", Destination = "rac2:LEVEL1" }, () => throw new Exception(), fixture.Install);
        owner.CaptureHeader();
        fixture.Stamps = [fixture.Stamps[0] with { Written = 99 }];
        Assert.Throws<InvalidDataException>(owner.ValidateRecordingSources);
        Assert.Throws<InvalidDataException>(() => owner.CaptureCheckpoint());
        fixture.Stamps = []; Assert.Throws<InvalidDataException>(owner.ValidateRecordingSources);
    }

    [Fact]
    public void ControllerResetIsOnceAtInitialOrdinaryEntryAndNotAWorldTransitionReset()
    {
        var fixture = new Fixture(); var owner = fixture.Owner(); int resets = 0;
        owner.PrepareControllers(() => resets++); Assert.Equal(0, resets);
        owner.Initialize(new() { RecordPlaytest = "fixture", Destination = "rac2:LEVEL1" }, () => throw new Exception(), fixture.Install);
        fixture.Ordinary = false; owner.PrepareControllers(() => resets++); Assert.Equal(0, resets);
        fixture.Ordinary = true; owner.PrepareControllers(() => resets++); owner.PrepareControllers(() => resets++); Assert.Equal(1, resets);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("clock")]
    [InlineData("backend")]
    [InlineData("mode")]
    public async Task InvalidStartupFailsBeforeInstallingARecorderAndReportsInvalid(string change)
    {
        var fixture = new Fixture(); var recording = fixture.Owner();
        recording.Initialize(new() { RecordPlaytest = "fixture", Destination = "rac2:LEVEL1" }, () => throw new Exception(), fixture.Install);
        string path = Path.Combine(_directory, "run.jsonl.gz");
        using (var writer = new PlaytestRunWriter(path, recording.CaptureHeader())) await writer.FinishAsync("saved");
        var environment = change == "clock" ? Environment with { PhysicsHz = 50 } : change == "backend" ? Environment with { PhysicsEngine = "other" } : Environment;
        var replay = fixture.Owner(environment, change == "build" ? Build with { AssemblyId = Guid.NewGuid() } : Build);
        var args = new CommandLineArgs { ReplayPlaytest = path, RecordPlaytest = change == "mode" ? "both" : null };
        var error = Assert.Throws<InvalidDataException>(() => replay.Initialize(args, () => throw new Exception(), (_, _, _) => throw new Exception("Must not install")));
        Assert.Null(replay.Recorder); replay.WriteStartupFailure(error);
        using var result = JsonDocument.Parse(File.ReadAllBytes(path + ".report.json")); Assert.Equal("invalid", result.RootElement.GetProperty("Result").GetString());
    }

    [Fact]
    public async Task ReplayRestoresExactSeedProfileAndEquipmentAndAllowsOnlyExplicitBuildInvestigation()
    {
        var fixture = new Fixture(); var recording = fixture.Owner();
        recording.Initialize(new() { RecordPlaytest = "fixture", Destination = "rac2:LEVEL1", FixedSeed = 271 }, () => throw new Exception(), fixture.Install);
        string path = Path.Combine(_directory, "run.jsonl.gz"); var header = recording.CaptureHeader();
        using (var writer = new PlaytestRunWriter(path, header)) await writer.FinishAsync("saved");
        var replay = fixture.Owner(build: Build with { AssemblyId = Guid.NewGuid() });
        var args = replay.Initialize(new() { ReplayPlaytest = path, PlaytestAllowBuildChange = true, FixedSeed = 1 }, () => throw new Exception(), fixture.Install);
        Assert.Equal(271, args.FixedSeed); Assert.Equal(header.Destination, args.Destination); Assert.True(args.NoProfile);
        Assert.Equal(fixture.Profile.Encode(), replay.TakeStartingProfile()!.Encode()); Assert.Null(replay.TakeStartingProfile());
        replay.ValidateProvenance(header);
        fixture.Sources = [fixture.Sources[0] with { Sha256 = new string('F', 64) }];
        Assert.Throws<InvalidDataException>(() => replay.ValidateProvenance(header));
        fixture.Session.Reader?.Dispose();
    }

    private sealed class Fixture
    {
        internal bool Ordinary = true, Accept = true;
        internal int ActorState = 4, EffectPrepares;
        internal ObpProfile Profile = ObpProfileTests.Profile() with { Destination = "rac2:LEVEL1" };
        internal ObpBuildIdentity[] Sources = [new(ObpSourceGame.Rac2, "fixture-build", Sha256: new string('A', 64))];
        internal VerificationSourceStamp[] Stamps = [new(ObpSourceGame.Rac2, "fixture-build", "synthetic-private-source", 12, 34)];
        internal CaptureReadiness Ready = new(false, [], false, 0, 0, true);
        internal CaptureEffectReadiness EffectReady = new(true, null);
        internal List<string> Actions { get; } = [];
        internal Session Session { get; } = new();
        internal ICaptureReplaySession Install(CaptureReplayCoordinator _, PlaytestRunReader? reader, OBP.RAC1.Camera.Rac1CameraOptions? camera)
        { Session.Reader = reader; return Session; }
        private bool Action(string value) { Actions.Add(value); return Accept; }
        internal CaptureReplayCoordinator Owner(CaptureEnvironment? environment = null, CaptureBuildIdentity? build = null) => new(new(
            () => Ordinary, () => 17,
            () => new("rac2:LEVEL1", "fixture-build", 960, 720, Json(new { }), Json(OBP.RAC1.Camera.Rac1CameraOptions.Default), null, Profile),
            Snapshot, () => new(Profile.Player, Sources.Select(s => new ProfileSource(s.Game, s.BuildId, s.Sha256!)).ToArray(),
                [new(new(ObpSourceGame.Rac2, 500, 2), new(1, 2, 3), true, false, 7, ActorState, 6)], 17),
            () => Sources, () => ["actor/rac2/v1"], () => Ready,
            new(() => { EffectPrepares++; return EffectReady; }, () => "fixture-effects", values => values.Add("effects.tick", new([60]))),
            new(value => Action("destination:" + value), value => Action("equip:" + value), (value, enabled) => Action($"gadget:{value}:{enabled}"),
                (slot, value) => Action($"slot:{slot}:{value}"), () => Action("unequip"), value => Action("effects:" + value)), _ => true, () => Stamps), environment ?? Environment, build ?? Build);
        private static CaptureCheckpointSnapshot Snapshot() => new("rac2:LEVEL1", false, 960, 720, false, false, false, false, null, null,
            new(0, [0], [0], [0]), new(4, false, 12, 30, -1, "wrench", Json(new { Owned = 30 }), Json(new { })), null,
            new(new(8, 9, 10), Vector3.Zero, .5, .25, true, true, "Ground", "Idle", 0, 1, 60, "none", Json(new { }), Json(new { }), Vector3.Zero, Vector3.Zero),
            [], [], [], [0, 0, 0, 0, 0, 0], [], [], [], [], []);
    }

    private sealed class Session : ICaptureReplaySession
    {
        public PlaytestRunReader? Reader { get; set; }
        public bool WaitingForEntry => true;
        public bool PreparingWorld => false;
        public bool HasEntered => false;
        public bool Finished => false;
        public bool BugInputSuspended => false;
        public PlaytestRunWriter BugWriter => throw new NotSupportedException();
        internal List<PlaytestCommand> Commands { get; } = [];
        internal List<PlaytestGameplayFact> Facts { get; } = [];
        public void RecordSessionCommand(PlaytestCommand command) => Commands.Add(command);
        public void RecordGameplayFact(PlaytestGameplayFact fact) => Facts.Add(fact);
        public void WorldEntered() { }
        public void Save() { }
        public void RequestBookmark(string path) { }
        public bool RejectMovementLoad() => false;
        public void ResumeAfterBugOverlay() { }
        public void ShowBugNotice(string notice) { }
        public void SuspendBugCaptureForQuit() { }
        public Task PrepareBugCaptureQuitAsync() => Task.CompletedTask;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}

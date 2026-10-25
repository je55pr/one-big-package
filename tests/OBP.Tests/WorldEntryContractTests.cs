using OBP.Core;
using OBP.Core.Math;
using OBP.Fusion;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class WorldEntryContractTests
{
    [Theory]
    [InlineData(ObpSourceGame.Rac1)]
    [InlineData(ObpSourceGame.Rac2)]
    public void SyntheticWorldOriginsCannotChooseThePlayerProviderOrServices(ObpSourceGame selectedAvatar)
    {
        var session = new TestSession(Snapshot(selectedAvatar));
        var providers = new PlayerAvatarProviderRegistry(Enum.GetValues<ObpSourceGame>()
            .Select(game => new TestAvatarProvider(game)));
        foreach (var destination in ReconciliationGateTests.Matrix.GetProperty("Route").EnumerateArray())
        {
            string id = destination.GetString()!;
            var game = SourceIdentityWire.ParseWorld(id.StartsWith("obp:", StringComparison.Ordinal) ? id : id.Split(':')[0]);
            var plan = WorldEntryPlanning.Prepare(Request(id), new TestWorldSource(game), session, providers);
            Assert.Equal(game, plan.Prepared.World.Game);
            Assert.Same(session, plan.PlayerSession);
            Assert.Equal(session.State, plan.Player);
            Assert.Equal(selectedAvatar, plan.AvatarProvider.SourceGame);
            Assert.Same(providers.Get(selectedAvatar), plan.AvatarProvider);
            Assert.Equal(session.State.Configuration.Movement, plan.Player.Configuration.Movement);
            Assert.Equal(session.State.Configuration.Animation, plan.Player.Configuration.Animation);
            Assert.Equal(session.State.Configuration.Services, plan.Player.Configuration.Services);
            Assert.Equal(new SourceWeaponKey(ObpSourceGame.Rac2, 30), plan.Player.Loadout.SelectedWeapon);
            Assert.Null(plan.Player.Loadout.HandEquipment); // Wrench ownership survives a selected gun.
            Assert.Equal(WorldEntryMode.OrdinaryFusion, plan.Request.Mode);
        }
        Assert.Equal(0, session.Restores);
    }

    [Fact]
    public void MissingAvatarCannotSelectAnotherSourceProviderOrAHiddenCapsule()
    {
        var session = new TestSession(Snapshot());
        var source = new TestWorldSource(ObpSourceGame.Rac2);
        var providers = new PlayerAvatarProviderRegistry([new TestAvatarProvider(ObpSourceGame.Rac2)]);
        var error = Assert.Throws<InvalidOperationException>(() => WorldEntryPlanning.Prepare(Request(), source, session, providers));
        Assert.Contains("Selected avatar Rac1", error.Message);
        Assert.Equal(0, source.Preparations);
        Assert.Equal(0, session.Restores);
    }

    [Fact]
    public void FailedPreparationDoesNotRestoreOrReplaceThePersistentSession()
    {
        var session = new TestSession(Snapshot());
        var before = session.Capture();
        var source = new TestWorldSource(WorldContentIdentity.FusionLab) { Fail = true };
        Assert.Throws<InvalidDataException>(() => WorldEntryPlanning.Prepare(Request(), source, session, Providers()));
        Assert.Same(before, session.Capture());
        Assert.Equal(0, session.Restores);
    }

    [Fact]
    public void ActorKeysRetainSourceAndWorldGenerationWithoutInventingCapabilities()
    {
        ActorRegistration a = Actor(ObpSourceGame.Rac1), b = Actor(ObpSourceGame.Rac3); // Same local class and instance, different source.
        var source = new TestWorldSource(WorldContentIdentity.FusionLab) { Actors = [a, b] };
        var plan = WorldEntryPlanning.Prepare(Request(), source, new TestSession(Snapshot()), Providers());
        Assert.Equal(2, plan.Prepared.Actors.Count);
        Assert.NotEqual(a.Instance, b.Instance);
        Assert.Equal(CapabilityAdmission.Unimplemented, b.Capabilities[0].Admission);
        Assert.Throws<ArgumentException>(() => (a with { Source = b.Source }).Validate());
        Assert.Throws<ArgumentException>(() => (a with { Version = 99 }).Validate());
        Assert.Throws<ArgumentException>(() => (b with { Capabilities = [new("roam", CapabilityAdmission.Unimplemented)] }).Validate());
        source.Actors = [a, a];
        Assert.Throws<ArgumentException>(() => WorldEntryPlanning.Prepare(Request(), source, new TestSession(Snapshot()), Providers()));
        source.Actors = [a with { Instance = a.Instance with { Generation = 1 } }];
        Assert.Throws<ArgumentException>(() => WorldEntryPlanning.Prepare(Request(), source, new TestSession(Snapshot()), Providers()));
    }

    [Fact]
    public void SnapshotAndEntryVersionsFailClosedAndParityIsExplicit()
    {
        var session = new TestSession(Snapshot() with { Version = 99 });
        Assert.Throws<ArgumentException>(() => WorldEntryPlanning.Prepare(Request(), new TestWorldSource(ObpSourceGame.Rac1), session, Providers()));
        session.State = Snapshot();
        Assert.Throws<ArgumentException>(() => WorldEntryPlanning.Prepare(Request() with { Version = 99 }, new TestWorldSource(ObpSourceGame.Rac1), session, Providers()));
        var request = Request() with { Mode = WorldEntryMode.RetailParity };
        var plan = WorldEntryPlanning.Prepare(request, new TestWorldSource(ObpSourceGame.Rac1), session, Providers());
        Assert.Equal(WorldEntryMode.RetailParity, plan.Request.Mode);
        Assert.Equal(session.State.Configuration, plan.Player.Configuration);
    }

    private static WorldEntryRequest Request(string destination = "obp:fixture") => new(destination, "world-test", 0, WorldEntryMode.OrdinaryFusion);
    private static PlayerAvatarProviderRegistry Providers() => new([new TestAvatarProvider(ObpSourceGame.Rac1)]);
    private static ActorRegistration Actor(ObpSourceGame game) => new(new("world-test", 0, new(game, 500, 0)),
        new(game, "synthetic-v1", "authored-placement:0"), "test/actor-adapter/v1", null,
        [new("roam", CapabilityAdmission.Unimplemented, "Synthetic registration only; no native roaming controller.")]);

    private static PlayerSessionSnapshot Snapshot(ObpSourceGame avatar = ObpSourceGame.Rac1) => new(
        "player-1", 4,
        new(new(avatar, "synthetic-v1", "ratchet", "test-model"),
            new(ObpSourceGame.Rac1, "synthetic-v1", "test-movement"),
            new(ObpSourceGame.Rac1, "synthetic-v1", "test-animation"),
            new("test/input/v1", "test/attack/v1", "test/health/v1", "test/camera/v1",
                "test/hud/v1", "test/pause/v1", "test/quick-select/v1", "test/audio/v1"), 60),
        new(new(ObpSourceGame.Rac2, 30), null, [new(ObpSourceGame.Rac1, 10), new(ObpSourceGame.Rac2, 30), new(ObpSourceGame.Rac3, 43)]),
        new(4, 4, true), new Dictionary<string, long> { ["test/bolts"] = 12 });

    private sealed class TestSession(PlayerSessionSnapshot snapshot) : IObpPlayerSession
    {
        internal PlayerSessionSnapshot State = snapshot;
        internal int Restores;
        public PlayerSessionSnapshot Capture() => State;
        public void Restore(PlayerSessionSnapshot state) { State = state; Restores++; }
    }

    private sealed class TestWorldSource(WorldContentIdentity game) : IWorldEntrySource
    {
        internal bool Fail;
        internal int Preparations;
        internal IReadOnlyList<ActorRegistration> Actors = [];
        public PreparedWorldEntry Prepare(WorldEntryRequest request)
        {
            Preparations++;
            if (Fail) throw new InvalidDataException("Synthetic provider refused preparation.");
            var world = new RuntimeWorld(game, "synthetic-v1", 0, "Fixture", null, [], [], 0, [],
                new ObpBounds(Vec3.Zero, new(10, 10, 10)), null, null);
            return new(world, new(0, 0, 0, 0), Actors);
        }
    }

    private sealed class TestAvatarProvider(ObpSourceGame game) : IPlayerAvatarProvider
    {
        public ObpSourceGame SourceGame => game;
        public string BuildId => "synthetic-v1";
        public string DefaultAvatarId => "ratchet";
        public bool CanLoad(string avatarId) => avatarId == DefaultAvatarId;
        public PlayerAvatar Load(string sourcePath, string avatarId) => throw new NotSupportedException("No retail asset loading in synthetic contract tests.");
    }
}

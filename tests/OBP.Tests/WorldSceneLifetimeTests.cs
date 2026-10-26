using OBP.Core;
using OBP.Core.Math;
using OBP.Fusion;
using OBP.Game;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class WorldSceneLifetimeTests
{
    [Fact]
    public void RetiringAWorldCancelsItsWorkBeforeDisposingResourcesAndCannotCancelTheReplacement()
    {
        using var fixture = new Fixture();
        var old = fixture.Enter("rac1:LEVEL0");
        var lifetime = fixture.Scenes.Current!.Lifetime;
        bool canceled = false;
        using var subscription = lifetime.Register(() => { Assert.Equal(0, old.Disposals); canceled = true; });
        fixture.Enter("obp:fusion-lab");
        Assert.True(canceled);
        Assert.True(lifetime.IsCancellationRequested);
        Assert.Equal(1, old.Disposals);
        Assert.False(fixture.Scenes.Current!.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public void NativeAndAuthoredRoundTripsRetireResourcesOnceAndKeepThePlayer()
    {
        using var fixture = new Fixture();
        var before = fixture.Player.Capture();
        foreach (string id in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
        {
            var plan = fixture.Plan(id);
            var resources = new Resources();
            using (fixture.Scenes.Prepare(plan, null, null, .75f, () => resources))
                fixture.Entries.Commit(plan, fixture.Scenes);
            Assert.Same(resources, fixture.Scenes.Current!.Resources);
            Assert.Same(fixture.Player, fixture.Entries.Player);
            Assert.Equal(before.Configuration, fixture.Player.Capture().Configuration);
            Assert.Equal(before.Loadout.SelectedWeapon, fixture.Player.Capture().Loadout.SelectedWeapon);
            Assert.Equal(before.Loadout.HandEquipment, fixture.Player.Capture().Loadout.HandEquipment);
            Assert.Equal(before.Loadout.QuickSelect, fixture.Player.Capture().Loadout.QuickSelect);
            Assert.Equal(before.Health, fixture.Player.Capture().Health);
            Assert.Equal(0, resources.Disposals);
            Assert.Same(plan, fixture.Entries.Current);
        }
        fixture.Scenes.Retire();
        fixture.Scenes.Retire();
        Assert.Null(fixture.Scenes.Current);
        Assert.Null(fixture.Entries.Current);
        Assert.All(fixture.Activated, resource => Assert.Equal(1, resource.Disposals));
        Assert.All(fixture.Activated, resource => Assert.False(resource.HasBindings));
        Assert.Equal("rac1:LEVEL0", fixture.Scenes.DestinationId); // Only small navigation metadata survives.
    }

    [Fact]
    public void RejectedSourceRevalidationDisposesTheCandidateAndRetainsLiveResources()
    {
        using var fixture = new Fixture();
        var live = fixture.Enter("rac1:LEVEL0");
        var candidate = new Resources();
        Assert.Throws<InvalidDataException>((Action)(() =>
        {
            using var staged = fixture.Scenes.Prepare(fixture.Plan("rac2:LEVEL1"), null, null, 8, () => candidate);
            throw new InvalidDataException("Source stamp changed after staging.");
        }));
        Assert.Same(live, fixture.Scenes.Current!.Resources);
        Assert.Equal(0, live.Disposals);
        Assert.Equal(1, candidate.Disposals);
        Assert.Equal("rac1:LEVEL0", fixture.Scenes.DestinationId);
    }

    [Fact]
    public void FailedSceneFactoryDoesNotRetireTheLiveLease()
    {
        using var fixture = new Fixture();
        var live = fixture.Enter("rac1:LEVEL0");
        Assert.Throws<InvalidDataException>(() => fixture.Scenes.Prepare(fixture.Plan("rac3:TABLE1"), null, null, 8,
            () => throw new InvalidDataException("Invalid scene.")));
        Assert.Same(live, fixture.Scenes.Current!.Resources);
        Assert.Equal(0, live.Disposals);
    }

    [Fact]
    public void SessionChangeRejectsAStagedPlanBeforeDestructiveActivation()
    {
        using var fixture = new Fixture();
        var live = fixture.Enter("rac1:LEVEL0");
        var candidate = new Resources();
        var plan = fixture.Plan("obp:fusion-lab");
        using (fixture.Scenes.Prepare(plan, null, null, 8, () => candidate))
        {
            fixture.Player.Restore(fixture.Player.Capture());
            Assert.Throws<InvalidOperationException>(() => fixture.Entries.Commit(plan, fixture.Scenes));
        }
        Assert.Same(live, fixture.Scenes.Current!.Resources);
        Assert.Equal(0, live.Disposals);
        Assert.Equal(1, candidate.Disposals);
    }

    [Fact]
    public void PartialActivationCleansOldAndPartialResourcesWithoutRestoringThePlayer()
    {
        using var fixture = new Fixture();
        var live = fixture.Enter("rac1:LEVEL0");
        var player = fixture.Player.Capture();
        fixture.FailActivation = true;
        var partial = new Resources();
        var plan = fixture.Plan("rac2:LEVEL1");
        using (fixture.Scenes.Prepare(plan, null, null, 8, () => partial))
            Assert.Throws<InvalidOperationException>(() => fixture.Entries.Commit(plan, fixture.Scenes));
        Assert.Null(fixture.Scenes.Current);
        Assert.Null(fixture.Entries.Current);
        Assert.Equal(1, live.Disposals);
        Assert.Equal(1, partial.Disposals);
        Assert.False(partial.HasBindings);
        Assert.Equal(player.Configuration, fixture.Player.Capture().Configuration);
        Assert.Equal(player.Generation, fixture.Player.Capture().Generation);
        fixture.FailActivation = false;
        Assert.Equal(0, fixture.Enter("rac3:TABLE1").Disposals);
    }

    [Fact]
    public void BindingsCanReadRetiringWorldFactsAndAuthoredEntryIsLeaseLocal()
    {
        using var fixture = new Fixture();
        fixture.Enter("rac1:LEVEL0");
        var authored = new RuntimeSpawn(4, 5, 6, .7);
        fixture.Scenes.OverrideAuthoredEntry(authored);
        Assert.Equal(authored, fixture.Scenes.PlayerEntry);
        fixture.Enter("obp:fusion-lab");
        Assert.Contains("rac1:LEVEL0", fixture.RetiringDestinations);
        Assert.Equal(new RuntimeSpawn(1, 2, 3, .5), fixture.Scenes.PlayerEntry);
        fixture.Scenes.Retire();
        Assert.Null(fixture.Scenes.PlayerEntry);
        Assert.Throws<InvalidOperationException>(() => fixture.Scenes.OverrideAuthoredEntry(authored));
    }

    [Fact]
    public async Task NewPreparationAndDisposalCancelPendingEntryWithoutPublishingLateWork()
    {
        using var fixture = new Fixture();
        bool entered = false;
        fixture.Scenes.PrepareLater(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            entered = true;
        }, (_, _) => Assert.Fail("Owned cancellation is not failure."));
        var old = fixture.Scenes.PendingPreparation;
        fixture.Scenes.PrepareLater(_ => Task.CompletedTask, (_, _) => Assert.Fail("Unexpected failure."));
        await old;
        Assert.False(entered);
        fixture.Scenes.PrepareLater(token => Task.Delay(Timeout.InfiniteTimeSpan, token), (_, _) => Assert.Fail("Unexpected failure."));
        var closing = fixture.Scenes.PendingPreparation;
        fixture.Dispose();
        await closing;
        Assert.Throws<ObjectDisposedException>(() => fixture.Scenes.PrepareLater(_ => Task.CompletedTask, (_, _) => { }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailuresAreObservedAndLateFailuresAreMarkedSuperseded(bool superseded)
    {
        using var fixture = new Fixture();
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        (Exception Error, bool Superseded)? observed = null;
        fixture.Scenes.PrepareLater(_ => work.Task, (error, late) => observed = (error, late));
        var pending = fixture.Scenes.PendingPreparation;
        if (superseded) fixture.Scenes.CancelPreparation();
        var error = new InvalidDataException("Preparation failed.");
        work.SetException(error);
        await pending;
        Assert.Same(error, observed!.Value.Error);
        Assert.Equal(superseded, observed.Value.Superseded);
    }

    [Fact]
    public void DisposeRetiresBothUncommittedAndLiveResourcesExactlyOnce()
    {
        var fixture = new Fixture();
        var live = fixture.Enter("rac1:LEVEL0");
        var candidate = new Resources();
        using (fixture.Scenes.Prepare(fixture.Plan("rac2:LEVEL1"), null, null, 8, () => candidate))
        {
            fixture.Dispose();
            fixture.Dispose();
        }
        Assert.Equal(1, live.Disposals);
        Assert.Equal(1, candidate.Disposals);
        Assert.Null(fixture.Scenes.Current);
    }

    [Fact]
    public async Task AnUnrelatedCancellationIsReportedEvenIfNavigationWasAlsoCanceled()
    {
        using var fixture = new Fixture();
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? observed = null;
        fixture.Scenes.PrepareLater(_ => work.Task, (error, _) => observed = error);
        var pending = fixture.Scenes.PendingPreparation;
        fixture.Scenes.CancelPreparation();
        var unrelated = new OperationCanceledException(new CancellationToken(canceled: true));
        work.SetException(unrelated);
        await pending;
        Assert.Same(unrelated, observed);
    }

    private sealed class Fixture : IDisposable
    {
        public ObpPlayerSession Player { get; } = new(new("player", 0,
            new(new(ObpSourceGame.Rac1, "test", "ratchet", "fixture"),
                new(ObpSourceGame.Rac1, "test", "movement"), new(ObpSourceGame.Rac1, "test", "animation"),
                new("input", "attack", "health", "camera", "hud", "pause", "quick", "audio"), 60),
            new(new(ObpSourceGame.Rac2, 30), null, []), new(4, 4, true), new Dictionary<string, long>()));
        public WorldEntrySession Entries { get; }
        public WorldSceneLifetime<Resources> Scenes { get; }
        public List<Resources> Activated { get; } = [];
        public List<string> RetiringDestinations { get; } = [];
        public bool FailActivation;
        public Fixture()
        {
            Entries = new(Player);
            Scenes = new(() =>
            {
                if (Scenes!.Current is { } current)
                {
                    RetiringDestinations.Add(current.Plan.Request.DestinationId);
                    current.Resources.HasBindings = false;
                }
            }, entry =>
            {
                entry.Resources.HasBindings = true;
                Activated.Add(entry.Resources);
                if (FailActivation) throw new InvalidOperationException("Partial scene activation.");
            }, Entries.Unload);
        }
        public WorldEntryPlan Plan(string id) => Entries.Prepare(Entries.Request(id, WorldEntryMode.OrdinaryFusion),
            new Source(id.StartsWith("obp:", StringComparison.Ordinal) ? WorldContentIdentity.FusionLab :
                SourceIdentityWire.ParseWorld(id.Split(':')[0])), new([new AvatarProvider()]));
        public Resources Enter(string id)
        {
            var plan = Plan(id); var resources = new Resources();
            using (Scenes.Prepare(plan, null, null, 8, () => resources)) Entries.Commit(plan, Scenes);
            return resources;
        }
        public void Dispose() => Scenes.Dispose();
    }

    private sealed class Resources : IDisposable
    {
        public int Disposals;
        public bool HasBindings;
        public void Dispose() => Disposals++;
    }

    private sealed class Source(WorldContentIdentity game) : IWorldEntrySource
    {
        public PreparedWorldEntry Prepare(WorldEntryRequest request) => new(
            new(game, "test", 0, null, null, [], [], 0, [], new ObpBounds(Vec3.Zero, new(10, 10, 10)), null, null),
            new(1, 2, 3, .5), []);
    }

    private sealed class AvatarProvider : IPlayerAvatarProvider
    {
        public ObpSourceGame SourceGame => ObpSourceGame.Rac1;
        public string BuildId => "test";
        public string DefaultAvatarId => "ratchet";
        public bool CanLoad(string id) => id == DefaultAvatarId;
        public PlayerAvatar Load(string path, string id) => throw new NotSupportedException();
    }
}

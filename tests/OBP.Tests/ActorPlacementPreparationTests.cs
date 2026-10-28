using System.Collections.Concurrent;
using OBP.Core;
using OBP.Core.Math;
using OBP.Fusion;
using OBP.Game;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class ActorPlacementPreparationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromMinutes(2);
    private static ActorPlacement Placement(string id, ObpSourceGame game = ObpSourceGame.Rac1, int nativeClass = 749) =>
        new(id, new(game, "fixture", id), 0, nativeClass, null,
            new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]), "fixture/actor", "fixture/context", 0, "fixture");

    [Fact]
    public async Task WholeBatchActivatesInSourceClassAndIdOrderRegardlessOfImportCompletion()
    {
        await using var fixture = new Fixture();
        using var owner = fixture.Create();
        var placements = new[] { Placement("rac3", ObpSourceGame.Rac3, 500), Placement("z"),
            Placement("rac2", ObpSourceGame.Rac2, 2827), Placement("a"), Placement("lower-class", nativeClass: 500) };
        foreach (var placement in placements) { owner.Add(placement); owner.Prepare(placement); }
        fixture.Release(ObpSourceGame.Rac3); await fixture.Library(ObpSourceGame.Rac3).WaitAsync(Guard);
        owner.Poll(); Assert.Empty(fixture.Activated);
        fixture.Release(ObpSourceGame.Rac1); await fixture.Library(ObpSourceGame.Rac1).WaitAsync(Guard);
        owner.Poll(); Assert.Empty(fixture.Activated);
        fixture.Release(ObpSourceGame.Rac2); await fixture.Library(ObpSourceGame.Rac2).WaitAsync(Guard);
        owner.Poll(); owner.Poll();
        Assert.Equal(new[] { "lower-class", "a", "z", "rac2", "rac3" }, fixture.Activated);
        Assert.Equal(3, fixture.Imports);
        Assert.Empty(owner.Problems);
    }

    [Fact]
    public async Task UnsupportedContextAndMissingSourceFailBeforeImportOrConstruction()
    {
        await using var fixture = new Fixture();
        fixture.Versions.TryRemove(ObpSourceGame.Rac3, out _);
        using var owner = fixture.Create(admit: placement =>
        {
            if (placement.Id == "unsupported") throw new NotSupportedException("unrecovered native context");
        });
        foreach (var placement in new[] { Placement("unsupported"), Placement("missing", ObpSourceGame.Rac3) })
        { owner.Add(placement); owner.Prepare(placement); }
        owner.Poll();
        Assert.Equal(0, fixture.Imports);
        Assert.Empty(fixture.Activated);
        Assert.Equal("unrecovered native context", owner.Problems["unsupported"]);
        Assert.Contains("Missing or unsupported", owner.Problems["missing"]);
    }

    [Fact]
    public async Task SourceReplacementRejectsAlreadyPreparedOldDataAndExplicitRetryUsesTheNewVersion()
    {
        await using var fixture = new Fixture();
        using var owner = fixture.Create();
        var placement = Placement("actor"); owner.Add(placement); owner.Prepare(placement);
        fixture.Release(ObpSourceGame.Rac1); await fixture.Library(ObpSourceGame.Rac1).WaitAsync(Guard);
        fixture.Versions[ObpSourceGame.Rac1] = fixture.Version(ObpSourceGame.Rac1) with { Written = 2 };
        owner.Poll();
        Assert.Empty(fixture.Activated);
        Assert.Contains("changed before activation", owner.Problems[placement.Id]);
        owner.Retry(placement.Id);
        await fixture.Library(ObpSourceGame.Rac1).WaitAsync(Guard); owner.Poll();
        Assert.Equal(new[] { placement.Id }, fixture.Activated);
        Assert.Equal(2, fixture.ActivatedVersions.Single().Written);
        Assert.Equal(2, fixture.Imports);
        Assert.Empty(owner.Problems);
    }

    [Fact]
    public async Task RetiredWorldCannotActivateLateDataAndTheNextWorldReusesItsSourceLibrary()
    {
        await using var fixture = new Fixture();
        var placement = Placement("actor");
        using var first = fixture.Create(); first.Add(placement); first.Prepare(placement);
        var library = fixture.Library(ObpSourceGame.Rac1);
        Assert.Same(library, first.Prepared(placement.Id).Library);
        first.Dispose();
        fixture.Release(ObpSourceGame.Rac1); await library.WaitAsync(Guard); first.Poll();
        Assert.Empty(fixture.Activated);
        Assert.True(fixture.Cache.IsCurrentLibrary(fixture.Version(ObpSourceGame.Rac1), 0, library));
        using var next = fixture.Create(); next.Add(placement); next.Prepare(placement);
        Assert.Same(library, next.Prepared(placement.Id).Library);
        next.Prepare(placement);
        Assert.Same(library, next.Prepared(placement.Id).Library);
        next.Poll();
        Assert.Equal(new[] { placement.Id }, fixture.Activated);
        Assert.Equal(1, fixture.Imports);
    }

    [Fact]
    public async Task InvalidationRejectsPendingAndLateResultsAndObservingCacheDoesNotRestartIo()
    {
        await using var fixture = new Fixture();
        using var owner = fixture.Create();
        var placement = Placement("actor"); owner.Add(placement); owner.Prepare(placement);
        owner.Add(placement with { Id = "pending-without-import" });
        var library = fixture.Library(ObpSourceGame.Rac1);
        fixture.Cache.Clear(); owner.Invalidate();
        var status = owner.Problems;
        fixture.Release(ObpSourceGame.Rac1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.WaitAsync(Guard));
        owner.Poll();
        Assert.Empty(fixture.Activated);
        Assert.Equal(2, status.Count);
        Assert.False(fixture.Cache.IsCurrentLibrary(fixture.Version(ObpSourceGame.Rac1), 0, library));
        Assert.Equal(0, fixture.Cache.LibraryCount);
        owner.Retry(placement.Id); await fixture.Library(ObpSourceGame.Rac1).WaitAsync(Guard); owner.Poll();
        Assert.Equal(new[] { placement.Id }, fixture.Activated);
        Assert.Equal(2, status.Count); // Earlier projections remain snapshots after retry.
        Assert.Single(owner.Problems);
    }

    [Fact]
    public async Task FailedActivationRequiresExplicitRetryAndNeverDoubleActivates()
    {
        await using var fixture = new Fixture(); int attempts = 0;
        using var owner = fixture.Create(activate: (placement, _, source) =>
        {
            if (++attempts == 1) throw new IOException("construction rolled back");
            fixture.Activate(placement, source);
        });
        var placement = Placement("actor"); owner.Add(placement); owner.Prepare(placement);
        fixture.Release(ObpSourceGame.Rac1); await fixture.Library(ObpSourceGame.Rac1).WaitAsync(Guard);
        owner.Poll(); owner.Poll();
        Assert.Equal(1, attempts); Assert.Equal("construction rolled back", owner.Problems[placement.Id]);
        owner.Retry(placement.Id); owner.Poll();
        Assert.Equal(2, attempts); Assert.Single(fixture.Activated); Assert.Empty(owner.Problems);
        Assert.Equal(1, fixture.Imports);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly HostOperationScope _operations = new();
        private readonly Dictionary<ObpSourceGame, TaskCompletionSource> _release = [];
        private int _imports;
        internal readonly ConcurrentDictionary<ObpSourceGame, ActorSourceVersion> Versions = new();
        internal readonly List<string> Activated = [];
        internal readonly List<ActorSourceVersion> ActivatedVersions = [];
        private readonly HashSet<string> _placed = [];
        internal ActorSourceCache Cache { get; }
        internal int Imports => Volatile.Read(ref _imports);

        internal Fixture()
        {
            var importers = new Dictionary<ObpSourceGame, Func<string, int, RuntimeWorld>>();
            foreach (var game in new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 })
            {
                Versions[game] = new(game, "fixture", "source-" + game, 100, 1);
                _release[game] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                importers[game] = (_, level) =>
                {
                    Interlocked.Increment(ref _imports); _release[game].Task.GetAwaiter().GetResult();
                    return new(game, "fixture", level, null, null, [], [], 0, [], new ObpBounds(new(0, 0, 0), new(1, 1, 1)), null, null);
                };
            }
            Cache = new(_operations, importers, path => Versions.Values.FirstOrDefault(version => version.Path == path) is { } source
                ? (source.Length, source.Written) : null);
        }

        internal ActorSourceVersion Version(ObpSourceGame game) => Versions[game];
        internal Task<RuntimeWorld> Library(ObpSourceGame game) => Cache.Library(Version(game), 0);
        internal void Release(ObpSourceGame game) => _release[game].TrySetResult();
        internal void Activate(ActorPlacement placement, ActorSourceVersion source)
        { Activated.Add(placement.Id); ActivatedVersions.Add(source); _placed.Add(placement.Id); }
        internal ActorPlacementPreparation Create(Action<ActorPlacement>? admit = null,
            Action<ActorPlacement, RuntimeWorld, ActorSourceVersion>? activate = null) =>
            new(Cache, game => Versions.GetValueOrDefault(game), admit ?? (placement => placement.Validate()), _placed.Contains,
                activate ?? ((placement, _, source) => Activate(placement, source)), (_, _) => { });

        public async ValueTask DisposeAsync()
        {
            foreach (var release in _release.Values) release.TrySetResult();
            await _operations.StopAsync().WaitAsync(Guard);
        }
    }
}

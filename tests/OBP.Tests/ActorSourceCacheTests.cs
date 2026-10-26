using OBP.Core;
using OBP.Core.Math;
using OBP.Game;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class ActorSourceCacheTests
{
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromMinutes(2);
    private static ActorSourceVersion Source => new(ObpSourceGame.Rac2, "build", "source", 100, 1);
    private static RuntimeWorld World(ObpSourceGame game = ObpSourceGame.Rac2, string build = "build", int level = 0) =>
        new(game, build, level, null, null, [], [], 0, [], new ObpBounds(new(0, 0, 0), new(1, 1, 1)), null, null);
    private static ActorSourceCache Cache(HostOperationScope operations, Func<string, int, RuntimeWorld> import,
        Func<string, (long Length, long Written)?>? stamp = null) => new(operations,
            new Dictionary<ObpSourceGame, Func<string, int, RuntimeWorld>> { [ObpSourceGame.Rac2] = import }, stamp ?? (_ => (100, 1)));

    [Fact]
    public async Task RepeatedWorldLeasesReuseOnlyDecodedData()
    {
        using var operations = new HostOperationScope(); int imports = 0;
        var cache = Cache(operations, (_, level) => { Interlocked.Increment(ref imports); return World(level: level); });
        var first = cache.Library(Source, 0);
        _ = await first.WaitAsync(DeadlockGuard);
        using (var world = new OBP.Fusion.ActorRuntimeSet("world-one", 1)) { }
        using (var world = new OBP.Fusion.ActorRuntimeSet("world-two", 2))
        { Assert.Same(first, cache.Library(Source, 0)); Assert.Equal(0, world.Count); }
        Assert.Equal(1, imports);
        Assert.NotSame(first, cache.Library(Source, 1));
        await operations.StopAsync().WaitAsync(DeadlockGuard);
    }

    [Fact]
    public async Task FaultsAreObservedAndRetryDoesNotReuseAFailedImport()
    {
        using var operations = new HostOperationScope(); int imports = 0;
        var cache = Cache(operations, (_, _) => ++imports == 1 ? throw new InvalidDataException("missing native data") : World());
        var failed = cache.Library(Source, 0);
        await Assert.ThrowsAsync<InvalidDataException>(() => failed.WaitAsync(DeadlockGuard));
        var retried = cache.Library(Source, 0);
        Assert.NotSame(failed, retried);
        _ = await retried.WaitAsync(DeadlockGuard);
        await operations.StopAsync().WaitAsync(DeadlockGuard);
        Assert.Empty(operations.TakeFailures());
    }

    [Fact]
    public async Task ChangedVersionCancelsOldImportAndCannotPublishItsLateFailure()
    {
        using var operations = new HostOperationScope();
        using var release = new ManualResetEventSlim(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long written = 1; int imports = 0;
        var cache = Cache(operations, (_, _) =>
        {
            if (Interlocked.Increment(ref imports) == 1)
            { started.SetResult(); Assert.True(release.Wait(DeadlockGuard)); throw new InvalidDataException("retired import"); }
            return World();
        }, _ => (100, Interlocked.Read(ref written)));
        var retired = cache.Library(Source, 0);
        try
        {
            await started.Task.WaitAsync(DeadlockGuard);
            Interlocked.Exchange(ref written, 2);
            var replacement = cache.Library(Source with { Written = 2 }, 0);
            _ = await replacement.WaitAsync(DeadlockGuard);
            Assert.Equal(1, cache.LibraryCount);
            release.Set();
            await Assert.ThrowsAsync<InvalidDataException>(() => retired.WaitAsync(DeadlockGuard));
            Assert.Same(replacement, cache.Library(Source with { Written = 2 }, 0));
        }
        finally { release.Set(); await operations.StopAsync().WaitAsync(DeadlockGuard); }
        Assert.Empty(operations.TakeFailures());
    }

    [Fact]
    public async Task SourceRetirementAndRootShutdownDrainUninterruptibleNativeReads()
    {
        using var operations = new HostOperationScope(); using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = Cache(operations, (_, _) => { started.SetResult(); Assert.True(release.Wait(DeadlockGuard)); return World(); });
        var result = cache.Library(Source, 0);
        try
        {
            await started.Task.WaitAsync(DeadlockGuard);
            cache.Clear();
            var stopping = operations.StopAsync();
            Assert.False(stopping.IsCompleted);
            Assert.Equal(0, cache.LibraryCount);
            release.Set();
            await stopping.WaitAsync(DeadlockGuard);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.WaitAsync(DeadlockGuard));
            Assert.Equal(0, cache.LibraryCount);
        }
        finally { release.Set(); await operations.StopAsync().WaitAsync(DeadlockGuard); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("build")]
    [InlineData("level")]
    [InlineData("stamp")]
    public async Task ImportsMustKeepTheirAdmittedProvenance(string field)
    {
        using var operations = new HostOperationScope(); long written = 1;
        var cache = Cache(operations, (_, _) =>
        {
            if (field == "stamp") written = 2;
            return World(field == "source" ? ObpSourceGame.Rac1 : ObpSourceGame.Rac2,
                field == "build" ? "other" : "build", field == "level" ? 1 : 0);
        }, _ => (100, written));
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.Library(Source, 0).WaitAsync(DeadlockGuard));
        await operations.StopAsync().WaitAsync(DeadlockGuard);
    }

    [Fact]
    public void MissingProviderAndUnavailableSourceFailExplicitly()
    {
        using var operations = new HostOperationScope();
        var cache = Cache(operations, (_, _) => throw new Exception("must not import"));
        Assert.Throws<NotSupportedException>(() => { _ = cache.Library(Source with { Game = ObpSourceGame.Rac1 }, 0); });
        var missing = Cache(operations, (_, _) => throw new Exception("must not import"), _ => null);
        Assert.Throws<InvalidDataException>(() => { _ = missing.Library(Source, 0); });
        Assert.Equal(0, cache.LibraryCount); Assert.Equal(0, missing.LibraryCount);
    }

    [Fact]
    public void AnimationAssetsAreQualifiedBySourceBuildAttachmentLevelAndFamily()
    {
        using var operations = new HostOperationScope();
        var cache = Cache(operations, (_, _) => World()); int loads = 0;
        object Load() { loads++; return new object(); }
        var first = cache.Asset(Source, 0, 2827, "animation", Load);
        Assert.Same(first, cache.Asset(Source, 0, 2827, "animation", Load));
        Assert.NotSame(first, cache.Asset(Source, 1, 2827, "animation", Load));
        Assert.NotSame(first, cache.Asset(Source, 0, 500, "animation", Load));
        Assert.NotSame(first, cache.Asset(Source with { Game = ObpSourceGame.Rac1 }, 0, 2827, "animation", Load));
        Assert.Equal(4, loads);
        Assert.NotSame(first, cache.Asset(Source with { Build = "replacement" }, 0, 2827, "animation", Load));
        Assert.Equal(2, cache.AssetCount); // Replacement evicts only this source's old versions.
        cache.Clear(); Assert.Equal(0, cache.AssetCount);
    }
}

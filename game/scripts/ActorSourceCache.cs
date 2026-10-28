using OBP.Core;
using OBP.Runtime;

namespace OBP.Game;

public sealed record ActorSourceVersion(ObpSourceGame Game, string Build, string Path, long Length, long Written);

/// <summary>Attachment-owned decoded data. No scene, actor controller or world lease is retained here.</summary>
public sealed class ActorSourceCache(HostOperationScope operations,
    IReadOnlyDictionary<ObpSourceGame, Func<string, int, RuntimeWorld>> importers,
    Func<string, (long Length, long Written)?> readStamp)
{
    private readonly Dictionary<(ActorSourceVersion Source, int Level), Import> _imports = [];
    private readonly Dictionary<(ActorSourceVersion Source, int Level, int Class, string Kind), object> _assets = [];
    public int LibraryCount => _imports.Count;
    public int AssetCount => _assets.Count;

    /// <summary>Read-only cache witness; observing a retired world never starts another import.</summary>
    internal bool IsCurrentLibrary(ActorSourceVersion source, int level, Task<RuntimeWorld> expected) =>
        readStamp(source.Path) == (source.Length, source.Written) && _imports.TryGetValue((source, level), out var entry) &&
        ReferenceEquals(entry.Completion.Task, expected) && !expected.IsFaulted && !expected.IsCanceled;

    private sealed class Import
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _lifetime = new();
        private bool _finished;
        public CancellationToken Token { get; }
        public TaskCompletionSource<RuntimeWorld> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Import()
        {
            Token = _lifetime.Token;
            // A retired world may never read its result. The cache still observes every failed import.
            _ = Completion.Task.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        public void Cancel() { lock (_sync) { if (!_finished) _lifetime.Cancel(); } }
        public void Finish() { lock (_sync) { _finished = true; _lifetime.Dispose(); } }
    }

    public void Clear()
    {
        foreach (var import in _imports.Values) import.Cancel();
        _imports.Clear(); _assets.Clear();
    }

    public void RetainVersion(ActorSourceVersion source)
    {
        foreach (var key in _imports.Keys.Where(key => key.Source.Game == source.Game && key.Source != source).ToArray())
        { _imports[key].Cancel(); _imports.Remove(key); }
        foreach (var key in _assets.Keys.Where(key => key.Source.Game == source.Game && key.Source != source).ToArray())
            _assets.Remove(key);
    }

    public Task<RuntimeWorld> Library(ActorSourceVersion source, int level)
    {
        RequireCurrent(source);
        RetainVersion(source);
        var key = (source, level);
        if (_imports.TryGetValue(key, out var cached) && !cached.Completion.Task.IsFaulted && !cached.Completion.Task.IsCanceled)
            return cached.Completion.Task;
        if (!importers.TryGetValue(source.Game, out var import))
            throw new NotSupportedException("Unregistered actor source provider.");
        var entry = new Import();
        _imports[key] = entry;
        try
        {
            operations.Start("actor-source-import", async cancellation =>
            {
                try
                {
                    var world = await Task.Run(() =>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var result = import(source.Path, level);
                        cancellation.ThrowIfCancellationRequested();
                        RequireCurrent(source);
                        if (result.Game != source.Game || result.BuildId != source.Build || result.LevelId != level)
                            throw new InvalidDataException("Actor import returned different source provenance.");
                        return Slice(result);
                    }, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    entry.Completion.TrySetResult(world);
                }
                catch (OperationCanceledException error) when (error.CancellationToken == cancellation && cancellation.IsCancellationRequested)
                { entry.Completion.TrySetCanceled(cancellation); }
                catch (Exception error) { entry.Completion.TrySetException(error); }
                finally { entry.Finish(); }
            }, entry.Token);
        }
        catch { _imports.Remove(key); entry.Finish(); throw; }
        return entry.Completion.Task;
    }

    public T Asset<T>(ActorSourceVersion source, int level, int nativeClass, string kind, Func<T> load) where T : class
    {
        RequireCurrent(source);
        RetainVersion(source);
        var key = (source, level, nativeClass, kind);
        if (_assets.TryGetValue(key, out var asset)) return (T)asset;
        var result = load();
        RequireCurrent(source);
        _assets.Add(key, result);
        return result;
    }

    public void RequireCurrent(ActorSourceVersion source)
    {
        if (readStamp(source.Path) != (source.Length, source.Written))
            throw new InvalidDataException("Actor source changed during preparation. Reattach it before retrying.");
    }

    private static RuntimeWorld Slice(RuntimeWorld world)
    {
        var objects = (world.DynamicObjects ?? []).ToArray();
        var textures = objects.SelectMany(source => source.Meshes).Select(mesh => (mesh.AssetKind, mesh.TextureId)).ToHashSet();
        return world with
        {
            Meshes = [],
            CollisionMeshes = [],
            AnimatedMeshes = [],
            AmbientAnimations = [],
            DynamicObjects = objects,
            Textures = world.Textures.Where(texture => textures.Contains((texture.AssetKind, texture.TextureId))).ToArray(),
            Environment = null,
            Lighting = null,
            LevelAudio = [],
            RepresentativeAudioOneShot = null
        };
    }
}

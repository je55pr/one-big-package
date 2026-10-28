using System.Collections.Immutable;
using System.Collections.ObjectModel;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>One world's preparation barrier. Source data outlives this owner; activation never does.</summary>
internal sealed class ActorPlacementPreparation(ActorSourceCache cache,
    Func<ObpSourceGame, ActorSourceVersion?> source, Action<ActorPlacement> admit,
    Func<string, bool> placed, Action<ActorPlacement, RuntimeWorld, ActorSourceVersion> activate,
    Action<string, Exception> failure) : IDisposable
{
    private readonly Dictionary<string, ActorPlacement> _placements = [];
    private readonly Dictionary<string, Task<RuntimeWorld>> _preparation = [];
    private readonly Dictionary<string, ActorSourceVersion> _sources = [];
    private readonly Dictionary<string, string> _problems = [];
    private bool _retired;

    internal int Count => _placements.Count;
    internal bool Preparing => _preparation.Values.Any(task => !task.IsCompleted);
    internal IReadOnlyDictionary<string, string> Problems => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(_problems));
    internal IEnumerable<ActorPlacement> Placements => _placements.Values.ToArray();
    internal ActorPlacement? Find(string id) => _placements.GetValueOrDefault(id);
    internal (ActorPlacement Placement, Task<RuntimeWorld> Library, ActorSourceVersion Source) Prepared(string id) =>
        (_placements[id], _preparation[id], _sources[id]);

    internal void Add(ActorPlacement placement) => _placements.Add(placement.Id, placement);
    internal void Remove(string id) { _placements.Remove(id); _preparation.Remove(id); _sources.Remove(id); _problems.Remove(id); }

    internal void Prepare(ActorPlacement placement)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        try
        {
            // Unsupported recipes fail before IO or scene construction.
            admit(placement);
            var current = source(placement.Source.Game);
            if (current is null || current.Build != placement.Source.Build)
                throw new InvalidDataException("Missing or unsupported " + placement.Source.Game + " source.");
            _sources[placement.Id] = current;
            _preparation[placement.Id] = cache.Library(current, placement.LevelId);
        }
        catch (Exception error) { _problems[placement.Id] = error.Message; }
    }

    internal void Retry(string id)
    {
        _problems.Remove(id);
        Prepare(_placements[id]);
        Poll();
    }

    internal void Poll()
    {
        if (_retired || Preparing) return;
        foreach (var placement in _placements.Values.OrderBy(placement => placement.Source.Game)
            .ThenBy(placement => placement.NativeClassId).ThenBy(placement => placement.Id, StringComparer.Ordinal))
        {
            if (placed(placement.Id) || _problems.ContainsKey(placement.Id)) continue;
            try
            {
                var current = source(placement.Source.Game);
                if (current is null) { _problems[placement.Id] = "Source was detached before actor activation."; continue; }
                var task = _preparation.GetValueOrDefault(placement.Id);
                if (task is null || !task.IsCompleted) continue;
                var library = task.GetAwaiter().GetResult();
                var version = _sources[placement.Id];
                if (current != version || current.Build != placement.Source.Build)
                    throw new InvalidDataException("Actor source changed before activation. Reset after reattaching the source.");
                activate(placement, library, version);
            }
            catch (Exception error) { _problems[placement.Id] = error.Message; failure(placement.Id, error); }
        }
    }

    internal void Invalidate()
    {
        foreach (string id in _placements.Keys)
            _problems[id] = "Actor source attachments changed. Reset the encounter after source preparation.";
        _preparation.Clear(); _sources.Clear();
    }

    public void Dispose()
    {
        _retired = true;
        _preparation.Clear(); _sources.Clear(); _placements.Clear(); _problems.Clear();
    }
}

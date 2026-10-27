using System.Collections.Immutable;
using Godot;
using OBP.Fusion;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

internal sealed class GodotTrilogyActorDiagnosticBindings
{
    internal required Func<ActorWorldHost> World { get; init; }
    internal required Func<ActorSourceCache> Sources { get; init; }
    internal required Func<bool> Rac2Animation { get; init; }
    internal required Action Pump { get; init; }
    internal required Func<string, bool> Reset { get; init; }
    internal required Action ResetEncounter { get; init; }
    internal required Action ClearSources { get; init; }
    internal required Action ResetSharedAssets { get; init; }
    internal required Action<ActorPlacement> Prepare { get; init; }
    internal required Func<ActorInstanceKey, bool> DamageTarget { get; init; }
    internal required Func<Node3D> Scene { get; init; }
    internal required Func<int> Enemies { get; init; }
    internal required Func<int> Hostiles { get; init; }
    internal required Func<Rac1ActorBindings> FaultBindings { get; init; }
}
internal sealed class GodotTrilogyActorDiagnosticFixture(GodotTrilogyActorDiagnosticBindings bindings) : ITrilogyActorDiagnosticFixture
{
    private ActorWorldHost World => bindings.World();
    private Invalidation? _invalidation;
    public TrilogyActorPopulationDiagnostic Read() => new(World.Placements.Count, World.Placed.Count, World.Runtime?.Count ?? 0, World.Bodies.Count,
        bindings.Sources().LibraryCount, bindings.Sources().AssetCount, [.. World.Problems.Select(pair => pair.Key + ": " + pair.Value)], bindings.Rac2Animation());
    public ImmutableArray<TrilogyActorDiagnostic> ReadActors() => [.. World.Placed.Select(pair =>
        new TrilogyActorDiagnostic(pair.Key, pair.Value, World.Runtime?.Find(pair.Value.Target)?.Observe() ?? throw new DiagnosticUnavailableException("A placed native actor is unavailable.")))];
    public async Task PumpAsync(CancellationToken token) { await Task.Delay(10, token); bindings.Pump(); }
    public async Task<bool> RejectUnknownContextsAsync(CancellationToken token)
    {
        foreach (var placement in World.Placements.Values)
        {
            var library = await World.Preparation[placement.Id].WaitAsync(token); token.ThrowIfCancellationRequested();
            var source = World.Sources[placement.Id]; bool rejected = false;
            try { World.Factories!.Resolve(placement with { NativeContextId = "unrecovered-context" }).Create(placement, library, source); }
            catch (NotSupportedException) { rejected = true; }
            if (!rejected || World.Runtime?.Count != 3 || World.Bodies.Count != 3) return false;
        }
        return true;
    }
    public bool Reset(string id) => bindings.Reset(id);
    public bool HasRuntimeActor(ActorInstanceKey key) => World.Runtime?.Find(key.Target) is not null;
    public bool HasRewardOrAudio(ActorInstanceKey key) => World.Rewards.ContainsKey(key.Target) || World.Audio.ContainsKey(key.Target);
    public void ResetEncounter() => bindings.ResetEncounter();
    public ITrilogyActorCacheWitness CaptureCache()
    {
        var placement = World.Placements["arena-rac1-toad"]; var source = World.Sources[placement.Id];
        var task = bindings.Sources().Library(source, placement.LevelId); bindings.Prepare(placement);
        return new Cache(() => ReferenceEquals(task, World.Placements.ContainsKey(placement.Id) ? World.Preparation[placement.Id] : bindings.Sources().Library(source, placement.LevelId)));
    }
    public async Task<bool> VerifyConstructionRollbackAsync(CancellationToken token)
    {
        var placement = World.Placements["arena-rac1-toad"]; var source = World.Sources[placement.Id];
        var library = await bindings.Sources().Library(source, placement.LevelId).WaitAsync(token); token.ThrowIfCancellationRequested();
        int roots = bindings.Scene().GetChildCount(), enemies = bindings.Enemies(), hostiles = bindings.Hostiles(); bool rolledBack = false;
        var factory = new Rac1ToadActorFactory(new(World.Runtime!, bindings.Scene(), World.Bodies), bindings.Sources(), bindings.FaultBindings());
        try { factory.Create(placement, library, source); }
        catch (InvalidDataException error) when (error.Message == "actor construction fixture") { rolledBack = true; }
        return rolledBack && World.Runtime?.Count == 3 && World.Bodies.Count == 3 && bindings.Enemies() == enemies && bindings.Hostiles() == hostiles && bindings.Scene().GetChildCount() == roots;
    }
    public ITrilogyActorInvalidation Invalidate()
    {
        var placement = World.Placements["arena-rac1-toad"]; var retired = new Retirement(bindings);
        const string pending = "source-invalidation-pending-fixture"; World.Placements.Add(pending, placement with { Id = pending });
        bindings.ClearSources(); _invalidation = new(retired, World.Problems.ContainsKey(pending), () => { World.Placements.Remove(pending); World.Problems.Remove(pending); });
        return _invalidation;
    }
    public void Readmit()
    {
        _invalidation?.RemovePending();
        // False includes a valid asynchronous preparation. The scenario pumps
        // those tasks before checking the full population and placement problems.
        foreach (string id in World.Placements.Keys) _ = bindings.Reset(id);
    }
    public ITrilogyActorRetirementWitness CaptureRetirement() => new Retirement(bindings);
    public void ResetSharedAssets() => bindings.ResetSharedAssets();
    private sealed class Cache(Func<bool> reused) : ITrilogyActorCacheWitness { public bool Reused => reused(); }
    private sealed class Invalidation(ITrilogyActorRetirementWitness retired, bool pendingRejected, Action removePending) : ITrilogyActorInvalidation
    {
        private bool _removed;
        public ITrilogyActorRetirementWitness Retired => retired;
        public bool PendingRejected => pendingRejected;
        public void RemovePending() { if (!_removed) { _removed = true; removePending(); } }
        public void Dispose() => RemovePending();
    }
    private sealed class Retirement : ITrilogyActorRetirementWitness
    {
        private readonly GodotTrilogyActorDiagnosticBindings _bindings;
        private readonly ActorRuntimeSet? _runtime;
        private readonly ActorInstanceKey[] _actors;
        private readonly Node3D[] _roots;
        internal Retirement(GodotTrilogyActorDiagnosticBindings bindings)
        {
            _bindings = bindings; var world = bindings.World(); _runtime = world.Runtime; _actors = [.. world.Placed.Values]; _roots = [.. world.Views.Values.Select(actor => actor.Node.Root)];
        }
        public bool OldActorsRetired => _runtime?.Count == 0;
        public bool OldRootsFreed => _roots.Length > 0 && _roots.All(root => !GodotObject.IsInstanceValid(root));
        public bool DamageTargetsRemoved => _actors.Length > 0 && _actors.All(actor => !_bindings.DamageTarget(actor));
        public bool ReadmittedWithNewIdentities => _actors.Length > 0 && _bindings.World().Placed.Count == _actors.Length &&
            _bindings.World().Placed.Values.All(actor => !_actors.Contains(actor));
    }
}

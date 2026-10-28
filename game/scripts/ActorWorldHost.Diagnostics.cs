using System.Collections.Immutable;
using Godot;
using OBP.Fusion;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record ActorResourceSnapshot(int Registered, int Bodies, int SceneRoots, ActorNativeCounts Native);
internal sealed record ActorConstructionTrial(string PlacementId, string? Context = null, bool FailPickupBank = false);
internal sealed record ActorConstructionResult(string? ErrorType, string? Message, ActorResourceSnapshot Before, ActorResourceSnapshot After);
internal interface IActorCacheObservation { bool Reused { get; } }
internal interface IActorRetirementObservation
{
    bool OldActorsRetired { get; }
    bool OldRootsFreed { get; }
    bool DamageTargetsRemoved { get; }
    bool ReadmittedWithNewIdentities { get; }
}
internal interface IActorInvalidationObservation : IDisposable
{
    IActorRetirementObservation Retired { get; }
    bool PendingRejected { get; }
    void RemovePending();
}
internal interface IActorWorldDiagnostics
{
    ActorWorldSnapshot ReadSnapshot();
    ImmutableArray<PlacedActorSnapshot> ReadPlaced();
    void PollPreparation();
    EquipmentChangeResult ResetPlacement(string id);
    bool HasActor(ActorInstanceKey instance);
    bool HasTransient(ActorInstanceKey instance);
    IActorCacheObservation CaptureCache(string placementId);
    Task<ActorConstructionResult> TrialConstructionAsync(ActorConstructionTrial request, CancellationToken token);
    IActorInvalidationObservation InvalidateForDiagnostics(string templateId);
    void ResetPlacements();
    IActorRetirementObservation CaptureRetirement();
}

internal sealed partial class ActorWorldHost : IActorWorldDiagnostics
{
    internal IActorWorldDiagnostics Diagnostics => this;
    ActorWorldSnapshot IActorWorldDiagnostics.ReadSnapshot() => ReadSnapshot();
    void IActorWorldDiagnostics.PollPreparation() => PollPreparation();
    EquipmentChangeResult IActorWorldDiagnostics.ResetPlacement(string id) => ResetPlacement(id);

    ImmutableArray<PlacedActorSnapshot> IActorWorldDiagnostics.ReadPlaced() => [.. _placed.Select(pair =>
        new PlacedActorSnapshot(pair.Key, pair.Value, _runtime?.Find(pair.Value.Target)?.Observe() ??
            throw new InvalidOperationException("A placed native actor is unavailable.")))];

    bool IActorWorldDiagnostics.HasActor(ActorInstanceKey instance) => _runtime?.Find(instance.Target)?.Registration.Instance == instance;
    bool IActorWorldDiagnostics.HasTransient(ActorInstanceKey instance) => _rewards.ContainsKey(instance.Target) || _audio.ContainsKey(instance.Target);

    IActorCacheObservation IActorWorldDiagnostics.CaptureCache(string placementId)
    {
        var prepared = _preparation!.Prepared(placementId);
        var task = _sources.Library(prepared.Source, prepared.Placement.LevelId);
        _preparation.Prepare(prepared.Placement);
        return new CacheObservation(() => _sources.IsCurrentLibrary(prepared.Source, prepared.Placement.LevelId, task));
    }

    private ActorResourceSnapshot ReadResources() => new(_runtime?.Count ?? 0, _bodies.Count, _scene?.GetChildCount() ?? 0, _native.Counts);

    async Task<ActorConstructionResult> IActorWorldDiagnostics.TrialConstructionAsync(ActorConstructionTrial request, CancellationToken token)
    {
        var preparation = _preparation ?? throw new InvalidOperationException("No actor encounter is active.");
        var runtime = _runtime;
        var prepared = preparation.Prepared(request.PlacementId);
        var library = await prepared.Library.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_preparation, preparation) || !ReferenceEquals(_runtime, runtime))
            throw new InvalidOperationException("The actor construction world was retired.");
        var before = ReadResources(); string? type = null, message = null;
        try
        {
            var placement = request.Context is { } context ? prepared.Placement with { NativeContextId = context } : prepared.Placement;
            var factory = _factories!.Resolve(placement);
            if (request.FailPickupBank)
            {
                var trial = _native.ConstructionTrialFactory(new(_runtime!, _scene!, _bodies), _sources);
                var registry = new ActorAdapterRegistry<IActorFactory>(); registry.Register(trial.Descriptor, trial);
                factory = registry.Resolve(placement);
            }
            // A diagnostic construction can never transfer resources into the live registry.
            using var actor = factory.Create(placement, library, prepared.Source);
        }
        catch (Exception error) { type = error.GetType().Name; message = error.Message; }
        return new(type, message, before, ReadResources());
    }

    IActorInvalidationObservation IActorWorldDiagnostics.InvalidateForDiagnostics(string templateId)
    {
        var preparation = _preparation ?? throw new InvalidOperationException("No actor encounter is active.");
        var placement = preparation.Find(templateId) ?? throw new InvalidOperationException("No registered actor placement.");
        var retired = CaptureRetirement();
        const string pending = "source-invalidation-pending-fixture";
        preparation.Add(placement with { Id = pending });
        InvalidateSources();
        return new InvalidationObservation(retired, preparation.Problems.ContainsKey(pending), () =>
        {
            if (ReferenceEquals(_preparation, preparation)) preparation.Remove(pending);
        });
    }

    void IActorWorldDiagnostics.ResetPlacements()
    {
        foreach (var placement in _preparation?.Placements ?? []) _ = ResetPlacement(placement.Id);
    }

    private IActorRetirementObservation CaptureRetirement() => new RetirementObservation(this, _runtime,
        [.. _placed.Values], [.. _views.Values.Select(actor => actor.Node.Root)]);
    IActorRetirementObservation IActorWorldDiagnostics.CaptureRetirement() => CaptureRetirement();

    private sealed class CacheObservation(Func<bool> reused) : IActorCacheObservation { public bool Reused => reused(); }
    private sealed class InvalidationObservation(IActorRetirementObservation retired, bool rejected, Action removePending) : IActorInvalidationObservation
    {
        private bool _removed;
        public IActorRetirementObservation Retired => retired;
        public bool PendingRejected => rejected;
        public void RemovePending() { if (!_removed) { _removed = true; removePending(); } }
        public void Dispose() => RemovePending();
    }
    private sealed class RetirementObservation(ActorWorldHost host, ActorRuntimeSet? runtime, ActorInstanceKey[] actors, Node3D[] roots) : IActorRetirementObservation
    {
        public bool OldActorsRetired => runtime?.Count == 0;
        public bool OldRootsFreed => roots.Length > 0 && roots.All(root => !GodotObject.IsInstanceValid(root));
        public bool DamageTargetsRemoved => actors.Length > 0 && actors.All(actor => !host._ports.HasPlayerAttack(actor));
        public bool ReadmittedWithNewIdentities => actors.Length > 0 && host._placed.Count == actors.Length && host._placed.Values.All(actor => !actors.Contains(actor));
    }
}

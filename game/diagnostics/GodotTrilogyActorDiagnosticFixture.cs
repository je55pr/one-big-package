using System.Collections.Immutable;
using OBP.Fusion;

namespace OBP.Game.Diagnostics;

internal sealed class GodotTrilogyActorDiagnosticBindings
{
    /// <summary>Returns the same host's diagnostic facade throughout the fixture lifetime, including across awaits and world leases.</summary>
    internal required Func<IActorWorldDiagnostics> Actors { get; init; }
    internal required Action ResetEncounter { get; init; }
    internal required Action ResetSharedAssets { get; init; }
}
internal sealed class GodotTrilogyActorDiagnosticFixture(GodotTrilogyActorDiagnosticBindings bindings) : ITrilogyActorDiagnosticFixture
{
    private IActorWorldDiagnostics Actors => bindings.Actors();
    private Invalidation? _invalidation;

    public TrilogyActorPopulationDiagnostic Read()
    {
        var state = Actors.ReadSnapshot();
        return new(state.Expected, state.Placed, state.Registered, state.Bodies, state.Libraries, state.Assets,
            [.. state.Problems.Select(problem => problem.Id + ": " + problem.Message)], state.Rac2AnimationPresented);
    }
    public ImmutableArray<TrilogyActorDiagnostic> ReadActors() =>
        [.. Actors.ReadPlaced().Select(actor => new TrilogyActorDiagnostic(actor.Id, actor.Instance, actor.State))];
    public async Task PumpAsync(CancellationToken token) { await Task.Delay(10, token); Actors.PollPreparation(); }
    public async Task<bool> RejectUnknownContextsAsync(CancellationToken token)
    {
        foreach (var actor in Actors.ReadPlaced())
        {
            var result = await Actors.TrialConstructionAsync(new(actor.Id, Context: "unrecovered-context"), token);
            if (result.ErrorType != nameof(NotSupportedException) || result.Before != result.After ||
                result.After is not { Registered: 3, Bodies: 3 }) return false;
        }
        return true;
    }
    public bool Reset(string id) => Actors.ResetPlacement(id).Accepted;
    public bool HasRuntimeActor(ActorInstanceKey key) => Actors.HasActor(key);
    public bool HasRewardOrAudio(ActorInstanceKey key) => Actors.HasTransient(key);
    public void ResetEncounter() => bindings.ResetEncounter();
    public ITrilogyActorCacheWitness CaptureCache() => new Cache(Actors.CaptureCache("arena-rac1-toad"));
    public async Task<bool> VerifyConstructionRollbackAsync(CancellationToken token)
    {
        var result = await Actors.TrialConstructionAsync(new("arena-rac1-toad", FailPickupBank: true), token);
        return result.ErrorType == nameof(InvalidDataException) && result.Message == "actor construction fixture" &&
            result.Before == result.After && result.After is { Registered: 3, Bodies: 3 };
    }
    public ITrilogyActorInvalidation Invalidate() =>
        _invalidation = new(Actors.InvalidateForDiagnostics("arena-rac1-toad"));
    public void Readmit() { _invalidation?.RemovePending(); Actors.ResetPlacements(); }
    public ITrilogyActorRetirementWitness CaptureRetirement() => new Retirement(Actors.CaptureRetirement());
    public void ResetSharedAssets() => bindings.ResetSharedAssets();

    private sealed class Cache(IActorCacheObservation witness) : ITrilogyActorCacheWitness { public bool Reused => witness.Reused; }
    private sealed class Invalidation(IActorInvalidationObservation witness) : ITrilogyActorInvalidation
    {
        public ITrilogyActorRetirementWitness Retired { get; } = new Retirement(witness.Retired);
        public bool PendingRejected => witness.PendingRejected;
        internal void RemovePending() => witness.RemovePending();
        public void Dispose() => witness.Dispose();
    }
    private sealed class Retirement(IActorRetirementObservation witness) : ITrilogyActorRetirementWitness
    {
        public bool OldActorsRetired => witness.OldActorsRetired;
        public bool OldRootsFreed => witness.OldRootsFreed;
        public bool DamageTargetsRemoved => witness.DamageTargetsRemoved;
        public bool ReadmittedWithNewIdentities => witness.ReadmittedWithNewIdentities;
    }
}

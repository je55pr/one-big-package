using System.Collections.Immutable;
using OBP.Fusion;
using Godot;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

internal sealed class GodotTrilogyActorDiagnosticBindings
{
    /// <summary>Returns the same host's diagnostic facade throughout the fixture lifetime, including across awaits and world leases.</summary>
    internal required Func<IActorWorldDiagnostics> Actors { get; init; }
    internal required Action ResetEncounter { get; init; }
    internal required Action ResetSharedAssets { get; init; }
    internal required Func<Rac2WorldModule?> Rac2World { get; init; }
    internal required Func<ActorWorldHost> Combat { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<int> Wallet { get; init; }
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

    public async Task<bool> VerifyRac2CratesAsync(CancellationToken token)
    {
        var world = bindings.Rac2World() ?? throw new DiagnosticUnavailableException("No R&C2 crate world.");
        var player = bindings.Player() ?? throw new DiagnosticUnavailableException("No selected player.");
        var host = bindings.Combat();
        if (world.Crates.Count != 190 || host.ReadActors().Count(actor => actor.Registration.Instance.Target.NativeClassId == 500) != 190)
            return false;
        var crate = world.Crates.OrderByDescending(node => node.Root.GlobalPosition.Y).First();
        if (crate.Root.GetParent().FindChildren("moby-marker_*", "MeshInstance3D", true, false).Count != 0) return false;
        var target = ActorWorldHost.Target(crate);
        var geometry = host.Combat.ReadGeometry().Single(item => item.Key == target);
        var centre = geometry.Bounds.GetCenter();
        var from = centre + Vector3.Up * 2;
        var to = centre;
        var shot = new WeaponShotKey(FusionWeaponSession.Lancer, 1, 999);
        var query = new WeaponQuery(0, shot, WeaponQueryKind.Segment, new(from.X, from.Y, from.Z), new(to.X, to.Y, to.Z), .05f);
        if (!host.Combat.Trace(query, from).Any(hit => hit.Target == target)) throw new InvalidOperationException($"crate query missed: {target}, bounds={geometry.Bounds}, from={from}");
        int before = bindings.Wallet();
        var contact = new WeaponDamageIntent(shot, target, .5f, 0xDEADBEEF, "crate-contact-fixture");
        if (!host.Combat.Apply(contact) || world.ObserveCrate(crate).Alive || crate.Root.Visible) throw new InvalidOperationException("crate did not break through ordinary contact");
        if (crate.Root.GetChildren().OfType<StaticBody3D>().Any(body => body.CollisionLayer != 0)) return false;
        int pickups = world.CrateBolts.OutstandingNodes;
        if (pickups != 3 || host.Combat.Apply(contact) || world.CrateBolts.OutstandingNodes != pickups) return false;
        if (host.ReadReplayAdapters().Length != 190) return false;
        player.ApplyDevelopmentReset(crate.Root.GlobalPosition + Vector3.Right * .6f, snapToFloor: false);
        // Keep this trial on the same ordinary world scheduler. No direct grant
        // or test-only pickup collision path is used.
        for (int frame = 0; frame < 240 && world.CrateBolts.OutstandingNodes > 0; frame++)
        {
            token.ThrowIfCancellationRequested();
            await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        if (world.CrateBolts.OutstandingNodes != 0 || bindings.Wallet() != before + 11)
            throw new InvalidOperationException($"crate collection: remaining={world.CrateBolts.OutstandingNodes}, wallet={before}->{bindings.Wallet()}, player={player.GlobalPosition}, pickups={world.CrateBolts.DiagnosticState}");
        var wrenchCrate = world.Crates.Where(node => world.ObserveCrate(node).Alive)
            .OrderByDescending(node => node.Root.GlobalPosition.Y).First();
        var wrenchBox = host.Combat.ReadGeometry().Single(item => item.Key == ActorWorldHost.Target(wrenchCrate)).Bounds;
        var hand = wrenchBox.GetCenter() - Vector3.Right * .2f;
        var tip = wrenchBox.GetCenter() + Vector3.Right * .2f;
        var miss = Vector3.Up * 100;
        host.Combat.ResolveWrench(hand + miss, tip + miss, hand + miss, tip + miss, .01f, 1);
        if (!world.ObserveCrate(wrenchCrate).Alive) throw new InvalidOperationException("distant wrench broke a crate");
        host.Combat.ResolveWrench(hand, tip, hand, tip, .01f, 1);
        if (world.ObserveCrate(wrenchCrate).Alive || world.CrateBolts.OutstandingNodes != 3)
            throw new InvalidOperationException("ordinary wrench sweep failed to break a crate once");
        host.Combat.ResolveWrench(hand, tip, hand, tip, .01f, 1);
        if (world.CrateBolts.OutstandingNodes != 3) throw new InvalidOperationException("repeat wrench duplicated payout");
        player.ApplyDevelopmentReset(wrenchCrate.Root.GlobalPosition + Vector3.Right * .6f, snapToFloor: false);
        for (int frame = 0; frame < 240 && world.CrateBolts.OutstandingNodes > 0; frame++)
        {
            token.ThrowIfCancellationRequested();
            await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        if (world.CrateBolts.OutstandingNodes != 0 || bindings.Wallet() != before + 22)
            throw new InvalidOperationException("wrench-crate pickups did not credit the shared wallet");
        return true;
    }

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

using System.Collections.Immutable;
using OBP.Fusion;
using Godot;
using OBP.Runtime.Gameplay;
using OBP.Godot;
using System.Security.Cryptography;

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
        VerifyCrateAdmission(world, host);
        int before = bindings.Wallet();
        var crate = world.Crates.OrderByDescending(node => node.Root.GlobalPosition.Y).First();
        VerifyCrateContact(world, host, crate);
        await CollectCrateAsync(world, player, crate, before + 11, token);
        var wrenchCrate = VerifyCrateWrench(world, host);
        await CollectCrateAsync(world, player, wrenchCrate, before + 22, token);
        return true;
    }

    private static void VerifyCrateAdmission(Rac2WorldModule world, ActorWorldHost host)
    {
        int registered = host.ReadActors().Count(actor => actor.Registration.Instance.Target.NativeClassId == 500);
        if (world.Crates.Count != 190 || registered != 190)
            throw new InvalidOperationException($"crate admission: world={world.Crates.Count}, actors={registered}, expected=190");
        int markers = world.Crates[0].Root.GetParent().FindChildren("moby-marker_*", "MeshInstance3D", true, false).Count;
        if (markers != 0) throw new InvalidOperationException($"ordinary marker meshes: {markers}, expected=0");
        int adapters = host.ReadReplayAdapters().Length;
        if (adapters != 190) throw new InvalidOperationException($"crate replay adapters: {adapters}, expected=190");
    }

    private static void VerifyCrateContact(Rac2WorldModule world, ActorWorldHost host, RuntimeWorldScene.DynamicObjectNode crate)
    {
        var target = ActorWorldHost.Target(crate);
        var geometry = host.Combat.ReadGeometry().Single(item => item.Key == target);
        var centre = geometry.Bounds.GetCenter();
        var from = centre + Vector3.Up * 2;
        var to = centre;
        var shot = new WeaponShotKey(FusionWeaponSession.Lancer, 1, 999);
        var query = new WeaponQuery(0, shot, WeaponQueryKind.Segment, new(from.X, from.Y, from.Z), new(to.X, to.Y, to.Z), .05f);
        if (!host.Combat.Trace(query, from).Any(hit => hit.Target == target)) throw new InvalidOperationException($"crate query missed: {target}, bounds={geometry.Bounds}, from={from}");
        var contact = new WeaponDamageIntent(shot, target, .5f, 0xDEADBEEF, "crate-contact-fixture");
        if (!host.Combat.Apply(contact) || world.ObserveCrate(crate).Alive || crate.Root.Visible) throw new InvalidOperationException("crate did not break through ordinary contact");
        int colliders = crate.Root.GetChildren().OfType<StaticBody3D>().Count(body => body.CollisionLayer != 0);
        if (colliders != 0) throw new InvalidOperationException($"broken crate collision bodies: {colliders}, expected=0");
        int pickups = world.CrateBolts.OutstandingNodes;
        VerifyPresentedPickups(world);
        bool repeated = host.Combat.Apply(contact);
        if (pickups != 3 || repeated || world.CrateBolts.OutstandingNodes != pickups)
            throw new InvalidOperationException($"crate duplicate gate: pickups={pickups}->{world.CrateBolts.OutstandingNodes}, repeated={repeated}; expected=3->3/false");
    }

    private static void VerifyPresentedPickups(Rac2WorldModule world)
    {
        var nodes = world.CrateBolts.PickupNodes;
        if (nodes.Count(node => node.Name.ToString().EndsWith("_class14", StringComparison.Ordinal)) != 2 ||
            nodes.Count(node => node.Name.ToString().EndsWith("_class13", StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException("crate pickup identities did not match 5/5/1 => classes 14/14/13");
        foreach (var node in nodes)
            VerifyPresentedPickup(node);
    }

    private static void VerifyPresentedPickup(Node3D node)
    {
        var meshes = node.GetChildren().OfType<MeshInstance3D>().ToArray();
        int expectedFaces = node.Name.ToString().EndsWith("_class13", StringComparison.Ordinal) ? 52 : 48;
        if (meshes.Any(mesh => mesh.Mesh is not ArrayMesh) || meshes.Sum(mesh => mesh.Mesh.GetFaces().Length / 3) != expectedFaces)
            throw new InvalidOperationException($"pickup {node.Name} did not present its original {expectedFaces}-triangle model");
        string[] expectedHashes = expectedFaces == 52
            ? ["FB5727C250A081F005BFA5A17BF99A0DAF44C5A5F2424A2918317D1A4F3A6617",
                "76C3B3017261FAEA873E612F3BE5AB2A172F51BF5AB5C49C1B65FFF7F38A0C70"]
            : ["AC4EB53E8608B337921069496D7E0763E2C4FCD4F05CCE4C584BCD6870B1EB5D"];
        string[] hashes = meshes.Select(PresentedTextureHash).ToArray();
        if (!expectedHashes.SequenceEqual(hashes))
            throw new InvalidOperationException($"pickup {node.Name} has unexpected material textures: {string.Join(',', hashes)}");
    }

    private static string PresentedTextureHash(MeshInstance3D mesh)
    {
        using var image = (mesh.MaterialOverride as StandardMaterial3D)?.AlbedoTexture?.GetImage()
            ?? throw new InvalidOperationException($"pickup {mesh.Name} has no original albedo texture");
        image.ClearMipmaps();
        return Convert.ToHexString(SHA256.HashData(image.GetData()));
    }

    private async Task CollectCrateAsync(Rac2WorldModule world, PlayerHost player, RuntimeWorldScene.DynamicObjectNode crate,
        int expectedWallet, CancellationToken token)
    {
        player.ApplyDevelopmentReset(crate.Root.GlobalPosition + Vector3.Right * .6f, snapToFloor: false);
        // Keep this trial on the same ordinary world scheduler. No direct grant
        // or test-only pickup collision path is used.
        for (int frame = 0; frame < 240 && world.CrateBolts.OutstandingNodes > 0; frame++)
        {
            token.ThrowIfCancellationRequested();
            await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        if (world.CrateBolts.OutstandingNodes != 0 || bindings.Wallet() != expectedWallet)
            throw new InvalidOperationException($"crate collection: remaining={world.CrateBolts.OutstandingNodes}, wallet={bindings.Wallet()}, expected=0/{expectedWallet}, player={player.GlobalPosition}, pickups={world.CrateBolts.DiagnosticState}");
    }

    private static RuntimeWorldScene.DynamicObjectNode VerifyCrateWrench(Rac2WorldModule world, ActorWorldHost host)
    {
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
        return wrenchCrate;
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

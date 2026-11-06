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
        if (world.CrateEffects.PickupSounds != 6 || world.CrateEffects.LastBankSound != 53)
            throw new InvalidOperationException("native Bolt arrival sound was not accepted by the audio host");
        await VerifyCrateStackAsync(world, host, player, token);
        return true;
    }

    private static async Task VerifyCrateStackAsync(Rac2WorldModule world, ActorWorldHost host, PlayerHost player, CancellationToken token)
    {
        var live = world.Crates.Where(node => world.ObserveCrate(node).Alive).ToArray();
        var pair = (from lower in live
                    from upper in live
                    let offset = upper.Root.GlobalPosition - lower.Root.GlobalPosition
                    where MathF.Abs(offset.X) < .1f && MathF.Abs(offset.Z) < .1f && MathF.Abs(offset.Y - 1) < .05f
                    orderby lower.Root.GlobalPosition.Y
                    select (lower, upper)).First();
        float floor = pair.lower.Root.GlobalPosition.Y;
        float start = pair.upper.Root.GlobalPosition.Y;
        int sounds = world.CrateEffects.BreakSounds;
        var shot = new WeaponShotKey(FusionWeaponSession.Lancer, 1, 1000);
        if (!host.Combat.Apply(new(shot, ActorWorldHost.Target(pair.lower), .5f, 0xDEADBEEF, "crate-stack-fixture")))
            throw new InvalidOperationException("stack support did not break through ordinary contact");
        if (world.CrateEffects.BreakSounds != sounds + 1 || world.CrateEffects.LastBankSound != 69)
            throw new InvalidOperationException("native crate break sound was not accepted by the audio host");
        var pieces = world.CrateEffects.FragmentNodes.Where(node => node.Name.ToString().StartsWith($"Rac2CrateFragment_{pair.lower.Source.InstanceIndex}_", StringComparison.Ordinal)).ToArray();
        if (pieces.Length != 3) throw new InvalidOperationException($"stack break emitted {pieces.Length} original parts, expected 3");
        foreach (var piece in pieces)
        {
            int triangles = piece.GetChildren().OfType<MeshInstance3D>().Sum(mesh => mesh.Mesh.GetFaces().Length / 3);
            int expected = piece.Name.ToString().EndsWith("_0", StringComparison.Ordinal) ? 32 : 20;
            if (triangles != expected || piece.GetChildren().OfType<MeshInstance3D>().Any(mesh => mesh.MaterialOverride is not StandardMaterial3D { AlbedoTexture: not null }))
                throw new InvalidOperationException($"fragment {piece.Name} did not present its original textured model");
        }
        var initialPiece = pieces[0].GlobalTransform;
        for (int frame = 0; frame < 60; frame++)
        {
            token.ThrowIfCancellationRequested();
            await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        float settled = pair.upper.Root.GlobalPosition.Y;
        if (settled > start - .8f || MathF.Abs(settled - floor) > .08f || MathF.Abs(world.CrateEffects.HeightVelocity(pair.upper.Source.InstanceIndex) ?? 1) > 1e-6f)
            throw new InvalidOperationException($"unsupported stack did not settle: {start}->{settled}, floor={floor}, velocity={world.CrateEffects.HeightVelocity(pair.upper.Source.InstanceIndex)}");
        if (Math.Abs(pair.upper.State.Presentation.Transform.Matrix[13] - settled) > 1e-6)
            throw new InvalidOperationException("falling crate presentation state did not follow its body");
        if (pieces[0].GlobalPosition.DistanceTo(initialPiece.Origin) < .05f || pieces[0].GlobalBasis.IsEqualApprox(initialPiece.Basis))
            throw new InvalidOperationException("original fragments did not move and spin");
        var target = ActorWorldHost.Target(pair.upper);
        var geometry = host.Combat.ReadGeometry().Single(item => item.Key == target);
        var centre = geometry.Bounds.GetCenter();
        var from = centre + Vector3.Up * 2;
        if (!host.Combat.Trace(new(0, shot, WeaponQueryKind.Segment, new(from.X, from.Y, from.Z), new(centre.X, centre.Y, centre.Z), .05f), from).Any(hit => hit.Target == target))
            throw new InvalidOperationException("fallen crate combat geometry did not follow its body");
        for (int frame = 0; frame < 800 && world.CrateEffects.Fragments > 0; frame++)
        {
            token.ThrowIfCancellationRequested();
            await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        if (world.CrateEffects.Fragments != 0) throw new InvalidOperationException("crate fragments did not expire");
        GD.Print($"[rac2-crate-physics] stack={start:0.###}->{settled:0.###}, original-parts=3, break-bank=69, pickup-bank=53, fragments-retired=true");
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

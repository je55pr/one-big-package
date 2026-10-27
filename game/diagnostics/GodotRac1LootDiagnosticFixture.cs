using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1LootDiagnosticBindings
{
    internal required Func<Rac1LootDiagnosticState> Read { get; init; }
    internal required Func<int> Pending { get; init; }
    internal required Func<Rac1PlacedBoltSession> Placed { get; init; }
    internal required Func<IReadOnlyDictionary<int, RuntimeWorldScene.DynamicObjectNode>> PlacedNodes { get; init; }
    internal required Func<IDictionary<int, Rac1BoltMotionSession>> Motions { get; init; }
    internal required Func<IReadOnlyDictionary<int, Node3D>> PickupNodes { get; init; }
    internal required Func<Rac1PickupRandom> Random { get; init; }
    internal required Func<Vector3, NVector3> ToNative { get; init; }
    internal required Func<NVector3, Vector3> FromNative { get; init; }
    internal required Func<System.Numerics.Quaternion, Quaternion> Orientation { get; init; }
    internal required Func<int, bool> CrateBody { get; init; }
    internal required Func<RuntimeWorldScene.DynamicObjectNode, bool> Strike { get; init; }
    internal required Action Damage { get; init; }
}
internal sealed class GodotRac1LootDiagnosticFixture(GodotRac1LootDiagnosticBindings bindings,
    GodotRac1WeaponTargetFixture targets, IDiagnosticWorldObservations worlds) : IRac1LootDiagnosticFixture
{
    public Rac1LootDiagnosticState Read() => bindings.Read();
    public int PendingPlacedBolts => bindings.Pending();
    private ActorInstanceKey Key(Rac1PlacedBoltSession.Registration registration)
    {
        var world = worlds.ReadWorld();
        if (string.IsNullOrWhiteSpace(world.WorldInstanceId)) throw new DiagnosticUnavailableException("The placed-bolt fixture world is unavailable.");
        return new(world.WorldInstanceId, world.Generation, new(ObpSourceGame.Rac1, registration.Instance.Key.NativeClassId, registration.Instance.Key.InstanceIndex));
    }
    private Rac1PlacedBoltSession.Registration Resolve(ActorInstanceKey actor)
    {
        Rac1DiagnosticActorScope.RequireCurrent(actor, worlds.ReadWorld());
        return bindings.Placed().Registrations.SingleOrDefault(item => item.Instance.Key.NativeClassId == actor.Target.NativeClassId && item.Instance.Key.InstanceIndex == actor.Target.InstanceIndex)
            ?? throw new DiagnosticUnavailableException("The authored placed-bolt registration is unavailable.");
    }
    public ImmutableArray<Rac1PlacedBoltPlacement> ReadPlacedBolts() => [.. bindings.Placed().Registrations.Select(item =>
        new Rac1PlacedBoltPlacement(Key(item), item.PickupId, item.Authored.NativeClassId, item.Instance.IsActive,
            item.Authored.PreserveAuthoredHeight, item.Authored.Position,
            bindings.Motions().TryGetValue(item.PickupId, out var motion) ? motion.Position.Z : null,
            bindings.PlacedNodes().TryGetValue(item.PickupId, out var node) && bindings.PickupNodes().TryGetValue(item.PickupId, out var pickup) && ReferenceEquals(node.Root, pickup)))];
    public IRac1PlacedBoltDiagnosticWitness CapturePlacedBolt(ActorInstanceKey actor)
    {
        var item = Resolve(actor);
        var node = bindings.PlacedNodes().GetValueOrDefault(item.PickupId) ?? throw new DiagnosticUnavailableException("The authored placed-bolt node is unavailable.");
        if (!bindings.Motions().TryGetValue(item.PickupId, out var motion)) throw new DiagnosticUnavailableException("The authored placed-bolt motion is unavailable.");
        return new PlacedWitness(new(worlds), () =>
        {
            // Staging replaces the motion, while arrival removes it. Retain the final motion after removal.
            if (bindings.Motions().TryGetValue(item.PickupId, out var current)) motion = current;
            bool valid = GodotObject.IsInstanceValid(node.Root);
            return new(item.PickupId, motion.NativeState, bindings.Placed().IsCollected(item.PickupId), item.Instance.IsActive,
                valid ? GodotMovementPlayer.Copy(node.Root.GlobalPosition) : GodotMovementPlayer.Copy(bindings.FromNative(motion.Position)),
                valid ? new(node.Root.Quaternion.X, node.Root.Quaternion.Y, node.Root.Quaternion.Z, node.Root.Quaternion.W) : default,
                valid ? GodotMovementPlayer.Copy(node.Root.Scale) : default, valid && node.Root.Visible,
                item.Instance.EntityState.Presentation.Presence, bindings.PickupNodes().ContainsKey(item.PickupId));
        });
    }
    public void StagePlacedBolt(ActorInstanceKey actor, NVector3 floor)
    {
        var item = Resolve(actor); var point = GodotMovementPlayer.Engine(floor);
        var authored = item.Authored with { Position = bindings.ToNative(point + Vector3.Up), PreserveAuthoredHeight = false };
        var motion = Rac1BoltMotionSession.FromPlaced(authored, bindings.Random(), new(bindings.ToNative(point), NVector3.UnitZ));
        bindings.Motions()[item.PickupId] = motion;
        var root = bindings.PickupNodes().GetValueOrDefault(item.PickupId) ?? throw new DiagnosticUnavailableException("The staged placed-bolt node is unavailable.");
        root.GlobalPosition = bindings.FromNative(motion.Position); root.Quaternion = bindings.Orientation(motion.Orientation);
        root.Scale = Vector3.One * motion.VisualScale;
    }
    public bool CrateHasBody(ActorInstanceKey actor) { targets.Read(actor); return bindings.CrateBody(actor.Target.InstanceIndex); }
    public bool StrikeCrate(ActorInstanceKey actor) { targets.Read(actor); return bindings.Strike(targets.ResolveNode(actor)); }
    public void InjectRetainedClass749Damage() => bindings.Damage();
    private sealed class PlacedWitness(GodotRac1DiagnosticWorldScope scope, Func<Rac1PlacedBoltDiagnostic> read) : IRac1PlacedBoltDiagnosticWitness
    { public Rac1PlacedBoltDiagnostic Read() { scope.RequireCurrent(); return read(); } }
}

/// <summary>Existing synthetic placement seam, shared with the earlier combat/weapon fixture bindings.</summary>
internal static class GodotRac1CrateStaging
{
    internal static void Place(RuntimeWorldScene.DynamicObjectNode crate, Vector3 position,
        IDictionary<int, (Node3D Node, Rac1CrateFallSession Motion, float FootOffset)> falling)
    {
        crate.Root.GlobalPosition = position;
        if (falling.TryGetValue(crate.Source.InstanceIndex, out var fall))
            falling[crate.Source.InstanceIndex] = (crate.Root, new(position.Y + fall.FootOffset), fall.FootOffset);
    }
}

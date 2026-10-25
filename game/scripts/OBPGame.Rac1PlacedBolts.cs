using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1PlacedBoltSession _rac1PlacedBolts = new(new());
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1PlacedBoltNodes = [];
    private readonly List<(Rac1PlacedBoltSession.Registration Registration, RuntimeWorldScene.DynamicObjectNode Node)> _rac1PendingPlacedBolts = [];
    private ulong _rac1PlacedBoltProbeAfter;

    private void ResetRac1PlacedBoltPresentation()
    {
        // The original world owns these roots. Free only dynamically emitted
        // effects in the ordinary pickup teardown, not authored objects twice.
        foreach (int id in _rac1PlacedBoltNodes.Keys)
        {
            _rac1PickupNodes.Remove(id);
            _rac1PickupMotion.Remove(id);
        }
        _rac1PlacedBoltNodes.Clear();
        _rac1PendingPlacedBolts.Clear();
    }

    private void ConfigureRac1PlacedBolts(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        foreach (var source in (world.DynamicObjects ?? []).Where(source => Rac1PlacedBolt.IsBoltClass(source.NativeClassId)))
        {
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("R&C1 authored bolt is missing its retail presentation.");
            var registration = _rac1PlacedBolts.Register(source, node.State);
            if (!registration.Instance.IsActive)
            {
                node.ApplyState(registration.Instance.EntityState);
                continue;
            }
            _rac1PlacedBoltNodes.Add(registration.PickupId, node);
            _rac1PendingPlacedBolts.Add((registration, node));
        }
        // Scene collision shapes must reach the physics server before the
        // native initialization ray can be represented by a Godot query.
        _rac1PlacedBoltProbeAfter = Engine.GetPhysicsFrames() + 2;
        GD.Print($"[rac1-pickups] registered {_rac1PendingPlacedBolts.Count} authored world bolts");
    }

    private void InitializeRac1PlacedBolts()
    {
        if (_player is null || Engine.GetPhysicsFrames() < _rac1PlacedBoltProbeAfter) return;
        foreach (var pending in _rac1PendingPlacedBolts)
        {
            var authored = pending.Registration.Authored;
            Rac1PickupContact? contact = null;
            if (!authored.PreserveAuthoredHeight)
            {
                var query = PhysicsRayQueryParameters3D.Create(FromEffectNative(authored.ProbeStart), FromEffectNative(authored.ProbeEnd));
                query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() };
                var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (hit.TryGetValue("position", out var position) && hit.TryGetValue("normal", out var normal))
                    contact = new(ToEffectNative(position.AsVector3()), ToEffectNative(normal.AsVector3()));
            }
            int id = pending.Registration.PickupId;
            var motion = Rac1BoltMotionSession.FromPlaced(authored, Rac1EffectRandom, contact);
            var node = pending.Node.Root;
            node.GlobalPosition = FromEffectNative(motion.Position);
            node.Quaternion = FromEffectOrientation(motion.Orientation);
            node.Scale = Vector3.One * motion.VisualScale;
            _rac1PickupNodes.Add(id, node);
            _rac1PickupMotion.Add(id, motion);
        }
        _rac1PendingPlacedBolts.Clear();
    }

    private int CollectRac1HostedBolt(int pickupId, Node3D node)
    {
        if (_rac1PlacedBolts.TryCollect(pickupId, out var placed))
        {
            _rac1PlacedBoltNodes[pickupId].ApplyState(placed!.EntityState);
            _rac1PlacedBoltNodes.Remove(pickupId);
            return placed.Value;
        }
        int value = _rac1BoltCrates.OwnsPickup(pickupId)
            ? _rac1BoltCrates.CollectPickup(pickupId) : _rac1BoltPickups.Collect(pickupId);
        node.QueueFree();
        return value;
    }
}

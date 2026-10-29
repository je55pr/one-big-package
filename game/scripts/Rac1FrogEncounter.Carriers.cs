using Godot;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
using OBP.RAC1.Gameplay;
using OBP.Godot;

namespace OBP.Game;

/// <summary>Native class-827 carrier-owned class-500 *spawned* visuals.
/// The copied mesh is from the mounted R&C1 retail disc, not a fabricated box.
/// These children have no authored placement, crate payout or world-crate UID.</summary>
internal sealed partial class Rac1FrogEncounter
{
    private readonly Dictionary<int, Node3D> _rac1FrogCarrierChildren = [];

    private void ResetRac1FrogCarriers()
    {
        foreach (var child in _rac1FrogCarrierChildren.Values)
            if (IsInstanceValid(child)) child.QueueFree();
        _rac1FrogCarrierChildren.Clear();
    }

    internal void ConfigureRac1FrogCarriers(RuntimeWorldScene.Result result)
    {
        if (_rac1Frogs is null) return;
        var carrierEnemies = _host.Roster.Values.Where(e =>
            e.Node.Source.NativeClassId == Rac1FrogSession.ClassId &&
            _rac1Frogs.Get(e.Node.Source.InstanceIndex).CarrierMode).ToArray();
        if (carrierEnemies.Length == 0) return;
        var donor = _host.Presentation.CrateNodes.FirstOrDefault(crate =>
            crate.Source.NativeClassId == Rac1FrogSession.CarrierChildClassId &&
            crate.Root.GetChildren().OfType<MeshInstance3D>().Any());
        if (donor is null)
        {
            GD.PrintErr("[frog-carrier] retail class-500 crate mesh missing; keeping carriers dormant");
            return;
        }

        foreach (var enemy in carrierEnemies)
        {
            int instance = enemy.Node.Source.InstanceIndex;
            var child = new Node3D { Name = $"FrogCarrier_class500_parent{instance}" };
            foreach (var mesh in donor.Root.GetChildren().OfType<MeshInstance3D>())
            {
                var copy = new MeshInstance3D
                {
                    Name = "RetailClass500Surface",
                    Mesh = mesh.Mesh,
                    MaterialOverride = mesh.MaterialOverride,
                    Transform = mesh.Transform
                };
                if (mesh.Mesh is { } data)
                    for (int surface = 0; surface < data.GetSurfaceCount(); surface++)
                        copy.SetSurfaceOverrideMaterial(surface, mesh.GetSurfaceOverrideMaterial(surface));
                child.AddChild(copy);
            }
            if (child.GetChildCount() == 0) { child.Free(); continue; }
            result.Root.AddChild(child);
            child.GlobalPosition = enemy.Node.Root.GlobalPosition;
            child.Rotation = enemy.Node.Root.Rotation;
            child.Scale = donor.Root.Scale;
            _rac1Frogs.BindCarrierChild(instance, Rac1FrogSession.CarrierChildClassId);
            _rac1FrogCarrierChildren.Add(instance, child);
        }
        GD.Print($"[frog-carrier] {_rac1FrogCarrierChildren.Count} source-owned class500 children, no authored crate inflation");
    }

    private void EmitRac1FrogCarrierBurst(Vector3 parentPosition)
    {
        if (!_ports.HasFragmentAssets()) return;
        foreach (var piece in Rac1FrogCarrierBurst.Create(ToEffectNative(parentPosition), Rac1EffectRandom))
        {
            if (!_ports.HasFragmentModel(piece.NativeClassId))
                continue; // Never invent a missing level-specific fragment.
            var visual = _ports.CreateFragmentNode(piece.NativeClassId,
                $"FrogCarrierDebris_retail_class{piece.NativeClassId}");
            visual.GlobalPosition = FromEffectNative(piece.Position);
            visual.Quaternion = _ports.FromEffectOrientation(piece.Orientation);
            visual.Scale = Vector3.One * piece.Scale;
            _ports.TrackFragment(visual, piece);
        }
    }

    /// <summary>Native child hit-slot check (FUN_L06_002e94f0):
    /// destruction removes the parent, unlike proximity release.</summary>
    private bool TryStrikeRac1FrogCarrier(int instance, int? sourceClass)
    {
        if (_rac1Frogs is null ||
            !_rac1FrogCarrierChildren.TryGetValue(instance, out var child) ||
            !IsInstanceValid(child) || !_host.Roster.TryGetValue(instance, out var enemy) ||
            !_rac1Frogs.HitCarrierChild(instance, sourceClass))
            return false;
        EmitRac1FrogCarrierBurst(enemy.Node.Root.GlobalPosition);
        child.QueueFree();
        _rac1FrogCarrierChildren.Remove(instance);
        enemy.Body.CollisionLayer = 0;
        enemy.Body.CollisionMask = 0;
        enemy.Node.ApplyState(_host.Context.MobyRuntime.Require(enemy.Node.Source).EntityState);
        GD.Print($"[frog-carrier] class500 child hit removed parent i{instance}, no crate payout");
        return true;
    }

    internal void ResolveRac1FrogCarrierWrenchContacts(Vector3 previousHand, Vector3 previousTip,
        Vector3 hand, Vector3 tip, float radius)
    {
        foreach (var pair in _rac1FrogCarrierChildren.ToArray())
        {
            var key = new Rac1MobyRuntimeKey(Rac1FrogSession.ClassId, pair.Key);
            if (_ports.HasWrenchContact(key) || !IsInstanceValid(pair.Value)) continue;
            bool contact = pair.Value.GetChildren().OfType<MeshInstance3D>().Any(mesh =>
            {
                Aabb bounds = (mesh.GlobalTransform * mesh.GetAabb()).Grow(radius);
                return ActorContactGeometry.WrenchSegmentIntersects(bounds, previousHand, previousTip) ||
                    ActorContactGeometry.WrenchSegmentIntersects(bounds, hand, tip) ||
                    ActorContactGeometry.WrenchSegmentIntersects(bounds, previousHand, hand) ||
                    ActorContactGeometry.WrenchSegmentIntersects(bounds, previousTip, tip) ||
                    ActorContactGeometry.WrenchSegmentIntersects(bounds, previousHand, tip) ||
                    ActorContactGeometry.WrenchSegmentIntersects(bounds, previousTip, hand);
            });
            if (!contact || !TryStrikeRac1FrogCarrier(pair.Key, 71)) continue;
            _ports.AddWrenchContact(key);
        }
    }

    internal void TickRac1FrogCarrier(Rac1HostedEnemy enemy)
    {
        if (_player is null || _rac1Frogs is null) return;
        int instance = enemy.Node.Source.InstanceIndex;
        if (!_rac1FrogCarrierChildren.TryGetValue(instance, out var child) ||
            !IsInstanceValid(child)) return;
        if (enemy.Node.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition) >
            FrogHostSleepRadius * FrogHostSleepRadius) return;
        Vector3 target = _player.GlobalPosition;
        bool eligible = true;
        var authored = _rac1Frogs.GetAuthored(instance);
        if (authored.TargetInstance >= 0)
        {
            if (!_host.Presentation.LinkedTargets.TryGetValue(authored.TargetInstance, out var linked) ||
                !IsInstanceValid(linked.Root) || !_host.IsRac1GameplayActive(linked))
                eligible = false;
            else target = linked.Root.GlobalPosition;
        }
        var frame = _rac1Frogs.Step(instance, ToEffectNative(enemy.Node.Root.GlobalPosition),
            ToEffectNative(target), eligible);
        if (!frame.CarrierReleased) return;
        EmitRac1FrogCarrierBurst(enemy.Node.Root.GlobalPosition);
        child.QueueFree();
        _rac1FrogCarrierChildren.Remove(instance);
        bool active = frame.Presented;
        enemy.Body.CollisionLayer = active ? 1u : 0u;
        enemy.Body.CollisionMask = active ? 1u : 0u;
        enemy.Node.ApplyState(_host.Context.MobyRuntime.Require(enemy.Node.Source).EntityState);
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, frame.Sequence, 5);
        GD.Print($"[frog-carrier] parent i{instance} released native class500 child at four-unit target gate");
    }
}
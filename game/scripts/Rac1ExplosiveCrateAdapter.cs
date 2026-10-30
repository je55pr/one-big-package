using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Class-505 scene contact, flash and blast adapter. The recovered native session
/// owns the fuse/state transitions; the world lease owns all borrowed scene bindings.
/// </summary>
internal sealed class Rac1ExplosiveCrateAdapter(
    Rac1WorldContext context, Rac1WorldPresentation presentation, Rac1LootPresentation loot,
    Rac1EnemyHost enemies, Rac1BreakablePorts ports)
{
    private Rac1ExplosiveCrateSession? _session;
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _nodes = [];
    private readonly Dictionary<int, (MeshInstance3D Mesh, Material Original, StandardMaterial3D Flash, Color Colour)[]> _materials = [];
    private readonly HashSet<(int Source, int Target)> _hits = [];
    private readonly HashSet<int> _playerHits = [];
    private readonly HashSet<int> _detonated = [];
    private double _tickCarry;

    internal void Reset()
    {
        foreach (var materials in _materials.Values)
            foreach (var (mesh, original, flash, _) in materials)
            {
                if (GodotObject.IsInstanceValid(mesh) && ReferenceEquals(mesh.MaterialOverride, flash))
                    mesh.MaterialOverride = original;
                if (GodotObject.IsInstanceValid(flash)) flash.Dispose();
            }
        _session = null;
        _nodes.Clear();
        _materials.Clear();
        _hits.Clear();
        _playerHits.Clear();
        _detonated.Clear();
        _tickCarry = 0;
    }

    internal void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (_session is not null) throw new InvalidOperationException("Explosive crates already configured.");
        _session = new(context.MobyRuntime, ports.ChooseFrames);
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != Rac1ExplosiveCrateSession.ClassId) continue;
            var node = Rac1WorldPresentation.FindPresentedDynamic(scene, source)
                ?? throw new InvalidDataException($"Native class-505 explosive {source.InstanceIndex} has no original model.");
            _session.Register(source, node.State);
            _nodes.Add(source.InstanceIndex, node);
            presentation.CrateNodes.Add(node);
            loot.AddRac1CrateCollider(node);
            _materials.Add(source.InstanceIndex, node.Root.GetChildren()
                .OfType<MeshInstance3D>().Where(mesh => mesh.MaterialOverride is StandardMaterial3D)
                .Select(mesh =>
                {
                    var original = (StandardMaterial3D)mesh.MaterialOverride;
                    var material = (StandardMaterial3D)original.Duplicate();
                    mesh.MaterialOverride = material;
                    return (mesh, (Material)original, material, material.AlbedoColor);
                }).ToArray());
        }
        GD.Print($"[rac1-explosives] {_nodes.Count} original class-505 crates registered");
    }

    private bool Contains(RuntimeWorldScene.DynamicObjectNode node) =>
        node.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId &&
        _nodes.TryGetValue(node.Source.InstanceIndex, out var registered) &&
        ReferenceEquals(node, registered);

    internal bool TryTriggerDamage(RuntimeWorldScene.DynamicObjectNode node, Rac1GameplayDamageEvent damage)
    {
        if (_session is null || !Contains(node) || !context.MobyRuntime.CanDispatchDamage(damage))
            return false;
        context.MobyRuntime.DispatchDamage<Rac1ExplosiveCrateTriggered>(damage);
        return true;
    }

    internal bool TryTriggerDamage(Rac1GameplayDamageEvent damage) =>
        _nodes.TryGetValue((int)damage.Target.RuntimeId, out var node) && TryTriggerDamage(node, damage);

    internal bool TouchForDiagnostics(int instanceIndex) =>
        _session is not null && _nodes.ContainsKey(instanceIndex) && _session.TryTouch(instanceIndex);

    internal ImmutableArray<Rac1BreakableEntrySnapshot> Observe() =>
        _nodes.OrderBy(pair => pair.Key).Select(pair => new Rac1BreakableEntrySnapshot(
            pair.Key, pair.Value.Source.NativeClassId, context.MobyRuntime.Require(pair.Value.Source).State.NativeState,
            pair.Value.State.Presentation.Presence, loot.HasRac1CrateCollider(pair.Key))).ToImmutableArray();

    internal void Tick(double delta)
    {
        var session = _session;
        var player = ports.Player();
        if (session is null || _nodes.Count == 0 || player is null || !GodotObject.IsInstanceValid(player))
            return;

        RegisterGroundedTouches(player, session);
        _tickCarry += Math.Max(0, delta) * Rac1RatchetMovementController.UpdateHz;
        while (_tickCarry >= 1)
        {
            _tickCarry--;
            foreach (var pair in _nodes)
            {
                // Hidden crates cannot prove the native draw-range predicate.
                var frame = session.Step(pair.Key, beyondNativeDrawRange: false);
                PresentFrame(session, pair.Key, pair.Value, frame, player);
            }
        }
    }

    private void RegisterGroundedTouches(PlayerHost player, Rac1ExplosiveCrateSession session)
    {
        // Native class 505 reacts to grounded/touched Moby contacts, not proximity.
        for (int i = 0; i < player.GetSlideCollisionCount(); i++)
        {
            if (player.GetSlideCollision(i).GetCollider() is not StaticBody3D body ||
                !loot.TryGetRac1CrateContact(body, out int id) || !_nodes.ContainsKey(id)) continue;
            session.TryTouch(id);
        }
    }

    private void PresentFrame(Rac1ExplosiveCrateSession session, int id,
        RuntimeWorldScene.DynamicObjectNode node, Rac1ExplosiveCrateFrame frame, PlayerHost player)
    {
        ApplyFlash(id, frame.FlashRed);
        if (frame.WarningSound)
            loot.PlayRac1PickupSound(Rac1ExplosiveCrateSession.ClassId, 0x20, node.Root.GlobalPosition);
        if (frame.Detonated && _detonated.Add(id))
            PresentDetonation(session, id, node);
        if (frame.MobyBlastRadius > 0 || frame.PlayerBlastRadius > 0)
            ApplyBlast(node, frame, player);
        ApplyPresence(id, node, frame);
    }

    private void ApplyFlash(int id, bool flashRed)
    {
        if (!_materials.TryGetValue(id, out var materials)) return;
        foreach (var (_, _, material, colour) in materials)
            material.AlbedoColor = flashRed
                ? new Color(.78f, .25f, .25f, colour.A)
                : colour;
    }

    private void PresentDetonation(Rac1ExplosiveCrateSession session, int id,
        RuntimeWorldScene.DynamicObjectNode node)
    {
        _hits.RemoveWhere(hit => hit.Source == id);
        _playerHits.Remove(id);
        // Debris consumes the shared RNG before the authored reward and its physical bolt motions.
        loot.SpawnRac1CrateDebris(node);
        var pickups = context.BoltPickups.Emit(ports.SelectCrateReward(session.Authored(id).RewardCentre));
        loot.PresentRac1ExplosiveDetonation(node, pickups);
        node.Root.Visible = false;
    }

    private void ApplyPresence(int id, RuntimeWorldScene.DynamicObjectNode node, Rac1ExplosiveCrateFrame frame)
    {
        if (frame.EntityState.Presentation.Presence == node.State.Presentation.Presence) return;
        node.ApplyState(frame.EntityState);
        if (!frame.Reappeared) return;
        _detonated.Remove(id);
        loot.AddRac1CrateCollider(node);
    }

    private void ApplyBlast(RuntimeWorldScene.DynamicObjectNode source,
        Rac1ExplosiveCrateFrame frame, PlayerHost player)
    {
        var origin = source.Root.GlobalPosition + Vector3.Up * .5f;
        int sourceIndex = source.Source.InstanceIndex;
        if (!_playerHits.Contains(sourceIndex) &&
            origin.DistanceTo(player.GlobalPosition + Vector3.Up * .5f) <= frame.PlayerBlastRadius &&
            ports.LineOfSight(origin, player.GlobalPosition + Vector3.Up * .5f))
        {
            _playerHits.Add(sourceIndex);
            ports.PlayerDamage(Rac1ExplosiveCrateBlast.ToPlayer(sourceIndex));
        }

        foreach (var target in presentation.CrateNodes.Concat(enemies.Targets))
        {
            int targetIndex = target.Source.InstanceIndex;
            if (targetIndex == sourceIndex || _hits.Contains((sourceIndex, targetIndex)) ||
                !GodotObject.IsInstanceValid(target.Root) || !enemies.IsRac1GameplayActive(target)) continue;
            var centre = target.Root.GlobalPosition + Vector3.Up * .5f;
            if (origin.DistanceTo(centre) > frame.MobyBlastRadius + .35f ||
                !ports.LineOfSight(origin, centre)) continue;
            var damage = Rac1ExplosiveCrateBlast.ToMoby(sourceIndex,
                new(target.Source.NativeClassId, targetIndex));
            if (!ApplyMobyBlastDamage(target, damage)) continue;
            _hits.Add((sourceIndex, targetIndex));
            context.DamageTransport.Publish(damage);
        }
    }

    private bool ApplyMobyBlastDamage(RuntimeWorldScene.DynamicObjectNode target,
        Rac1GameplayDamageEvent damage)
    {
        if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
            return TryTriggerDamage(target, damage);

        if (Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId) &&
            context.MobyRuntime.CanDispatchDamage(damage))
        {
            ports.CompleteOrdinaryCrateDamage(target,
                context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage));
            return true;
        }

        return enemies.Roster.ContainsKey(target.Source.InstanceIndex) &&
            enemies.ApplyRac1EnemyWeaponDamage(target, damage);
    }
}

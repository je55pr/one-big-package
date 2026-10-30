using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Source-admitted Novalis mushroom/rock contacts. The bounded native session
/// owns the accepted hit policy; no guessed fragments or generic bolt drops.
/// </summary>
internal sealed class Rac1NovalisBreakableAdapter(
    int levelId, Rac1WorldContext context, Rac1LootPresentation loot, Rac1BreakablePorts ports)
{
    private Rac1NovalisBreakableSession _session = new();
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _nodes = [];

    internal IEnumerable<RuntimeWorldScene.DynamicObjectNode> Nodes => _nodes.Values;
    internal int DestroyedCount => _session.DestroyedCount;
    internal bool IsDestroyed(int instanceIndex) => _session.IsDestroyed(instanceIndex);

    internal bool Contains(RuntimeWorldScene.DynamicObjectNode node) =>
        node.Source.SourceGame == ObpSourceGame.Rac1 &&
        Rac1NovalisBreakableSession.IsAdmitted(levelId, node.Source.NativeClassId) &&
        _nodes.TryGetValue(node.Source.InstanceIndex, out var registered) &&
        ReferenceEquals(node, registered);

    internal void Reset()
    {
        _session = new();
        _nodes.Clear();
    }

    internal void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (world.LevelId != Rac1NovalisBreakableSession.NovalisLevelId) return;
        if (_nodes.Count != 0) throw new InvalidOperationException("Novalis breakables already configured.");
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (source.SourceGame != ObpSourceGame.Rac1 ||
                !Rac1NovalisBreakableSession.IsAdmitted(world.LevelId, source.NativeClassId))
                continue;
            var node = Rac1WorldPresentation.FindPresentedDynamic(scene, source)
                ?? throw new InvalidDataException("Novalis mushroom/rock lacks its imported visual.");
            _session.Register(world.LevelId, source.NativeClassId, source.InstanceIndex);
            _nodes.Add(source.InstanceIndex, node);
        }
        GD.Print($"[rac1-novalis] {_session.RegisteredCount} mushroom/rock props admitted");
    }

    internal ImmutableArray<Rac1BreakableEntrySnapshot> Observe() =>
        _nodes.OrderBy(pair => pair.Key).Select(pair => new Rac1BreakableEntrySnapshot(
            pair.Key, pair.Value.Source.NativeClassId, null,
            pair.Value.State.Presentation.Presence, false)).ToImmutableArray();

    internal bool TryBreak(RuntimeWorldScene.DynamicObjectNode node,
        Rac1NovalisBreakableSession.ContactKind contact)
    {
        if (!Contains(node) || !GodotObject.IsInstanceValid(node.Root) ||
            !_session.TryBreak(node.Source.InstanceIndex, contact))
            return false;

        // No class-specific fragment program is recovered yet. Preserve the
        // existing source-bounded sound and optional plant reward, without guessing.
        node.ApplyState(node.State.WithPresence(RuntimeEntityPresence.Inactive));
        loot.PlayRac1PickupSound(node.Source.NativeClassId, 0, node.Root.GlobalPosition);
        IReadOnlyList<Rac1BoltPickup> pickups = [];
        if (node.Source.NativeClassId == Rac1NovalisBreakableSession.MushroomClass &&
            ports.Player() is { } player && GodotObject.IsInstanceValid(player))
        {
            Vector3 hero = player.GlobalPosition, plant = node.Root.GlobalPosition;
            float planarDistance = new Vector2(hero.X - plant.X, hero.Z - plant.Z).Length();
            if (Rac1NovalisBreakableSession.PlantBoltRewardAdmitted(planarDistance))
            {
                int value = ports.ChooseFrames(Rac1NovalisBreakableSession.PlantBoltMinimum,
                    Rac1NovalisBreakableSession.PlantBoltMaximum);
                pickups = context.BoltPickups.Emit(value);
            }
        }
        loot.SpawnRac1BoltPickups(node.Root.GlobalPosition, pickups);
        GD.Print($"[rac1-novalis] {node.Source.NativeClassId} instance {node.Source.InstanceIndex} " +
                 $"destroyed by {contact} (host contact policy)");
        return true;
    }

    internal bool BreakForDiagnostics(int instanceIndex,
        Rac1NovalisBreakableSession.ContactKind contact) =>
        _nodes.TryGetValue(instanceIndex, out var node) && TryBreak(node, contact);

    internal void ResolveBomb(Vector3 origin, float radius)
    {
        foreach (var node in _nodes.Values)
        {
            if (_session.IsDestroyed(node.Source.InstanceIndex) ||
                !GodotObject.IsInstanceValid(node.Root))
                continue;
            var center = node.Root.GlobalPosition + Vector3.Up * .5f;
            if (origin.DistanceTo(center) > radius + .35f ||
                !ports.LineOfSight(origin, center))
                continue;
            TryBreak(node, Rac1NovalisBreakableSession.ContactKind.Explosion);
        }
    }
}

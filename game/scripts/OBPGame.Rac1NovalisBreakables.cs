using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1NovalisBreakableSession _rac1NovalisBreakables = new();
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1NovalisBreakableNodes = [];

    private void ResetRac1NovalisBreakables()
    {
        _rac1NovalisBreakables = new();
        _rac1NovalisBreakableNodes.Clear();
    }

    private void ConfigureRac1NovalisBreakables(RuntimeWorld world,
        RuntimeWorldScene.Result result)
    {
        if (world.LevelId != Rac1NovalisBreakableSession.NovalisLevelId) return;
        foreach (var source in world.DynamicObjects ?? [])
        {
            if (!Rac1NovalisBreakableSession.IsAdmitted(world.LevelId, source.NativeClassId))
                continue;
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException("Novalis mushroom/rock lacks its imported visual.");
            _rac1NovalisBreakables.Register(world.LevelId, source.NativeClassId, source.InstanceIndex);
            _rac1NovalisBreakableNodes.Add(source.InstanceIndex, node);
        }
        GD.Print($"[rac1-novalis] {_rac1NovalisBreakables.RegisteredCount} mushroom/rock props admitted");
    }

    private bool TryBreakRac1NovalisProp(RuntimeWorldScene.DynamicObjectNode node,
        Rac1NovalisBreakableSession.ContactKind contact)
    {
        if (!_rac1NovalisBreakables.TryBreak(node.Source.InstanceIndex, contact))
            return false;

        // Class-specific fragments and bolt payouts require native consequence
        // recovery. Do not silently award arbitrary currency here.
        node.ApplyState(node.State.WithPresence(OBP.Runtime.Gameplay.RuntimeEntityPresence.Inactive));
        PlayRac1PickupSound(node.Source.NativeClassId, 0, node.Root.GlobalPosition);
        GD.Print($"[rac1-novalis] {node.Source.NativeClassId} instance {node.Source.InstanceIndex} " +
                 $"destroyed by {contact} (host contact policy)");
        return true;
    }

    private void ResolveRac1NovalisBombBreakables(Vector3 origin, float radius)
    {
        foreach (var node in _rac1NovalisBreakableNodes.Values)
        {
            if (_rac1NovalisBreakables.IsDestroyed(node.Source.InstanceIndex) ||
                !IsInstanceValid(node.Root))
                continue;
            var center = node.Root.GlobalPosition + Vector3.Up * .5f;
            if (origin.DistanceTo(center) > radius + .35f ||
                !BombHasLineOfSight(origin, center))
                continue;
            TryBreakRac1NovalisProp(node, Rac1NovalisBreakableSession.ContactKind.Explosion);
        }
    }
}

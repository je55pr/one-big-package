using Godot;
using OBP.Godot;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1RedPlantSession _rac1RedPlants = new();
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _rac1RedPlantNodes = [];
    private Vector3? _rac1RedPlantLastPlayerPosition;
    private double _rac1RedPlantNativeTickCarry;

    private void ResetRac1RedPlants()
    {
        _rac1RedPlants = new Rac1RedPlantSession();
        _rac1RedPlantNodes.Clear();
        _rac1RedPlantLastPlayerPosition = null;
        _rac1RedPlantNativeTickCarry = 0;
    }

    private void ConfigureRac1RedPlants(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        // This selector is proved from the LEVEL0 overlay only; an identical
        // class asset elsewhere does not establish identical gameplay policy.
        if (world.LevelId != Rac1RedPlantSession.NativeLevelId) return;
        foreach (var source in (world.DynamicObjects ?? [])
            .Where(obj => obj.NativeClassId == Rac1MobyAnimationProvider.RedPlantClassId))
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Veldin red plant {source.InstanceIndex} has no source visual.");
            if (_rac1RedPlants.Register(source))
                _rac1RedPlantNodes.Add(node);
        }
        GD.Print($"[rac1-red-plants] {_rac1RedPlants.RegisteredCount} source-animated Veldin plants");
    }

    private void TickRac1RedPlants(double delta)
    {
        if (_rac1RedPlantNodes.Count == 0 || _player is null || !IsInstanceValid(_player)) return;

        Vector3 playerPosition = _player.GlobalPosition;
        // This is observed host movement, not a fake native P+0x100 velocity:
        // normalize distance moved during the render interval to one 60-Hz
        // update so different host frame rates use the same source threshold.
        double nativeMotion = _rac1RedPlantLastPlayerPosition is { } previous && delta > 0
            ? playerPosition.DistanceTo(previous) / (delta * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz)
            : 0;
        _rac1RedPlantLastPlayerPosition = playerPosition;
        _rac1RedPlantNativeTickCarry += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_rac1RedPlantNativeTickCarry >= 1)
        {
            _rac1RedPlantNativeTickCarry--;
            foreach (var node in _rac1RedPlantNodes)
            {
                double distance = node.Root.GlobalPosition.DistanceTo(playerPosition);
                var next = _rac1RedPlants.Step(node.Source, node.State, distance, nativeMotion);
                if (next.Presentation.AnimationRole != node.State.Presentation.AnimationRole)
                    node.ApplyState(next);
            }
        }
    }
}

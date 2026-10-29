using Godot;
using OBP.Godot;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Veldin's original red-plant animation session, bound to this scene only.</summary>
internal sealed class Rac1RedPlantMechanism : Rac1AuthoredMechanism
{
    private readonly Rac1RedPlantSession _session = new();
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _nodes = [];
    private Vector3? _lastPlayerPosition;
    private double _nativeTickCarry;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        // Selector is proved from LEVEL0 only. A reused model in another level
        // is not evidence of identical native gameplay policy.
        if (world.LevelId != Rac1RedPlantSession.NativeLevelId) return;
        foreach (var source in (world.DynamicObjects ?? [])
            .Where(obj => obj.NativeClassId == Rac1MobyAnimationProvider.RedPlantClassId))
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Veldin red plant {source.InstanceIndex} has no source visual.");
            if (_session.Register(source)) _nodes.Add(node);
        }
        GD.Print($"[rac1-red-plants] {_session.RegisteredCount} source-animated Veldin plants");
    }

    internal override void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player)
    {
        if (phase != Rac1AuthoredTickPhase.Visual || _nodes.Count == 0 ||
            player is null || !GodotObject.IsInstanceValid(player)) return;

        Vector3 position = player.GlobalPosition;
        // Observed host movement, not an invented P+0x100 velocity.
        // Normalize to one native 60-Hz frame.
        double nativeMotion = _lastPlayerPosition is { } previous && delta > 0
            ? position.DistanceTo(previous) / (delta * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz)
            : 0;
        _lastPlayerPosition = position;
        _nativeTickCarry += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_nativeTickCarry >= 1)
        {
            _nativeTickCarry--;
            foreach (var node in _nodes)
            {
                double distance = node.Root.GlobalPosition.DistanceTo(position);
                var next = _session.Step(node.Source, node.State, distance, nativeMotion);
                if (next.Presentation.AnimationRole != node.State.Presentation.AnimationRole)
                    node.ApplyState(next);
            }
        }
    }

    internal override void OnVisualTickSkipped() => _lastPlayerPosition = null;

    public override void Dispose()
    {
        _nodes.Clear();
        _lastPlayerPosition = null;
        _nativeTickCarry = 0;
    }
}

using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private bool TryGetRac1Hostile(
        int instanceIndex,
        out RuntimeWorldScene.DynamicObjectNode? node,
        out Rac1Class749HostProbe? probe)
    {
        if (_world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: Rac1Class749VeldinPopulation.LevelId } &&
            _rac1HostileNodes.TryGetValue(instanceIndex, out node))
        {
            probe = _rac1Hostiles.Probe(node.Source);
            return true;
        }

        node = null;
        probe = null;
        return false;
    }

    private Rac1Class749HostProbe[] SnapshotRac1HostileProbes() =>
        _rac1HostileNodes.Values
            .OrderBy(node => node.Source.InstanceIndex)
            .Select(node => _rac1Hostiles.Probe(node.Source))
            .ToArray();

    private bool IsRac1GameplayActive(RuntimeWorldScene.DynamicObjectNode node) =>
        _rac1MobyRuntime.Require(node.Source).IsActive;

    private static bool HasRac1HostContactGeometry(
        RuntimeWorldScene.DynamicObjectNode node) =>
        node.Root.GetChildren().Any(child => child is MeshInstance3D);

    private void SetRac1HostedPresence(
        RuntimeWorldScene.DynamicObjectNode node,
        RuntimeEntityPresence presence)
    {
        var runtime = _rac1MobyRuntime.Require(node.Source);
        _rac1MobyRuntime.SetTransform(
            runtime,
            RuntimeWorldScene.ToRuntimeTransform(node.Root.Transform));
        node.ApplyState(_rac1MobyRuntime.SetPresence(runtime, presence));
        if (_rac1CrateBodies.TryGetValue(node.Source.InstanceIndex, out var crateBody) && IsInstanceValid(crateBody))
            crateBody.CollisionLayer = presence == RuntimeEntityPresence.Active ? 1u : 0;
        if (_rac1Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy))
        {
            enemy.Body.CollisionLayer = presence == RuntimeEntityPresence.Active && !enemy.RewardEmitted ? 1u : 0;
            if (presence != RuntimeEntityPresence.Active) SetRac1EnemyFlame(enemy, false);
        }
    }

}

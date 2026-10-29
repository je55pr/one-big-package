using OBP.Core;
using Godot;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

internal sealed partial class Rac1EnemyHost
{
    internal bool TryGetRac1Hostile(
        int instanceIndex,
        out RuntimeWorldScene.DynamicObjectNode? node,
        out Rac1Class749HostProbe? probe)
    {
        if (Current && _world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: Rac1Class749VeldinPopulation.LevelId } &&
            Presentation.HostileNodes.TryGetValue(instanceIndex, out node))
        {
            probe = Context.Hostiles.Probe(node.Source);
            return true;
        }

        node = null;
        probe = null;
        return false;
    }

    internal Rac1Class749HostProbe[] SnapshotRac1HostileProbes() =>
        !Current ? [] : Presentation.HostileNodes.Values
            .OrderBy(node => node.Source.InstanceIndex)
            .Select(node => Context.Hostiles.Probe(node.Source))
            .ToArray();

    // Native IDs may be reused. Check both the scene subtree and the exact
    // roster node before admitting an old callback to the current runtime.
    private bool OwnsNode(RuntimeWorldScene.DynamicObjectNode node)
    {
        if (!Current || node.Source.SourceGame != ObpSourceGame.Rac1 ||
            !IsInstanceValid(_sceneResult.Root) || !IsInstanceValid(node.Root) ||
            node.Root.IsQueuedForDeletion() || !_sceneResult.Root.IsAncestorOf(node.Root)) return false;
        if (Roster.TryGetValue(node.Source.InstanceIndex, out var enemy))
            return !enemy.Retired && ReferenceEquals(enemy.Node, node);
        return !Roster.PreviouslyOwned(node.Source.InstanceIndex);
    }

    private bool TryOwnedRuntime(RuntimeWorldScene.DynamicObjectNode node, out Rac1MobyRuntimeInstance? instance)
    {
        instance = null;
        return OwnsNode(node) &&
            Context.MobyRuntime.TryGet(new(node.Source.NativeClassId, node.Source.InstanceIndex), out instance) &&
            instance is not null && instance.EntityState.Identity.Matches(node.Source);
    }

    internal bool IsRac1GameplayActive(RuntimeWorldScene.DynamicObjectNode node) =>
        TryOwnedRuntime(node, out var runtime) && runtime!.IsActive;

    internal static bool HasRac1HostContactGeometry(
        RuntimeWorldScene.DynamicObjectNode node) =>
        node.Root.GetChildren().Any(child => child is MeshInstance3D);

    internal void SetRac1HostedPresence(
        RuntimeWorldScene.DynamicObjectNode node,
        RuntimeEntityPresence presence)
    {
        if (!TryOwnedRuntime(node, out var runtime)) return;
        Context.MobyRuntime.SetTransform(
            runtime!,
            RuntimeWorldScene.ToRuntimeTransform(node.Root.Transform));
        node.ApplyState(Context.MobyRuntime.SetPresence(runtime!, presence));
        _ports.Combat.CratePresence(node.Source.InstanceIndex, presence);
        if (_rac1Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy))
        {
            enemy.Body.CollisionLayer = presence == RuntimeEntityPresence.Active && !enemy.RewardEmitted ? 1u : 0;
            if (presence != RuntimeEntityPresence.Active) SetRac1EnemyFlame(enemy, false);
        }
    }

}

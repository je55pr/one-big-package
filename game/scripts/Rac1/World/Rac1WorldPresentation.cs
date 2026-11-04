using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record Rac1HostedProjectile(Node3D Node, Rac1BombProjectileSession Motion, long Generation);

/// <summary>Scene-only R&C1 views. Missing source presentation never manufactures contact geometry.</summary>
internal sealed class Rac1WorldPresentation : IDisposable
{
    // OBP feet-to-body presentation anchor; contact admission stays in Rac1WrenchHostContactPolicy.
    internal const float WrenchHostRootHeight = 1.0f;
    internal List<RuntimeWorldScene.DynamicObjectNode> CrateNodes { get; } = [];
    internal Dictionary<int, Node3D> PickupNodes { get; } = [];
    internal Dictionary<long, Rac1HostedProjectile> Projectiles { get; } = [];
    internal List<RuntimeWorldScene.DynamicObjectNode> Class749Nodes { get; } = [];
    internal Dictionary<int, RuntimeWorldScene.DynamicObjectNode> LinkedTargets { get; } = [];
    internal Dictionary<int, RuntimeWorldScene.DynamicObjectNode> HostileNodes { get; } = [];
    internal Vector3 LastEnvironmentalDeathPosition { get; set; }

    public void Dispose()
    {
        foreach (var projectile in Projectiles.Values)
            if (GodotObject.IsInstanceValid(projectile.Node) && !projectile.Node.IsQueuedForDeletion()) projectile.Node.QueueFree();
        Projectiles.Clear();
        CrateNodes.Clear();
        PickupNodes.Clear();
        Class749Nodes.Clear();
        LinkedTargets.Clear();
        HostileNodes.Clear();
        LastEnvironmentalDeathPosition = default;
    }

    internal static RuntimeWorldScene.DynamicObjectNode? FindPresentedDynamic(
        RuntimeWorldScene.Result result,
        RuntimeDynamicObject source) =>
        result.DynamicObjectNodes?.FirstOrDefault(node =>
            node.Source.SourceGame == source.SourceGame &&
            node.Source.NativeClassId == source.NativeClassId &&
            node.Source.InstanceIndex == source.InstanceIndex);
    internal void TrackLinkedTarget(
        RuntimeWorld world,
        RuntimeWorldScene.Result result,
        int linkedInstanceIndex)
    {
        if (LinkedTargets.ContainsKey(linkedInstanceIndex))
            return;

        var matches = (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.InstanceIndex == linkedInstanceIndex)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            GD.PrintErr(
                $"[rac1-gameplay] linked Moby i{linkedInstanceIndex} could not be resolved " +
                $"uniquely from authored runtime objects ({matches.Length} matches)");
            return;
        }

        RuntimeDynamicObject source = matches[0];
        var node = FindPresentedDynamic(result, source)
            ?? CrateNodes.FirstOrDefault(candidate =>
                candidate.Source.SourceGame == source.SourceGame &&
                candidate.Source.NativeClassId == source.NativeClassId &&
                candidate.Source.InstanceIndex == source.InstanceIndex)
            ?? CreateInvisibleRuntimeNode(result, source);
        LinkedTargets.Add(linkedInstanceIndex, node);
    }

    internal static RuntimeWorldScene.DynamicObjectNode CreateInvisibleRuntimeNode(
        RuntimeWorldScene.Result result,
        RuntimeDynamicObject source)
    {
        var root = new RuntimeWorldScene.RuntimeDynamicObjectRoot3D
        {
            Name = $"rac1_runtime_{source.NativeClassId}_{source.InstanceIndex}",
        };
        root.Configure(source);
        result.Root.AddChild(root);

        var node = new RuntimeWorldScene.DynamicObjectNode(source, root);
        node.ApplyState(RuntimeEntityState.FromAuthored(source));
        // Preserve authored identity/state for class registration, but do not
        // manufacture a visible/contactable host target when presentation is absent.
        root.Visible = false;
        return node;
    }

}

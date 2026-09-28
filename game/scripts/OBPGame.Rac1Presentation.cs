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
    private static RuntimeWorldScene.DynamicObjectNode? FindPresentedDynamic(
        RuntimeWorldScene.Result result,
        RuntimeDynamicObject source) =>
        result.DynamicObjectNodes?.FirstOrDefault(node =>
            node.Source.SourceGame == source.SourceGame &&
            node.Source.NativeClassId == source.NativeClassId &&
            node.Source.InstanceIndex == source.InstanceIndex);
    private void TrackRac1LinkedTarget(
        RuntimeWorld world,
        RuntimeWorldScene.Result result,
        int linkedInstanceIndex)
    {
        if (_rac1LinkedTargetNodes.ContainsKey(linkedInstanceIndex))
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
            ?? _rac1CrateNodes.FirstOrDefault(candidate =>
                candidate.Source.SourceGame == source.SourceGame &&
                candidate.Source.NativeClassId == source.NativeClassId &&
                candidate.Source.InstanceIndex == source.InstanceIndex)
            ?? CreateRac1InvisibleRuntimeNode(result, source);
        _rac1LinkedTargetNodes.Add(linkedInstanceIndex, node);
    }

    private static RuntimeWorldScene.DynamicObjectNode CreateRac1InvisibleRuntimeNode(
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

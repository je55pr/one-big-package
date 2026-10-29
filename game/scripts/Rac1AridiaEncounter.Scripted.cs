using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Game;

internal sealed partial class Rac1AridiaEncounter
{
    private Rac1AridiaClass604Session _rac1AridiaController604 = new();
    private Rac1AridiaSandmouseSession _rac1AridiaSandmouse = new();

    private void ResetRac1AridiaScriptActors()
    {
        _rac1AridiaController604 = new();
        _rac1AridiaSandmouse = new();
    }

    /// <summary>
    /// The retail Sandmouse #756 and its linked class604 #526 are ordinary
    /// authored Mobies, not hostile actors. Register them in the SAME native
    /// runtime store as the waiting class580 sharks. Their recovered state0
    /// initializers enter state1, but no missing cinematic/scene condition is
    /// fabricated. A legitimate scene adapter must eventually supply the
    /// class604 +0xbc control writes, animation completion and hero/route
    /// conditions before either machine can advance beyond this safe state.
    /// </summary>
    internal void ConfigureScriptActors(RuntimeWorld world,
        RuntimeWorldScene.Result result, IReadOnlyList<Rac1Instances.MobyInstance> placements)
    {
        var dynamic = (world.DynamicObjects ?? []).ToDictionary(moby => moby.InstanceIndex);
        foreach (var source in dynamic.Values.Where(moby =>
            moby.NativeClassId == Rac1AridiaEnemies.Sandmouse).OrderBy(moby => moby.InstanceIndex))
        {
            var pvar = placements[source.InstanceIndex].PVar
                ?? throw new InvalidDataException("Aridia Sandmouse has no authored PVar.");
            int controllerId = Rac1AridiaEnemies.ReadSandmouseLinkedController(pvar);
            if (!dynamic.TryGetValue(controllerId, out var linked) ||
                linked.NativeClassId != Rac1AridiaClass604Session.NativeClassId)
                throw new InvalidDataException(
                    $"Aridia Sandmouse {source.InstanceIndex} has no authored linked class604.");
            var linkedKey = new Rac1MobyRuntimeKey(linked.NativeClassId, controllerId);
            if (!_host.Context.MobyRuntime.TryGet(linkedKey, out _))
            {
                var linkedNode = Rac1WorldPresentation.FindPresentedDynamic(result, linked)
                    ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, linked);
                _rac1AridiaController604.Register(controllerId);
                var linkedNative = _host.Context.MobyRuntime.Register(linked, linkedNode.State, nativeState: 0,
                    pvar: placements[controllerId].PVar ?? []);
                linkedNode.ApplyState(linkedNative.EntityState);
                var init = _rac1AridiaController604.Step(controllerId, default);
                _host.Context.MobyRuntime.SetNativeState(linkedNative, init.NativeState);
            }

            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, source);
            _rac1AridiaSandmouse.Register(source.InstanceIndex, controllerId);
            var native = _host.Context.MobyRuntime.Register(source, node.State, nativeState: 0, pvar: pvar);
            node.ApplyState(native.EntityState);
            _host.Context.MobyRuntime.SetNativeState(native,
                _rac1AridiaSandmouse.Step(source.InstanceIndex, default));
        }
        GD.Print($"[rac1-aridia] scripted class604={_rac1AridiaController604.Count} " +
            $"Sandmouse={dynamic.Values.Count(m => m.NativeClassId == Rac1AridiaEnemies.Sandmouse)}; " +
            "registered in native state1; cinematic prerequisites unimplemented");
    }
}

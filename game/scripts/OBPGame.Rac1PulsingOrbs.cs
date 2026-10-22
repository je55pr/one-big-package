using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1PulsingOrbSession _rac1PulsingOrbs = new();
    private readonly Dictionary<int, StandardMaterial3D[]> _rac1PulsingOrbMaterials = [];
    private double _rac1PulsingOrbCarry;

    private void ResetRac1PulsingOrbs()
    {
        _rac1PulsingOrbs = new();
        _rac1PulsingOrbMaterials.Clear();
        _rac1PulsingOrbCarry = 0;
    }

    private void ConfigureRac1PulsingOrbs(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId is not (0 or 2 or 5 or 18)) return;
        foreach (var source in (world.DynamicObjects ?? [])
            .Where(source => source.NativeClassId == Rac1PulsingOrbSession.NativeClassId))
        {
            var node = FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Native pulse object {source.InstanceIndex} has no source visual.");
            var materialCopies = node.Root.GetChildren().OfType<MeshInstance3D>()
                .Select(surface =>
                {
                    if (surface.MaterialOverride is not StandardMaterial3D original)
                        throw new InvalidDataException($"R&C1 pulse object {source.InstanceIndex} has no tintable original material.");
                    var copy = (StandardMaterial3D)original.Duplicate();
                    surface.MaterialOverride = copy;
                    return copy;
                }).ToArray();
            if (materialCopies.Length == 0)
                throw new InvalidDataException($"R&C1 pulse object {source.InstanceIndex} has no rendered surface.");
            _rac1PulsingOrbs.Register(world.LevelId, source);
            _rac1PulsingOrbMaterials.Add(source.InstanceIndex, materialCopies);
        }
        GD.Print($"[rac1-orbs] {_rac1PulsingOrbs.Count} native pulsing-colour sources ready");
    }

    private void TickRac1PulsingOrbs(double delta)
    {
        if (_rac1PulsingOrbMaterials.Count == 0) return;
        _rac1PulsingOrbCarry += Math.Max(0, delta) * Rac1PulsingOrbSession.NativeUpdateHz;
        while (_rac1PulsingOrbCarry >= 1)
        {
            _rac1PulsingOrbCarry--;
            foreach (var entry in _rac1PulsingOrbMaterials)
            {
                var rgb = _rac1PulsingOrbs.Step(entry.Key);
                var colour = new Color(rgb.Red / 255f, rgb.Green / 255f, rgb.Blue / 255f);
                foreach (var material in entry.Value)
                    material.AlbedoColor = colour;
            }
        }
    }
}

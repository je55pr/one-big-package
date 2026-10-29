using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Level-specific native orb colour controller with reversible material overrides.</summary>
internal sealed class Rac1PulsingOrbMechanism : Rac1AuthoredMechanism
{
    private readonly Rac1PulsingOrbSession _session = new();
    private readonly Dictionary<int, StandardMaterial3D[]> _materials = [];
    private readonly List<(MeshInstance3D Surface, Material? Original, StandardMaterial3D Copy)> _overrides = [];
    private double _carry;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId is not (0 or 2 or 5 or 18)) return;
        foreach (var source in (world.DynamicObjects ?? [])
            .Where(source => source.NativeClassId == Rac1PulsingOrbSession.NativeClassId))
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? throw new InvalidDataException($"Native pulse object {source.InstanceIndex} has no source visual.");
            var materialCopies = node.Root.GetChildren().OfType<MeshInstance3D>()
                .Select(surface =>
                {
                    if (surface.MaterialOverride is not StandardMaterial3D original)
                        throw new InvalidDataException($"R&C1 pulse object {source.InstanceIndex} has no tintable original material.");
                    var copy = (StandardMaterial3D)original.Duplicate();
                    surface.MaterialOverride = copy;
                    _overrides.Add((surface, original, copy));
                    return copy;
                }).ToArray();
            if (materialCopies.Length == 0)
                throw new InvalidDataException($"R&C1 pulse object {source.InstanceIndex} has no rendered surface.");
            _session.Register(world.LevelId, source);
            _materials.Add(source.InstanceIndex, materialCopies);
        }
        GD.Print($"[rac1-orbs] {_session.Count} native pulsing-colour sources ready");
    }

    internal override void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player)
    {
        if (phase != Rac1AuthoredTickPhase.Visual || _materials.Count == 0) return;
        _carry += Math.Max(0, delta) * Rac1PulsingOrbSession.NativeUpdateHz;
        while (_carry >= 1)
        {
            _carry--;
            foreach (var entry in _materials)
            {
                var rgb = _session.Step(entry.Key);
                var colour = new Color(rgb.Red / 255f, rgb.Green / 255f, rgb.Blue / 255f);
                foreach (var material in entry.Value) material.AlbedoColor = colour;
            }
        }
    }

    public override void Dispose()
    {
        foreach (var (surface, original, copy) in _overrides)
        {
            if (GodotObject.IsInstanceValid(surface)) surface.MaterialOverride = original;
            if (GodotObject.IsInstanceValid(copy)) copy.Dispose();
        }
        _overrides.Clear();
        _materials.Clear();
        _carry = 0;
    }
}

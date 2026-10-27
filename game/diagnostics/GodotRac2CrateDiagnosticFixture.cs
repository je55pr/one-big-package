using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

/// <summary>Copies source-qualified class-500 scene facts; no private root access or gameplay session.</summary>
internal sealed class GodotRac2CrateDiagnosticFixture(IReadOnlyList<RuntimeWorldScene.DynamicObjectNode> nodes)
{
    public Rac2CrateDiagnosticProbe Probe { get; } = new();
    private RuntimeWorldScene.DynamicObjectNode? _target;
    private RuntimeWorldScene.DynamicObjectNode? Target
    {
        get
        {
            if (Probe.TargetIndex is not { } index) return null;
            if (_target?.Source.InstanceIndex != index)
                _target = nodes.FirstOrDefault(node => node.Source.SourceGame == ObpSourceGame.Rac2 &&
                    node.Source.NativeClassId == 500 && node.Source.InstanceIndex == index);
            return _target;
        }
    }
    private Rac2CrateTargetSnapshot? ReadTarget() => Target is { } target ? Read(target) : null;
    private static Rac2CrateTargetSnapshot Read(RuntimeWorldScene.DynamicObjectNode node)
    {
        bool valid = GodotObject.IsInstanceValid(node.Root);
        var position = valid ? node.Root.GlobalPosition : Vector3.Zero;
        return new(node.Source.InteractionId, node.Source.NativeUid, node.Source.InstanceIndex,
            new(position.X, position.Y, position.Z), valid, valid && node.Root.Visible);
    }
    private static Rac2CrateRewardSnapshot Read(Rac2FreshBoltSession rewards) =>
        new(rewards.CollectedBolts, rewards.OutstandingPickupCount, rewards.DeferredBolts);
    public void Configure(bool focus, bool autoStrike)
    {
        if (!focus && !autoStrike) { Probe.Configure(false, false, null); return; }
        var target = nodes.Where(node => node.Source.SourceGame == ObpSourceGame.Rac2 && node.Source.NativeClassId == 500)
            .OrderBy(node => node.Source.InstanceIndex).FirstOrDefault();
        Probe.Configure(focus, autoStrike, target is null ? null : Read(target));
        if (Probe.FocusRequested && target is not null)
            GD.Print($"[crate-debug] target {target.Source.InteractionId} uid={target.Source.NativeUid?.ToString() ?? "?"}");
    }
    public bool TryGetFocusPose(RuntimeWorld world, out Vector3 spawn, out float yaw)
    {
        System.Numerics.Vector3? ship = null;
        if (world.Ship is { } value)
        {
            var scene = RuntimeWorldScene.ToScene(value.X, value.Y, value.Z);
            ship = new(scene.X, scene.Y, scene.Z);
        }
        bool found = Probe.TryGetFocusPose(Target is { } target ? Read(target) : null, ship, out var position, out yaw);
        spawn = new(position.X, position.Y, position.Z);
        return found;
    }
    public string HudLine(Rac2FreshBoltSession rewards) => Probe.Status == "off" ? string.Empty :
        Probe.HudLine(ReadTarget(), Read(rewards));
    public Rac2CrateDebugSnapshot? Capture(Rac2FreshBoltSession rewards) => Probe.Status == "off" ? null :
        Probe.Capture(ReadTarget(), Read(rewards));
}

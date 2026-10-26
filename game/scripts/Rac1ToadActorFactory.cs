using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record Rac1ActorBindings(Rac1Class749HostileSession Hostiles,
    IDictionary<int, RuntimeWorldScene.DynamicObjectNode> Nodes, IDictionary<int, Rac1HostedEnemy> Enemies,
    Func<RuntimeWorldScene.DynamicObjectNode, ActorObservation> Observe,
    Func<RuntimeWorldScene.DynamicObjectNode, WeaponDamageIntent, ActorHitResult> Damage,
    Action<WeaponTargetKey> ClearTransients, Action<int> ClearCaptures, Action<RuntimeWorld> EnsurePickupBank,
    Action<ActorTickFacts> Tick);

/// <summary>Godot binding of the recovered LEVEL0 class-749 target/home recipe.</summary>
internal sealed class Rac1ToadActorFactory(ActorSceneFactory scenes, ActorSourceCache assets, Rac1ActorBindings native) : IActorFactory
{
    public ActorAdapterDescriptor Descriptor { get; } = new(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, 749,
        "rac1/class749/v1", "rac1/veldin-target-home/v1", 0, Rac1Class749Hostile.TargetSearchNativeState);

    public void RegisterUpdateGroups(ActorRuntimeSet actors) => actors.RegisterUpdateGroup("rac1/native-enemies", native.Tick);

    public HostedActor Create(ActorPlacement placement, RuntimeWorld library, ActorSourceVersion source)
    {
        var scene = scenes.Create(placement, library, source => Rac1Class749Hostile.ReadAuthored(source) is { Health: 1 });
        var node = scene.Node;
        bool registered = false;
        void Retire()
        {
            try { native.ClearTransients(scene.Registration.Instance.Target); }
            finally
            {
                if (native.Enemies.Remove(node.Source.InstanceIndex, out var enemy) &&
                    enemy.Flame is { } flame && GodotObject.IsInstanceValid(flame)) flame.QueueFree();
                native.Nodes.Remove(node.Source.InstanceIndex);
                if (registered) native.Hostiles.Unregister(node.Source);
                native.ClearCaptures(node.Source.InstanceIndex);
            }
        }
        try
        {
            var position = node.Root.GlobalPosition;
            var probe = native.Hostiles.Register(node.Source, node.State, new(-position.X, position.Y, position.Z));
            registered = true;
            native.Nodes.Add(node.Source.InstanceIndex, node);
            var asset = assets.Asset(source, placement.LevelId, placement.NativeClassId, "rac1/moby-animation", () =>
            {
                using var reader = new FileRandomAccessReader(source.Path);
                var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == placement.LevelId);
                var core = Rac1LevelCore.Open(reader, level);
                var cls = Rac1StaticClasses.Read(core).Mobies[placement.NativeClassId];
                return Rac1MobyAnimationAssets.Bake(cls, node.Source.Meshes, core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength));
            });
            node.Root.Scale *= Rac1EnemyMotion.Class749Scale;
            var body = scene.AddBody();
            var forward = (node.Root.ToGlobal(new Vector3(-1, 0, 0)) - node.Root.GlobalPosition).Normalized();
            var effectForward = new System.Numerics.Vector3(-forward.X, forward.Z, forward.Y);
            var enemy = new Rac1HostedEnemy(node, asset, body, new Rac1EnemyMotion(749, MathF.Atan2(effectForward.Y, effectForward.X)), probe.NativeSequence ?? 1);
            node.ConfigureAnimations(asset.Presentation); node.ApplyAnimationSample(enemy.Animation.Sample);
            native.Enemies.Add(node.Source.InstanceIndex, enemy);
            // Only relocation/home is authored by OBP; steering, reactions and rewards remain source-owned.
            native.EnsurePickupBank(library);
            return new(scene.Registration, node, () => native.Observe(node), intent => native.Damage(node, intent),
                retire: Retire, resources: scene);
        }
        catch { try { Retire(); } finally { scene.Dispose(); } throw; }
    }
}

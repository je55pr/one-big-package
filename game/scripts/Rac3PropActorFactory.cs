using OBP.Core;
using OBP.Fusion;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed class Rac3PropActorFactory(ActorSceneFactory scenes) : IActorFactory
{
    public bool SupportsDeterministicReplay => true;
    public ActorAdapterDescriptor Descriptor { get; } = new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, 500,
        "rac3/class500/v1", "uya/table1-class500-deactivate/v1", 1, Rac3Class500Destructible.ActiveNativeState);

    public void RegisterUpdateGroups(ActorRuntimeSet actors) { /* Native class-500 is an event consumer. */ }

    public HostedActor Create(ActorPlacement placement, RuntimeWorld library, ActorSourceVersion source)
    {
        var scene = scenes.Create(placement, library, source =>
            Rac3Class500Destructible.PostBreakRoute(Rac3Class500Destructible.ReadAuthored(source)) == Rac3Class500PostBreakRoute.Deactivate);
        var node = scene.Node;
        try
        {
            var runtime = new Rac3MobyRuntimeSession(); runtime.Register(node.Source); _ = new Rac3Class500DestructibleSession(runtime);
            var body = scene.AddBody();
            var registration = scene.Registration with
            {
                Capabilities = scene.Registration.Capabilities.Concat(new[]
                {
                    new ActorCapabilityRegistration("break-presentation", CapabilityAdmission.Unimplemented, "The recovered UYA consumer deactivates the target; original break SFX/scatter presentation is not yet recovered."),
                }).ToArray()
            };
            return new(registration, node, () => new(
                new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z),
                runtime.Require(node.Source).IsActive, !runtime.Require(node.Source).IsActive, null,
                runtime.Require(node.Source).IsActive ? Rac3Class500Destructible.ActiveNativeState : Rac3Class500Destructible.BreakTransitionNativeState, null), intent =>
            {
                var damage = new Rac3GameplayDamageEvent(Rac3GameplayEntityRef.Player,
                    Rac3GameplayEntityRef.Moby(new(node.Source.NativeClassId, node.Source.InstanceIndex)), intent.SourceDamage, 1);
                if (!runtime.TryDispatchDamage<Rac3Class500BreakResult>(damage, out var result) || result is null)
                    return new(false, false, "Native UYA class-500 receiver rejected this contact.");
                node.ApplyState(result.EntityState); body.CollisionLayer = body.CollisionMask = 0;
                return new(true, true);
            }, resources: scene);
        }
        catch { scene.Dispose(); throw; }
    }
}

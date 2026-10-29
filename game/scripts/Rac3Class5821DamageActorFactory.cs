using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// TABLE1 class-5821 incoming-damage admission only. This is NOT a recovered
/// moving Tyhrranoid: the visual variant, native locomotion, attack scheduler,
/// animation selection, terminal reaction and reward policy are unverified.
/// Never advertise it as a playable native enemy until those facts are proved.
/// </summary>
internal sealed class Rac3Class5821DamageActorFactory(ActorSceneFactory scenes) : IActorFactory
{
    public ActorAdapterDescriptor Descriptor { get; } = new(
        ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, Rac3Class5821Actor.NativeClassId,
        "rac3/class5821-damage-only/v1", "uya/table1-class5821-damage-only/v1",
        1, Rac3Class5821Actor.NativeDormantState);

    public void RegisterUpdateGroups(ActorRuntimeSet actors)
    {
        // The class's full state update is not recovered. No fabricated tick.
    }

    public HostedActor Create(ActorPlacement placement, RuntimeWorld library, ActorSourceVersion source)
    {
        var scene = scenes.Create(placement, library, candidate =>
            Rac3Class5821Admission.TryRead(candidate, out _, out _));
        try
        {
            var node = scene.Node;
            var native = new Rac3Class5821DamagePort(node.Source);
            var body = scene.AddBody();
            var registration = scene.Registration with
            {
                Capabilities =
                [
                    new("damage", CapabilityAdmission.Ready),
                    new("native-update", CapabilityAdmission.Unimplemented,
                        "UYA class-5821 native locomotion, targeting and attack progression are not recovered."),
                    new("animation", CapabilityAdmission.Unimplemented,
                        "The TABLE1 class-5821 skeletal clips are not animation-safe or native-state selected."),
                    new("terminal-reaction", CapabilityAdmission.Unimplemented,
                        "Native death, despawn and reward transitions after exhausted lifetime are not recovered."),
                    new("variant-identity", CapabilityAdmission.Unimplemented,
                        "Class 5821 has not been visually verified as the one-eyed Tyhrranoid.")
                ]
            };
            ActorObservation Observe()
            {
                float life = native.Lifetime;
                return new(
                    new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z),
                    native.HasNativePresence && life > 0f,
                    !native.HasNativePresence,
                    life, Rac3Class5821Actor.NativeDormantState, null);
            }
            ActorHitResult Apply(WeaponDamageIntent intent)
            {
                var result = native.Apply(intent);
                if (result.Applied && native.Lifetime <= 0f)
                    body.CollisionLayer = body.CollisionMask = 0; // OBP contact policy, not native death.
                return result;
            }
            return new(registration, node, Observe, Apply, resources: scene);
        }
        catch
        {
            scene.Dispose();
            throw;
        }
    }
}

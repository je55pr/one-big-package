using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC2;
using OBP.RAC2.Animation;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record Rac2ActorBindings(IDictionary<int, Rac2Class2827HostileSession> Hostiles,
    IDictionary<int, Rac2OpeningMsr1HostSession> Sessions, Func<bool> Current,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac2Class2827HostileSession, ActorObservation> Observe,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac2Class2827HostileSession, WeaponDamageIntent, ActorHitResult> Damage,
    Action<ActorTickFacts> Tick);

internal sealed class Rac2Msr1ActorFactory(ActorSceneFactory scenes, ActorSourceCache assets, Rac2ActorBindings native) : IActorFactory
{
    public bool SupportsDeterministicReplay => true;
    public ActorAdapterDescriptor Descriptor { get; } = new(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, 2827,
        "rac2/class2827/v1", "gc/aranos-chase-resume/v1", 0, Rac2Class2827HostileSession.ChaseNativeState);

    public void RegisterUpdateGroups(ActorRuntimeSet actors) => actors.RegisterUpdateGroup("rac2/native-class2827", native.Tick);

    public HostedActor Create(ActorPlacement placement, RuntimeWorld library, ActorSourceVersion source)
    {
        if (!native.Current()) throw new InvalidOperationException("R&C2 actor world lease has retired.");
        var scene = scenes.Create(placement, library, source => Rac2Class2827HostileSession.ReadAuthored(source) is
        { AuthoredMode: Rac2Class2827HostileSession.OpeningRoomAuthoredMode });
        var node = scene.Node;
        Rac2Class2827HostileSession? owned = null;
        void Retire()
        {
            if (native.Hostiles.TryGetValue(node.Source.InstanceIndex, out var live) && ReferenceEquals(live, owned))
                native.Hostiles.Remove(node.Source.InstanceIndex);
            if (native.Sessions.TryGetValue(node.Source.InstanceIndex, out var session) && ReferenceEquals(session.Node, node))
                native.Sessions.Remove(node.Source.InstanceIndex);
        }
        try
        {
            var hostile = new Rac2Class2827HostileSession(node.Source, node.State);
            owned = hostile;
            hostile.NativeState!.Transition(Rac2Class2827HostileSession.ChaseNativeState, 4);
            var hosted = new Rac2OpeningMsr1HostSession(node, hostile) { Phase = Rac2OpeningMsr1Phase.Chase, AttackCount = 1 };
            var registration = scene.Registration with
            {
                Capabilities = scene.Registration.Capabilities.Concat(new[]
                {
                    new ActorCapabilityRegistration("enemy-audio", CapabilityAdmission.Unimplemented, "Original GC enemy sound dispatch is not recovered by the bounded Aranos adapter."),
                    new ActorCapabilityRegistration("reward-presentation", CapabilityAdmission.Unimplemented, "Authored GC bolts are retained as source facts; native death scatter/pickup presentation is not recovered."),
                }).ToArray()
            };
            native.Hostiles.Add(node.Source.InstanceIndex, hostile); native.Sessions.Add(node.Source.InstanceIndex, hosted);
            _ = scene.AddBody();
            var animation = assets.Asset(source, placement.LevelId, placement.NativeClassId, "rac2/msr1-animation",
                () => Rac2Class2827Presentation.Load(source.Path, node.Source.Meshes,
                    nativePosePreview: OBP.RAC2.Level.Rac2MobyClasses.NativePoseEnabledForLevel(placement.LevelId)));
            node.ConfigureAnimations(animation.Presentation);
            // Reuse the original source approach/attack cycle, starting at the admitted resumed chase.
            return new(registration, node,
                () => native.Current() && native.Hostiles.TryGetValue(node.Source.InstanceIndex, out var live) && ReferenceEquals(live, hostile)
                    ? native.Observe(node, hostile) : new(System.Numerics.Vector3.Zero, false, true, null, 0, null),
                intent => native.Current() && native.Hostiles.TryGetValue(node.Source.InstanceIndex, out var live) && ReferenceEquals(live, hostile)
                    ? native.Damage(node, hostile, intent) : new(false, false, "The R&C2 encounter has retired."), tick: _ =>
            {
                if (!native.Current()) return;
                int sequence = hosted.Phase switch
                {
                    Rac2OpeningMsr1Phase.Chase => Rac2Class2827HostileSession.ChaseSequence,
                    Rac2OpeningMsr1Phase.Attack => hosted.AttackSequence,
                    Rac2OpeningMsr1Phase.Recovery => Rac2Class2827HostileSession.RecoverySequence,
                    _ => throw new InvalidOperationException("Unregistered GC encounter phase."),
                };
                node.ApplyAnimationSample(Rac2Class2827Presentation.Sample(animation, sequence, hostile.NativeState!.StateTicks));
            }, retire: Retire, resources: scene);
        }
        catch { try { Retire(); } finally { scene.Dispose(); } throw; }
    }
}

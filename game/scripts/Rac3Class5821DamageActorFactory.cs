using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Damage-only adapter for the retail-identified one-eyed UYA Tyhrranoid
/// (TABLE1 oClass 5821). Only visual class identity and incoming lifetime
/// damage are currently supported. Native locomotion, attack scheduling,
/// animation selection, death/rewards and audio remain unrecovered.
/// </summary>
internal sealed class Rac3Class5821DamageActorFactory(ActorSceneFactory scenes, ActorSourceCache assets) : IActorFactory
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
        // The source was checked at preparation, but repeat the build check at
        // construction so a stale/mismatched source cannot bypass this seam.
        if (source.Game != ObpSourceGame.Rac3 ||
            source.Build != Rac3Authority.Primary.BuildId ||
            placement.Source.Game != source.Game ||
            placement.Source.Build != source.Build ||
            library.BuildId != source.Build)
            throw new InvalidDataException("UYA class-5821 actor source/build changed before construction.");

        var scene = scenes.Create(placement, library, candidate =>
            Rac3Class5821Admission.TryRead(candidate, out _, out _));
        try
        {
            var node = scene.Node;
            var native = new Rac3Class5821DamagePort(node.Source);
            // Decode once per attached source/class and share the immutable frames
            // between every native actor. Do not start playing without a native
            // state-10 action clock from the still-unrecovered actor controller.
            // The supported legacy-pose comparison mode intentionally lacks the
            // full native skin stream. Keep damage-only actors available there.
            bool nativePose = OBP.RAC3.Level.Rac3Assets.NativeMobyPoseEnabled;
            RuntimeObjectAnimationClip? clip = null;
            if (nativePose)
            {
                clip = assets.Asset(source, placement.LevelId, placement.NativeClassId,
                    "uya/class5821-state10-native-action12/v1",
                    () => Rac3WorldImport.LoadClass5821State10Clip(source.Path, node.Source.Meshes));
                node.ConfigureAnimations(new([clip]));
            }
            var body = scene.AddBody();
            var registration = scene.Registration with
            {
                Capabilities =
                [
                    new("damage", CapabilityAdmission.Ready),
                    new("native-update", CapabilityAdmission.Unimplemented,
                        "UYA class-5821 native locomotion, targeting and attack progression are not recovered."),
                    new("native-state10-clip", nativePose ? CapabilityAdmission.Ready : CapabilityAdmission.Unimplemented,
                        nativePose ? null : "Full UYA skinning is disabled for the legacy-pose comparison; damage-only admission remains available."),
                    new("animation", CapabilityAdmission.Unimplemented,
                        "Native action 12 has 20 source-backed poseable frames; the native state-to-frame clock and other action selectors remain unrecovered."),
                    new("terminal-reaction", CapabilityAdmission.Unimplemented,
                        "Native death, despawn and reward transitions after exhausted lifetime are not recovered."),
                    new("variant-identity", CapabilityAdmission.Ready)
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
            // OBP-only diagnostic audition. No movement, target acquisition or
            // native state is advanced. It only exposes verified clip frames for
            // the eight-actor visual test, never ordinary UYA gameplay.
            Action<ActorTickFacts>? preview = null;
            if (placement.ScenarioGroup == "obp-eighth-tyhrranoid-visual-and-pyrocitor-diagnostic" && clip is not null)
            {
                long firstTick = -1;
                preview = facts =>
                {
                    if (!native.HasNativePresence || native.Lifetime <= 0f) return;
                    if (firstTick == -1) firstTick = facts.Tick;
                    node.ApplyAnimationSample(Rac3WorldImport.SampleClass5821Diagnostic(clip, facts.Tick - firstTick));
                    if (facts.Tick == firstTick)
                        GD.Print("[tyhrranoid-test] " + placement.Id + ": original action 12 playing on OBP-only diagnostic clock");
                };
            }
            return new(registration, node, Observe, Apply, tick: preview, resources: scene);
        }
        catch
        {
            scene.Dispose();
            throw;
        }
    }
}

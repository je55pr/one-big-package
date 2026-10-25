using OBP.Core;
using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private readonly Dictionary<(string Path, long Length, long Written, int Level, int Class), Rac1MobyAnimationAssets.Asset> _actorRac1Animations = [];
    private readonly Dictionary<(string Path, long Length, long Written), OBP.RAC2.Animation.Rac2Class2827Presentation.Asset> _actorRac2Animations = [];

    private HostedActor CreatePlacedRac1Toad(ActorPlacement placement, RuntimeWorld library, string path)
    {
        if (placement.Source.Game != ObpSourceGame.Rac1 || placement.NativeClassId != 749 || placement.LevelId != 0 ||
            placement.InitialNativeState != Rac1Class749Hostile.TargetSearchNativeState || placement.NativeContextId != "rac1/veldin-target-home/v1")
            throw new NotSupportedException("R&C1 toad requires its recovered LEVEL0 target/home context and state 5.");
        var (scene, node, registration) = CreateActorView(placement, library, source => Rac1Class749Hostile.ReadAuthored(source) is { Health: 1 });
        bool registered = false;
        try
        {
            var position = node.Root.GlobalPosition;
            var probe = _rac1Hostiles.Register(node.Source, node.State, new(-position.X, position.Y, position.Z));
            registered = true;
            _rac1HostileNodes.Add(node.Source.InstanceIndex, node);
            var stamp = SourceFileStamp(path)!.Value;
            var assetKey = (path, stamp.Length, stamp.Written, placement.LevelId, placement.NativeClassId);
            if (!_actorRac1Animations.TryGetValue(assetKey, out var asset))
            {
                using var reader = new FileRandomAccessReader(path);
                var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == placement.LevelId);
                var core = Rac1LevelCore.Open(reader, level);
                var cls = Rac1StaticClasses.Read(core).Mobies[placement.NativeClassId];
                asset = Rac1MobyAnimationAssets.Bake(cls, node.Source.Meshes, core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength));
                _actorRac1Animations.Add(assetKey, asset);
            }
            node.Root.Scale *= Rac1EnemyMotion.Class749Scale;
            var body = AddRegisteredActorBody(node, scene.Root);
            var forward = ToEffectNative(EnemyForward(node));
            var enemy = new Rac1HostedEnemy(node, asset, body, new Rac1EnemyMotion(749, MathF.Atan2(forward.Y, forward.X)), probe.NativeSequence ?? 1);
            node.ConfigureAnimations(asset.Presentation); node.ApplyAnimationSample(enemy.Animation.Sample);
            _rac1Enemies.Add(node.Source.InstanceIndex, enemy);
            // OBP relocation supplies a new home and ordinary target search;
            // native steering, animation gates, damage and death remain reused.
            EnsureRac1ActorPickupBank(library);
            return new(registration, node, () => ObserveRac1Actor(node), intent => ApplyRegisteredRac1Damage(node, intent), retire: () =>
            {
                ClearRegisteredActorTransients(ActorTarget(node));
                _rac1Enemies.Remove(node.Source.InstanceIndex); _rac1HostileNodes.Remove(node.Source.InstanceIndex);
                _rac1Hostiles.Unregister(node.Source); _actorContactBodies.Remove(ActorTarget(node));
                _rac1Captures.RemoveAll(capture => capture.Key == new Rac1MobyRuntimeKey(749, node.Source.InstanceIndex));
                body.CollisionLayer = body.CollisionMask = 0; scene.Root.QueueFree();
            });
        }
        catch
        {
            _rac1HostileNodes.Remove(node.Source.InstanceIndex); _rac1Enemies.Remove(node.Source.InstanceIndex);
            if (registered) _rac1Hostiles.Unregister(node.Source);
            _actorContactBodies.Remove(ActorTarget(node)); scene.Root.QueueFree(); throw;
        }
    }

    private void EnsureRac1ActorPickupBank(RuntimeWorld library)
    {
        if (_rac1PickupAssets is not null) return;
        _rac1PickupAssets = _selectedRac1PresentationAssets ?? throw new InvalidDataException("R&C1 actor's original sound/pickup bank is unavailable.");
        _rac1PickupMaterials = new(library with { Textures = _rac1PickupAssets.Textures });
        _actors!.RegisterUpdateGroup("rac1/actor-pickups", _ => TickRac1Pickups(1d / 60));
    }

    private HostedActor CreatePlacedRac2Hostile(ActorPlacement placement, RuntimeWorld library, string path)
    {
        if (placement.Source.Game != ObpSourceGame.Rac2 || placement.NativeClassId != 2827 || placement.LevelId != 0 ||
            placement.InitialNativeState != Rac2Class2827HostileSession.ChaseNativeState || placement.NativeContextId != "gc/aranos-chase-resume/v1")
            throw new NotSupportedException("GC MSR I requires its admitted Aranos chase/attack context and state 12.");
        var (scene, node, registration) = CreateActorView(placement, library, source => Rac2Class2827HostileSession.ReadAuthored(source) is { AuthoredMode: Rac2Class2827HostileSession.OpeningRoomAuthoredMode });
        try
        {
            var native = new Rac2Class2827HostileSession(node.Source, node.State);
            native.NativeState!.Transition(Rac2Class2827HostileSession.ChaseNativeState, 4);
            var hosted = new Rac2OpeningMsr1HostSession(node, native) { Phase = Rac2OpeningMsr1Phase.Chase, AttackCount = 1 };
            registration = registration with
            {
                Capabilities = registration.Capabilities.Concat(new[]
            {
                new ActorCapabilityRegistration("enemy-audio", CapabilityAdmission.Unimplemented, "Original GC enemy sound dispatch is not recovered by the bounded Aranos adapter."),
                new ActorCapabilityRegistration("reward-presentation", CapabilityAdmission.Unimplemented, "Authored GC bolts are retained as source facts; native death scatter/pickup presentation is not recovered."),
            }).ToArray()
            };
            _rac2Class2827Hostiles.Add(node.Source.InstanceIndex, native); _rac2OpeningMsr1.Add(node.Source.InstanceIndex, hosted);
            var body = AddRegisteredActorBody(node, scene.Root);
            var stamp = SourceFileStamp(path)!.Value;
            var animationKey = (path, stamp.Length, stamp.Written);
            if (!_actorRac2Animations.TryGetValue(animationKey, out var animation))
            { animation = OBP.RAC2.Animation.Rac2Class2827Presentation.Load(path, node.Source.Meshes); _actorRac2Animations.Add(animationKey, animation); }
            node.ConfigureAnimations(animation.Presentation);
            // OBP starts at the recovered resumed chase, not the source level's
            // lift/door intro predicates. The source approach and attack cycle
            // are exactly those used by the original Aranos adapter.
            return new(registration, node,
                () => ObserveRac2Actor(node, native), intent => ApplyRegisteredRac2Damage(node, native, intent), tick: _ =>
            {
                int sequence = hosted.Phase switch
                {
                    Rac2OpeningMsr1Phase.Chase => Rac2Class2827HostileSession.ChaseSequence,
                    Rac2OpeningMsr1Phase.Attack => hosted.AttackSequence,
                    Rac2OpeningMsr1Phase.Recovery => Rac2Class2827HostileSession.RecoverySequence,
                    _ => throw new InvalidOperationException("Unregistered GC encounter phase."),
                };
                node.ApplyAnimationSample(OBP.RAC2.Animation.Rac2Class2827Presentation.Sample(animation, sequence, native.NativeState!.StateTicks));
            }, retire: () =>
            {
                _rac2Class2827Hostiles.Remove(node.Source.InstanceIndex); _rac2OpeningMsr1.Remove(node.Source.InstanceIndex);
                _actorContactBodies.Remove(ActorTarget(node)); body.CollisionLayer = body.CollisionMask = 0; scene.Root.QueueFree();
            });
        }
        catch
        {
            _rac2Class2827Hostiles.Remove(node.Source.InstanceIndex); _rac2OpeningMsr1.Remove(node.Source.InstanceIndex);
            _actorContactBodies.Remove(ActorTarget(node)); scene.Root.QueueFree(); throw;
        }
    }

    private HostedActor CreatePlacedUyaProp(ActorPlacement placement, RuntimeWorld library, string path)
    {
        if (placement.Source.Game != ObpSourceGame.Rac3 || placement.NativeClassId != 500 || placement.LevelId != 1 ||
            placement.InitialNativeState != UyaClass500Destructible.ActiveNativeState || placement.NativeContextId != "uya/table1-class500-deactivate/v1")
            throw new NotSupportedException("UYA target requires its recovered TABLE1 class-500 deactivation context.");
        var (scene, node, registration) = CreateActorView(placement, library, source => UyaClass500Destructible.PostBreakRoute(UyaClass500Destructible.ReadAuthored(source)) == UyaClass500PostBreakRoute.Deactivate);
        try
        {
            var runtime = new UyaMobyRuntimeSession(); runtime.Register(node.Source); _ = new UyaClass500DestructibleSession(runtime);
            var body = AddRegisteredActorBody(node, scene.Root);
            registration = registration with
            {
                Capabilities = registration.Capabilities.Concat(new[]
            {
                new ActorCapabilityRegistration("break-presentation", CapabilityAdmission.Unimplemented, "The recovered UYA consumer deactivates the target; original break SFX/scatter presentation is not yet recovered."),
            }).ToArray()
            };
            return new(registration, node, () => ObserveUyaActor(node, runtime), intent => ApplyRegisteredUyaDamage(node, runtime, intent), retire: () =>
            { _actorContactBodies.Remove(ActorTarget(node)); body.CollisionLayer = body.CollisionMask = 0; scene.Root.QueueFree(); });
        }
        catch { _actorContactBodies.Remove(ActorTarget(node)); scene.Root.QueueFree(); throw; }
    }

    private void RegisterExistingNativeTargets()
    {
        foreach (var node in Rac1EnemyNodes.Concat(_rac1CrateNodes).DistinctBy(ActorTarget))
            RegisterOriginalActor(node, () => ObserveRac1Actor(node), intent => ApplyRegisteredRac1Damage(node, intent));
        foreach (var hostile in _rac2Class2827Hostiles.Values)
        {
            var node = _rac2OpeningMsr1.GetValueOrDefault(hostile.InstanceIndex)?.Node;
            if (node is not null) RegisterOriginalActor(node, () => ObserveRac2Actor(node, hostile), intent => ApplyRegisteredRac2Damage(node, hostile, intent));
        }
        foreach (var node in _uyaClass500Nodes)
            RegisterOriginalActor(node, () => ObserveUyaActor(node, _uyaMobyRuntime), intent => ApplyRegisteredUyaDamage(node, _uyaMobyRuntime, intent));
    }

    private void RegisterOriginalActor(RuntimeWorldScene.DynamicObjectNode node, Func<ActorObservation> observe, Func<WeaponDamageIntent, ActorHitResult> damage)
    {
        string build = _sources.Attached.Values.Single(source => source.Game == node.Source.SourceGame).Identity.BuildId;
        var registration = new ActorRegistration(new(_actors!.WorldInstanceId, _actors.Generation, ActorTarget(node)),
            new(node.Source.SourceGame, build, node.Source.InteractionId), "source-world/" + SourceIdentityWire.FormatSource(node.Source.SourceGame),
            node.Source.InteractionId, [new("damage", CapabilityAdmission.Ready), new("native-update", CapabilityAdmission.Ready)]);
        RegisterActorView(new(registration, node, observe, damage));
    }

    private ActorObservation ObserveRac1Actor(RuntimeWorldScene.DynamicObjectNode node)
    {
        var native = _rac1MobyRuntime.Require(node.Source);
        double? health = node.Source.NativeClassId == 749 ? _rac1Hostiles.Probe(node.Source).Health : null;
        return new(ToNumerics(node.Root.GlobalPosition), native.IsActive && !(health <= 0) &&
            (!_rac1Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy) || !enemy.RewardEmitted), !native.IsActive,
            health, native.State.NativeState, node.Source.NativeClassId == 749 ? _rac1Hostiles.Probe(node.Source).NativeSequence : null);
    }

    private static ActorObservation ObserveRac2Actor(RuntimeWorldScene.DynamicObjectNode node, Rac2Class2827HostileSession native) =>
        new(ToNumerics(node.Root.GlobalPosition), !native.IsTerminal, native.IsTerminal, native.Health, native.NativeState?.CurrentState ?? 0, null);

    private static ActorObservation ObserveUyaActor(RuntimeWorldScene.DynamicObjectNode node, UyaMobyRuntimeSession native) =>
        new(ToNumerics(node.Root.GlobalPosition), native.Require(node.Source).IsActive, !native.Require(node.Source).IsActive, null,
            native.Require(node.Source).IsActive ? UyaClass500Destructible.ActiveNativeState : UyaClass500Destructible.BreakTransitionNativeState, null);

    private ActorHitResult ApplyRegisteredRac1Damage(RuntimeWorldScene.DynamicObjectNode node, WeaponDamageIntent intent)
    {
        var translated = FusionDamagePolicy.Translate(intent);
        if (translated is null) return new(false, false, "The native R&C1 receiver does not admit this source damage; class-749 sublethal reactions are unrecovered.");
        bool applied = ApplyRac1AdditionalDamage(node, translated);
        return new(applied, !ObserveRac1Actor(node).Alive, applied ? null : "Native R&C1 immunity or reaction gate.");
    }

    private ActorHitResult ApplyRegisteredRac2Damage(RuntimeWorldScene.DynamicObjectNode node, Rac2Class2827HostileSession native, WeaponDamageIntent intent)
    {
        // OBP adapter: retain the source amount; create a GC-owned mask/record.
        // Foreign flag bits and class numbers never become native GC payloads.
        var result = native.Apply(new(Rac2GameplayEntityRef.Player, Rac2GameplayEntityRef.Moby(node.Source),
            new(Rac2Class2827HostileSession.DamageQueryMask, 0, 0, 0, intent.SourceDamage, 0)));
        node.ApplyState(result.EntityState);
        if (result.Terminal && _actorContactBodies.TryGetValue(ActorTarget(node), out var body)) body.CollisionLayer = body.CollisionMask = 0;
        return new(result.Admitted, result.Terminal, result.Admitted ? null : "Native GC damage cooldown or terminal state.");
    }

    private ActorHitResult ApplyRegisteredUyaDamage(RuntimeWorldScene.DynamicObjectNode node, UyaMobyRuntimeSession native, WeaponDamageIntent intent)
    {
        var damage = new UyaGameplayDamageEvent(UyaGameplayEntityRef.Player,
            UyaGameplayEntityRef.Moby(new(node.Source.NativeClassId, node.Source.InstanceIndex)), intent.SourceDamage, 1);
        if (!native.TryDispatchDamage<UyaClass500BreakResult>(damage, out var result) || result is null) return new(false, false, "Native UYA class-500 receiver rejected this contact.");
        node.ApplyState(result.EntityState);
        if (_actorContactBodies.TryGetValue(ActorTarget(node), out var body)) body.CollisionLayer = body.CollisionMask = 0;
        return new(true, true);
    }
}

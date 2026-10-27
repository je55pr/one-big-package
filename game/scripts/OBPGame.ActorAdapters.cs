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

namespace OBP.Game;

public partial class OBPGame
{
    private void ConfigureActorFactories()
    {
        var scenes = new ActorSceneFactory(_actors!, _sceneResult!.Root, _actorWorld.Bodies);
        var rac1 = new Rac1ActorBindings(_rac1Hostiles, _rac1HostileNodes, _rac1Enemies, ObserveRac1Actor,
            ApplyRegisteredRac1Damage, ClearRegisteredActorTransients,
            index => _rac1Captures.RemoveAll(capture => capture.Key == new Rac1MobyRuntimeKey(749, index)), EnsureRac1ActorPickupBank,
            _ => { if (_rac1Enemies.Count > 0) TickRac1Hostiles(1d / 60); });
        var rac2 = new Rac2ActorBindings(_rac2Class2827Hostiles, _rac2OpeningMsr1, ObserveRac2Actor, ApplyRegisteredRac2Damage, _ =>
        {
            foreach (var hostile in _rac2Class2827Hostiles.Values) hostile.TickCooldown();
            TickRac2OpeningMsr1NativeTick();
        });
        _actorWorld.Factories = ActorFactoryRegistrations.Create(_actors!,
            new Rac1ToadActorFactory(scenes, ActorSources, rac1), new Rac2Msr1ActorFactory(scenes, ActorSources, rac2),
            new Rac3PropActorFactory(scenes));
    }

    private void EnsureRac1ActorPickupBank(RuntimeWorld library)
    {
        if (_rac1PickupAssets is not null) return;
        _rac1PickupAssets = _selectedRac1PresentationAssets ?? throw new InvalidDataException("R&C1 actor's original sound/pickup bank is unavailable.");
        _rac1PickupMaterials = new(library with { Textures = _rac1PickupAssets.Textures });
        _actors!.RegisterUpdateGroup("rac1/actor-pickups", _ => TickRac1Pickups(1d / 60));
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
        foreach (var node in _rac3Class500Nodes)
            RegisterOriginalActor(node, () => ObserveRac3Actor(node, _rac3MobyRuntime), intent => ApplyRegisteredRac3Damage(node, _rac3MobyRuntime, intent));
    }

    private void RegisterOriginalActor(RuntimeWorldScene.DynamicObjectNode node, Func<ActorObservation> observe, Func<WeaponDamageIntent, ActorHitResult> damage)
    {
        string build = Sources.Setup.Library.Attached.Values.Single(source => source.Game == node.Source.SourceGame).Identity.BuildId;
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

    private static ActorObservation ObserveRac3Actor(RuntimeWorldScene.DynamicObjectNode node, Rac3MobyRuntimeSession native) =>
        new(ToNumerics(node.Root.GlobalPosition), native.Require(node.Source).IsActive, !native.Require(node.Source).IsActive, null,
            native.Require(node.Source).IsActive ? Rac3Class500Destructible.ActiveNativeState : Rac3Class500Destructible.BreakTransitionNativeState, null);

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
        if (result.Terminal && _actorWorld.Bodies.TryGetValue(ActorTarget(node), out var body)) body.CollisionLayer = body.CollisionMask = 0;
        return new(result.Admitted, result.Terminal, result.Admitted ? null : "Native GC damage cooldown or terminal state.");
    }

    private ActorHitResult ApplyRegisteredRac3Damage(RuntimeWorldScene.DynamicObjectNode node, Rac3MobyRuntimeSession native, WeaponDamageIntent intent)
    {
        var damage = new Rac3GameplayDamageEvent(Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(new(node.Source.NativeClassId, node.Source.InstanceIndex)), intent.SourceDamage, 1);
        if (!native.TryDispatchDamage<Rac3Class500BreakResult>(damage, out var result) || result is null) return new(false, false, "Native UYA class-500 receiver rejected this contact.");
        node.ApplyState(result.EntityState);
        if (_actorWorld.Bodies.TryGetValue(ActorTarget(node), out var body)) body.CollisionLayer = body.CollisionMask = 0;
        return new(true, true);
    }
}

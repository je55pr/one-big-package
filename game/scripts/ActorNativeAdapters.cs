using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record Rac1NativeActorPorts(Func<Rac1MobyRuntimeSession> Runtime, Func<Rac1Class749HostileSession> Hostiles,
    IDictionary<int, RuntimeWorldScene.DynamicObjectNode> Nodes, IDictionary<int, Rac1HostedEnemy> Enemies,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> OriginalTargets,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> ApplyDamage, Action<Rac1MobyRuntimeKey> ClearCaptures,
    Func<Rac1PickupAssets.Catalogue?> PickupBank, Func<Rac1PickupAssets.Catalogue?> SelectedBank,
    Action<Rac1PickupAssets.Catalogue, WorldMaterialFactory> AttachPickupBank,
    Action<double> TickHostiles, Action<double> TickPickups);
internal sealed record Rac2NativeActorPorts(IDictionary<int, Rac2Class2827HostileSession> Hostiles,
    IDictionary<int, Rac2OpeningMsr1HostSession> Sessions, Action Tick);
internal sealed record Rac3NativeActorPorts(Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> OriginalTargets,
    Func<Rac3MobyRuntimeSession> Runtime);
internal sealed record ActorNativePorts(Rac1NativeActorPorts Rac1, Rac2NativeActorPorts Rac2, Rac3NativeActorPorts Rac3);
internal sealed record ActorNativeCounts(int Rac1Enemies, int Rac1Hostiles);

/// <summary>Typed bridges into the existing source controllers. Native amounts, reaction gates and update clocks stay source-owned.</summary>
internal sealed class ActorNativeAdapters(ActorNativePorts ports, Action<WeaponTargetKey> clearTransients,
    Action<WeaponTargetKey> disableBody, Action<string, Action<ActorTickFacts>> registerUpdate)
{
    internal Rac1PickupAssets.Catalogue? PickupBank => ports.Rac1.PickupBank();
    internal ActorNativeCounts Counts => new(ports.Rac1.Enemies.Count, ports.Rac1.Nodes.Count);
    internal bool Rac2AnimationPresented => ports.Rac2.Sessions.Count == 1 && ports.Rac2.Sessions.Values.Single().Node.AnimationSample is not null;

    internal IReadOnlyList<IActorFactory> CreateFactories(ActorSceneFactory scenes, ActorSourceCache sources) =>
        [new Rac1ToadActorFactory(scenes, sources, Rac1Bindings()),
            new Rac2Msr1ActorFactory(scenes, sources, new(ports.Rac2.Hostiles, ports.Rac2.Sessions, ObserveRac2, ApplyRac2, _ =>
            {
                foreach (var hostile in ports.Rac2.Hostiles.Values) hostile.TickCooldown();
                ports.Rac2.Tick();
            })), new Rac3PropActorFactory(scenes)];

    private Rac1ActorBindings Rac1Bindings() => new(ports.Rac1.Hostiles(), ports.Rac1.Nodes, ports.Rac1.Enemies,
        ObserveRac1, ApplyRac1, clearTransients, index => ports.Rac1.ClearCaptures(new(749, index)), EnsurePickupBank,
        _ => { if (ports.Rac1.Enemies.Count > 0) ports.Rac1.TickHostiles(1d / 60); });

    internal IActorFactory ConstructionTrialFactory(ActorSceneFactory scenes, ActorSourceCache sources) =>
        new Rac1ToadActorFactory(scenes, sources, Rac1Bindings() with
        {
            EnsurePickupBank = _ => throw new InvalidDataException("actor construction fixture"),
            Tick = _ => { },
        });

    private void EnsurePickupBank(RuntimeWorld library)
    {
        if (ports.Rac1.PickupBank() is not null) return;
        var assets = ports.Rac1.SelectedBank() ?? throw new InvalidDataException("R&C1 actor's original sound/pickup bank is unavailable.");
        ports.Rac1.AttachPickupBank(assets, new(library with { Textures = assets.Textures }));
        registerUpdate("rac1/actor-pickups", _ => ports.Rac1.TickPickups(1d / 60));
    }

    internal void RegisterOriginalTargets(Action<RuntimeWorldScene.DynamicObjectNode, Func<ActorObservation>, Func<WeaponDamageIntent, ActorHitResult>> register)
    {
        foreach (var node in ports.Rac1.OriginalTargets().DistinctBy(ActorWorldHost.Target))
            register(node, () => ObserveRac1(node), intent => ApplyRac1(node, intent));
        foreach (var hostile in ports.Rac2.Hostiles.Values)
        {
            var node = ports.Rac2.Sessions.TryGetValue(hostile.InstanceIndex, out var session) ? session.Node : null;
            if (node is not null) register(node, () => ObserveRac2(node, hostile), intent => ApplyRac2(node, hostile, intent));
        }
        foreach (var node in ports.Rac3.OriginalTargets())
            register(node, () => ObserveRac3(node, ports.Rac3.Runtime()), intent => ApplyRac3(node, ports.Rac3.Runtime(), intent));
    }

    private ActorObservation ObserveRac1(RuntimeWorldScene.DynamicObjectNode node)
    {
        var native = ports.Rac1.Runtime().Require(node.Source);
        double? health = node.Source.NativeClassId == 749 ? ports.Rac1.Hostiles().Probe(node.Source).Health : null;
        return new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z), native.IsActive && !(health <= 0) &&
            (!ports.Rac1.Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy) || !enemy.RewardEmitted), !native.IsActive,
            health, native.State.NativeState, node.Source.NativeClassId == 749 ? ports.Rac1.Hostiles().Probe(node.Source).NativeSequence : null);
    }

    private static ActorObservation ObserveRac2(RuntimeWorldScene.DynamicObjectNode node, Rac2Class2827HostileSession native) =>
        new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z), !native.IsTerminal, native.IsTerminal,
            native.Health, native.NativeState?.CurrentState ?? 0, null);

    private static ActorObservation ObserveRac3(RuntimeWorldScene.DynamicObjectNode node, Rac3MobyRuntimeSession native) =>
        new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z), native.Require(node.Source).IsActive,
            !native.Require(node.Source).IsActive, null, native.Require(node.Source).IsActive
                ? Rac3Class500Destructible.ActiveNativeState : Rac3Class500Destructible.BreakTransitionNativeState, null);

    private ActorHitResult ApplyRac1(RuntimeWorldScene.DynamicObjectNode node, WeaponDamageIntent intent)
    {
        var translated = FusionDamagePolicy.Translate(intent);
        if (translated is null) return new(false, false, "The native R&C1 receiver does not admit this source damage; class-749 sublethal reactions are unrecovered.");
        bool applied = ports.Rac1.ApplyDamage(node, translated);
        return new(applied, !ObserveRac1(node).Alive, applied ? null : "Native R&C1 immunity or reaction gate.");
    }

    private ActorHitResult ApplyRac2(RuntimeWorldScene.DynamicObjectNode node, Rac2Class2827HostileSession native, WeaponDamageIntent intent)
    {
        // OBP adapter: retain the source amount; foreign flags/classes never become native GC payloads.
        var result = native.Apply(new(Rac2GameplayEntityRef.Player, Rac2GameplayEntityRef.Moby(node.Source),
            new(Rac2Class2827HostileSession.DamageQueryMask, 0, 0, 0, intent.SourceDamage, 0)));
        node.ApplyState(result.EntityState);
        if (result.Terminal) disableBody(ActorWorldHost.Target(node));
        return new(result.Admitted, result.Terminal, result.Admitted ? null : "Native GC damage cooldown or terminal state.");
    }

    private ActorHitResult ApplyRac3(RuntimeWorldScene.DynamicObjectNode node, Rac3MobyRuntimeSession native, WeaponDamageIntent intent)
    {
        var damage = new Rac3GameplayDamageEvent(Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(new(node.Source.NativeClassId, node.Source.InstanceIndex)), intent.SourceDamage, 1);
        if (!native.TryDispatchDamage<Rac3Class500BreakResult>(damage, out var result) || result is null)
            return new(false, false, "Native UYA class-500 receiver rejected this contact.");
        node.ApplyState(result.EntityState); disableBody(ActorWorldHost.Target(node));
        return new(true, true);
    }
}

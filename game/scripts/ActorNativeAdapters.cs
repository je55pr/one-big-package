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

internal sealed record Rac1NativeActorPorts(Func<Rac1EnemyHost?> Enemies,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> OriginalTargets,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> ApplyDamage, Action<Rac1MobyRuntimeKey> ClearCaptures,
    Func<Rac1PickupAssets.Catalogue?> PickupBank, Func<Rac1PickupAssets.Catalogue?> SelectedBank,
    Action<Rac1PickupAssets.Catalogue, WorldMaterialFactory> AttachPickupBank,
    Action<double> TickPickups);
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
    internal ActorNativeCounts Counts => new(ports.Rac1.Enemies()?.Roster.Count ?? 0, ports.Rac1.Enemies()?.Presentation.HostileNodes.Count ?? 0);
    internal bool Rac2AnimationPresented => ports.Rac2.Sessions.Count == 1 && ports.Rac2.Sessions.Values.Single().Node.AnimationSample is not null;

    internal IReadOnlyList<IActorFactory> CreateFactories(ActorSceneFactory scenes, ActorSourceCache sources) =>
        [new Rac1ToadActorFactory(scenes, sources, Rac1Bindings()),
            new Rac2Msr1ActorFactory(scenes, sources, new(ports.Rac2.Hostiles, ports.Rac2.Sessions, ObserveRac2, ApplyRac2, _ =>
            {
                foreach (var hostile in ports.Rac2.Hostiles.Values) hostile.TickCooldown();
                ports.Rac2.Tick();
            })), new Rac3PropActorFactory(scenes)];

    private Rac1ActorBindings Rac1Bindings()
    {
        var owner = ports.Rac1.Enemies() ?? throw new InvalidOperationException("R&C1 actors require an active world scene lease.");
        return new(owner.Context.Hostiles, owner.Presentation.HostileNodes, owner.Roster,
            node => ObserveRac1(node, owner), (node, intent) => ApplyRac1(node, intent, owner), clearTransients,
            index => ports.Rac1.ClearCaptures(new(749, index)), world => EnsurePickupBank(world, owner),
            _ => { if (owner.Roster.Count > 0) owner.TickRac1Hostiles(1d / 60); }, () => owner.Current);
    }

    internal IActorFactory ConstructionTrialFactory(ActorSceneFactory scenes, ActorSourceCache sources) =>
        new Rac1ToadActorFactory(scenes, sources, Rac1Bindings() with
        {
            EnsurePickupBank = _ => throw new InvalidDataException("actor construction fixture"),
            Tick = _ => { },
        });

    private void EnsurePickupBank(RuntimeWorld library, Rac1EnemyHost owner)
    {
        if (ports.Rac1.PickupBank() is not null) return;
        var assets = ports.Rac1.SelectedBank() ?? throw new InvalidDataException("R&C1 actor's original sound/pickup bank is unavailable.");
        ports.Rac1.AttachPickupBank(assets, new(library with { Textures = assets.Textures }));
        registerUpdate("rac1/actor-pickups", _ => { if (owner.Current) ports.Rac1.TickPickups(1d / 60); });
    }

    internal void RegisterOriginalTargets(Action<RuntimeWorldScene.DynamicObjectNode, Func<ActorObservation>, Func<WeaponDamageIntent, ActorHitResult>> register)
    {
        if (ports.Rac1.Enemies() is { } owner)
            foreach (var node in ports.Rac1.OriginalTargets().DistinctBy(ActorWorldHost.Target))
                register(node, () => ObserveRac1(node, owner), intent => ApplyRac1(node, intent, owner));
        foreach (var hostile in ports.Rac2.Hostiles.Values)
        {
            var node = ports.Rac2.Sessions.TryGetValue(hostile.InstanceIndex, out var session) ? session.Node : null;
            if (node is not null) register(node, () => ObserveRac2(node, hostile), intent => ApplyRac2(node, hostile, intent));
        }
        foreach (var node in ports.Rac3.OriginalTargets())
            register(node, () => ObserveRac3(node, ports.Rac3.Runtime()), intent => ApplyRac3(node, ports.Rac3.Runtime(), intent));
    }

    private static ActorObservation ObserveRac1(RuntimeWorldScene.DynamicObjectNode node, Rac1EnemyHost owner)
    {
        if (!owner.Current || !GodotObject.IsInstanceValid(node.Root) || node.Source.NativeClassId == 749 &&
            (!owner.Roster.TryGetValue(node.Source.InstanceIndex, out var registered) || !ReferenceEquals(registered.Node, node)))
            return new(System.Numerics.Vector3.Zero, false, true, null, 0, null);
        var native = owner.Context.MobyRuntime.Require(node.Source);
        double? health = node.Source.NativeClassId == 749 ? owner.Context.Hostiles.Probe(node.Source).Health : null;
        return new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z), native.IsActive && !(health <= 0) &&
            (!owner.Roster.TryGetValue(node.Source.InstanceIndex, out var enemy) || !enemy.RewardEmitted), !native.IsActive,
            health, native.State.NativeState, node.Source.NativeClassId == 749 ? owner.Context.Hostiles.Probe(node.Source).NativeSequence : null);
    }

    private static ActorObservation ObserveRac2(RuntimeWorldScene.DynamicObjectNode node, Rac2Class2827HostileSession native) =>
        new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z), !native.IsTerminal, native.IsTerminal,
            native.Health, native.NativeState?.CurrentState ?? 0, null);

    private static ActorObservation ObserveRac3(RuntimeWorldScene.DynamicObjectNode node, Rac3MobyRuntimeSession native) =>
        new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z), native.Require(node.Source).IsActive,
            !native.Require(node.Source).IsActive, null, native.Require(node.Source).IsActive
                ? Rac3Class500Destructible.ActiveNativeState : Rac3Class500Destructible.BreakTransitionNativeState, null);

    private ActorHitResult ApplyRac1(RuntimeWorldScene.DynamicObjectNode node, WeaponDamageIntent intent, Rac1EnemyHost owner)
    {
        if (!owner.Current || node.Source.NativeClassId == 749 &&
            (!owner.Roster.TryGetValue(node.Source.InstanceIndex, out var registered) || !ReferenceEquals(registered.Node, node)))
            return new(false, false, "The R&C1 enemy scene or actor has retired.");
        var translated = FusionDamagePolicy.Translate(intent);
        if (translated is null) return new(false, false, "The native R&C1 receiver does not admit this source damage; class-749 sublethal reactions are unrecovered.");
        bool applied = ports.Rac1.ApplyDamage(node, translated);
        return new(applied, !ObserveRac1(node, owner).Alive, applied ? null : "Native R&C1 immunity or reaction gate.");
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

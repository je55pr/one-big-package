using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.Fusion;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// One R&C2 world lease owns Aranos native opening sessions, lift and door clocks,
/// class-2827 contacts and crate-family damage transport. The ordinary actor group
/// retains its own native update phase; debug focus is an optional targeting aid.
/// </summary>
internal sealed partial class Rac2WorldModule : IDisposable
{
    private readonly RuntimeWorld _world;
    private readonly RuntimeWorldScene.Result _scene;
    private readonly GodotWorldResources _resources;
    private readonly Rac2WorldPorts _ports;
    private PlayerHost? _player;
    private bool _configured;
    private bool _activatingCrateStrike;
    private bool _disposed;
    private readonly Rac2WorldLeaseClock _clock;
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _crates = [];
    private readonly Dictionary<int, StaticBody3D> _crateBodies = [];

    internal Rac2WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene, GodotWorldResources resources,
        Rac2WorldPorts ports, Rac2CrateBoltPorts boltPorts, Rac2CrateSupplyPorts supplyPorts)
    {
        _world = world;
        _scene = scene;
        _resources = resources;
        _ports = ports;
        _clock = new(ports.Current, ports.SourceValid, world.Game == ObpSourceGame.Rac2 ||
            scene.DynamicObjectNodes?.Any(node => node.Source.SourceGame == ObpSourceGame.Rac2) == true);
        CrateBolts = new(world, scene, resources, boltPorts, () => _clock.CanSpawnCrateBolts(_activatingCrateStrike));
        CrateEffects = new(world, scene, boltPorts, () => _clock.CanSpawnCrateBolts(_activatingCrateStrike));
        CrateSupply = new(world, scene, boltPorts, supplyPorts, CrateEffects, () => _clock.CanSpawnCrateBolts(_activatingCrateStrike));
        Explosives = new(world, scene, boltPorts, ports, CrateEffects, ApplyExplosiveBlastDamage, DetonateExplosive, () => Current);
    }

    internal Rac2CrateBoltPresentation CrateBolts { get; }
    internal Rac2CrateEffects CrateEffects { get; }
    internal Rac2CrateSupplyPresentation CrateSupply { get; }
    internal Rac2ExplosiveCrateHost Explosives { get; }
    internal bool Current => _clock.Current;
    internal IReadOnlyList<RuntimeWorldScene.DynamicObjectNode> Crates => _crates;

    internal IDictionary<int, Rac2Class2827HostileSession> Hostiles => _rac2Class2827Hostiles;
    internal IDictionary<int, Rac2OpeningMsr1HostSession> OpeningSessions => _rac2OpeningMsr1;
    internal int ParityNanotech => Current ? _rac2RatchetNanotech.Current : 0;
    internal IReadOnlyList<(int Instance, Node3D Root)> OpeningHostileNodes() =>
        Current ? [.. _rac2OpeningMsr1.Values.Select(s => (s.Hostile.InstanceIndex, s.Node.Root))] : [];

    internal bool IsLiveHostile(RuntimeWorldScene.DynamicObjectNode node) =>
        Current && _rac2Class2827Hostiles.TryGetValue(node.Source.InstanceIndex, out var hostile) &&
        !hostile.IsTerminal && GodotObject.IsInstanceValid(node.Root) &&
        (!_rac2OpeningMsr1.TryGetValue(node.Source.InstanceIndex, out var hosted) || ReferenceEquals(hosted.Node, node));

    internal bool IsLiveHostile(RuntimeWorldScene.DynamicObjectNode node, Rac2Class2827HostileSession hostile) =>
        Current && _rac2Class2827Hostiles.TryGetValue(node.Source.InstanceIndex, out var live) &&
        ReferenceEquals(live, hostile) && !hostile.IsTerminal && GodotObject.IsInstanceValid(node.Root);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Retire();
        CrateBolts.Dispose();
        CrateEffects.Dispose();
        CrateSupply.Dispose();
        Explosives.Dispose();
        foreach (var body in _crateBodies.Values)
            if (GodotObject.IsInstanceValid(body)) body.CollisionLayer = body.CollisionMask = 0;
        _crateBodies.Clear();
        _crates.Clear();
        if (_player is not null && GodotObject.IsInstanceValid(_player))
            _player.CrateStrikeRequested -= OnRac2PrimaryAttackRequested;
        _player = null;
        _rac2Class2827Hostiles.Clear();
        _rac2OpeningMsr1.Clear();
        _rac2OpeningLift = null;
        _rac2OpeningDoor = null;
    }

    private readonly Rac2DamageTransportSession _rac2DamageTransport = new();
    internal void Configure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_configured) throw new InvalidOperationException("R&C2 world gameplay is already configured.");
        _configured = true;
        if (_scene.DynamicObjectNodes is not { } nodes)
        {
            return;
        }

        _resources.CrateDiagnostics.Configure(_ports.CrateFocus, _ports.CrateAutoStrike);
        foreach (var family in nodes.Where(node => node.Source.SourceGame == ObpSourceGame.Rac2 &&
            Rac2CrateSupply.IsCrate(node.Source.NativeClassId)).GroupBy(node => node.Source.NativeClassId))
        {
            try { ConfigureCrateFamily(family.Key, family.ToArray()); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or InvalidOperationException)
            {
                if (_ports.Ordinary() && family.Key == Rac2CrateSupply.AmmoClass)
                {
                    GD.PrintErr($"[rac2-crates] Required ammo assets could not be prepared: {ex}");
                    ApplicationLifecycle.RequestQuit(_scene.Root, "required-ammo-assets-unavailable", 2);
                    throw;
                }
                CrateSupply.RetireFamily(family.Key);
                if (family.Key == Rac2ExplosiveCrateSession.NativeClass) Explosives.Dispose();
                foreach (var node in family)
                {
                    if (_crateBodies.Remove(node.Source.InstanceIndex, out var body))
                    {
                        body.CollisionLayer = body.CollisionMask = 0;
                        body.QueueFree();
                    }
                    _crates.Remove(node);
                }
                string status = $"class {family.Key} admission unavailable: {ex.Message}";
                _resources.CrateDiagnostics.Probe.Status = status;
                GD.PrintErr($"[rac2-crates] LEVEL{_world.LevelId}: {status}");
            }
        }

        var openingLiftNode = nodes.FirstOrDefault(n =>
            n.Source.SourceGame == ObpSourceGame.Rac2 &&
            n.Source.NativeClassId == Rac2AranosOpeningLiftSession.NativeClassId &&
            n.Source.InstanceIndex == Rac2AranosOpeningLiftSession.OpeningInstanceIndex);
        if (openingLiftNode is not null)
        {
            _rac2OpeningLift = new Rac2OpeningLiftHostSession(
                openingLiftNode,
                new Rac2AranosOpeningLiftSession(openingLiftNode.Source));
        }

        var openingDoorNode = nodes.FirstOrDefault(n =>
            n.Source.SourceGame == ObpSourceGame.Rac2 &&
            n.Source.NativeClassId == Rac2AranosOpeningDoorSession.NativeClassId &&
            n.Source.InstanceIndex == Rac2AranosOpeningDoorSession.OpeningInstanceIndex);
        if (openingDoorNode is not null)
        {
            _rac2OpeningDoor = new Rac2OpeningDoorHostSession(
                openingDoorNode,
                new Rac2AranosOpeningDoorSession(openingDoorNode.Source, openingDoorNode.State));
        }

        foreach (var node in nodes.Where(n =>
                     n.Source.SourceGame == ObpSourceGame.Rac2 &&
                     n.Source.NativeClassId == Rac2Class2827HostileSession.NativeClassId))
        {
            var hostile = new Rac2Class2827HostileSession(node.Source, node.State);
            _rac2Class2827Hostiles[node.Source.InstanceIndex] = hostile;
            if (node.Source.InstanceIndex is 205 or 206)
            {
                _rac2OpeningMsr1[node.Source.InstanceIndex] =
                    new Rac2OpeningMsr1HostSession(node, hostile);
            }
        }
    }

    private void ConfigureCrateFamily(int nativeClass, IReadOnlyList<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        var admitted = new List<RuntimeWorldScene.DynamicObjectNode>();
        foreach (var node in nodes)
        {
            byte? respawn = nativeClass == 500 ? Rac2Class500Authority.Read(node.Source)?.PvarC8 : Rac2CrateSupply.RespawnFlag(node.Source);
            if (respawn != 0)
            {
                GD.PrintErr($"[rac2-crates] {node.Source.InteractionId}: respawning/missing crate authority is not supported.");
                continue;
            }
            _crates.Add(node);
            admitted.Add(node);
            // Host convex contact derived from the retail model; PS2 narrow
            // phase is not reproduced by Godot's physics engine.
            var body = new AnimatableBody3D { Name = $"Rac2CrateContact_{node.Source.InstanceIndex}", CollisionLayer = 1, CollisionMask = 1, SyncToPhysics = false };
            _crateBodies.Add(node.Source.InstanceIndex, body);
            foreach (var mesh in node.Root.GetChildren().OfType<MeshInstance3D>())
                body.AddChild(new CollisionShape3D { Shape = mesh.Mesh.CreateConvexShape(), Transform = mesh.Transform });
            node.Root.AddChild(body);
        }
        if (admitted.Count == 0) return;
        if (nativeClass is 500 or Rac2ExplosiveCrateSession.NativeClass) CrateBolts.Prepare();
        if (nativeClass == Rac2ExplosiveCrateSession.NativeClass) Explosives.Configure(admitted);
        else if (Rac2CrateSupply.IsSupply(nativeClass)) CrateSupply.Prepare(nativeClass, admitted);
        CrateEffects.Configure(nativeClass, admitted);
    }

    internal void Arm(PlayerHost player)
    {
        if (_disposed) return;
        // The activating scene is not globally current until after player creation.
        // Bind directly to its owned resources rather than the global lease lookup.
        if (_player is not null && GodotObject.IsInstanceValid(_player))
            _player.CrateStrikeRequested -= OnRac2PrimaryAttackRequested;
        _player = player;
        if (_ports.Ordinary() || _scene.DynamicObjectNodes?.Any(node => node.Source.SourceGame == ObpSourceGame.Rac2) != true)
            return;
        // During ActivateOwnedWorld this lease has not been published as Current.
        // Check source admission independently so diagnostics cannot auto-strike
        // an R&C2 target after the source has been invalidated or detached.
        if (!_clock.SourceReadyForActivation) return;
        player.CrateStrikeRequested += OnRac2PrimaryAttackRequested;
        if (_resources.CrateDiagnostics.Probe is { AutoStrike: true, TargetIndex: { } index } &&
            _scene.DynamicObjectNodes.FirstOrDefault(node => node.Source.SourceGame == ObpSourceGame.Rac2 &&
                node.Source.NativeClassId == 500 && node.Source.InstanceIndex == index) is { } target)
        {
            // The lease is source-validated but is not yet published as Current.
            _activatingCrateStrike = true;
            try { ApplyRac2Class500Strike(target, "auto"); }
            finally { _activatingCrateStrike = false; }
        }
    }
    private void OnRac2PrimaryAttackRequested()
    {
        if (!Current || _player is null || !GodotObject.IsInstanceValid(_player)) return;
        var target = SelectRac2PrimaryTargetFromAim();
        if (target is null)
        {
            if (_resources.CrateDiagnostics.Probe is { } probe)
                probe.Status = "strike: no aimed GC gameplay target";
            GD.Print("[gc-damage] strike ignored: no aimed GC gameplay target");
            return;
        }

        if (target.Source.NativeClassId == Rac2Class2827HostileSession.NativeClassId)
        {
            ApplyRac2Class2827Strike(target);
            return;
        }

        ApplyRac2Class500Strike(target, "primary");
    }

    private RuntimeWorldScene.DynamicObjectNode? SelectRac2PrimaryTargetFromAim()
    {
        if (_resources.CrateDiagnostics.Probe is { FocusRequested: true, TargetIndex: { } index } &&
            _scene.DynamicObjectNodes?.FirstOrDefault(node => node.Source.SourceGame == ObpSourceGame.Rac2 &&
                node.Source.NativeClassId == 500 && node.Source.InstanceIndex == index) is { } focused
            && GodotObject.IsInstanceValid(focused.Root) && focused.Root.Visible)
        {
            return focused;
        }

        if (!Current || _scene.DynamicObjectNodes is not { } nodes || _player?.Camera is not { } camera)
        {
            return null;
        }

        Vector3 origin = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalTransform.Basis.Z.Normalized();
        RuntimeWorldScene.DynamicObjectNode? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var node in nodes)
        {
            bool gameplayTarget = Rac2CrateSupply.IsCrate(node.Source.NativeClassId) ||
                node.Source.NativeClassId == Rac2Class2827HostileSession.NativeClassId;
            if (node.Source.SourceGame != ObpSourceGame.Rac2 || !gameplayTarget
                || !GodotObject.IsInstanceValid(node.Root) || !node.Root.Visible)
            {
                continue;
            }

            Vector3 to = node.Root.GlobalPosition - origin;
            float along = to.Dot(forward);
            if (along <= 0f || along > 45f)
            {
                continue;
            }

            float perpendicular = (to - forward * along).Length();
            if (perpendicular > 4.5f)
            {
                continue;
            }

            float score = perpendicular * 10f + along * 0.02f;
            if (score < bestScore)
            {
                bestScore = score;
                best = node;
            }
        }

        return best;
    }

    private void ApplyRac2Class2827Strike(RuntimeWorldScene.DynamicObjectNode target)
    {
        if (!_rac2Class2827Hostiles.TryGetValue(target.Source.InstanceIndex, out var hostile))
        {
            GD.PrintErr($"[gc-hostile] missing class-2827 session for {target.Source.InteractionId}");
            return;
        }

        var damageEvent = Rac2DamageRuntime.FromPlayerState20(target.Source);
        var dispatch = _rac2DamageTransport.Publish(damageEvent);
        var result = hostile.Apply(damageEvent);
        if (!result.Admitted)
        {
            GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId}: class-2827 damage rejected");
            return;
        }

        target.ApplyState(result.EntityState);
        if (result.Terminal)
        {
            _ports.Audio(target);
        }

        GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId} " +
                 $"class2827 hp={result.Health:0.###} cooldown={result.HitCooldownTicks} terminal={result.Terminal}");
    }

    private void ApplyRac2Class500Strike(RuntimeWorldScene.DynamicObjectNode target, string source)
        => ApplyRac2Class500Damage(target, Rac2DamageRuntime.FromPlayerState20(target.Source), source);

    internal ActorObservation ObserveCrate(RuntimeWorldScene.DynamicObjectNode node)
    {
        bool active = Current && _crates.Contains(node) && GodotObject.IsInstanceValid(node.Root) &&
            node.State.Presentation.Presence == RuntimeEntityPresence.Active;
        int nativeState = node.Source.NativeClassId == Rac2ExplosiveCrateSession.NativeClass
            ? Explosives.NativeState(node) : active ? Rac2Class500Lifecycle.ActiveNativeState : Rac2Class500Lifecycle.BreakTransitionNativeState;
        return new(GodotObject.IsInstanceValid(node.Root)
                ? new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z) : System.Numerics.Vector3.Zero,
            active && !(node.Source.NativeClassId == Rac2ExplosiveCrateSession.NativeClass && nativeState == Rac2ExplosiveCrateSession.BurstState),
            !active, null, nativeState, null);
    }

    internal bool IsExplosiveDamageProducer(RuntimeWorldScene.DynamicObjectNode node) =>
        Current && node.Source.SourceGame == ObpSourceGame.Rac2 && node.Source.NativeClassId == Rac2ExplosiveCrateSession.NativeClass &&
        _crates.Contains(node) && GodotObject.IsInstanceValid(node.Root) && node.State.Presentation.Presence == RuntimeEntityPresence.Active &&
        Explosives.NativeState(node) == Rac2ExplosiveCrateSession.BurstState;

    internal ActorHitResult ApplyCrate(RuntimeWorldScene.DynamicObjectNode node, WeaponDamageIntent intent)
    {
        if (!ObserveCrate(node).Alive || intent.Target != ActorWorldHost.Target(node) ||
            !float.IsFinite(intent.SourceDamage) || intent.SourceDamage <= 0)
            return new(false, false, "The R&C2 crate is retired or the contact is invalid.");
        // Explicit OBP cross-source contact adapter: preserve the real source
        // amount, then use an admitted target-game envelope. Foreign flags and
        // native class numbers never become GC attack identities.
        var damage = new Rac2GameplayDamageEvent(Rac2GameplayEntityRef.Player, Rac2GameplayEntityRef.Moby(node.Source),
            new(intent.Kind == "native-blast" ? Rac2ExplosiveCrateSession.FastFuseDamageFlag : Rac2PlayerAttackDamage.State20.DamageFlags,
                0, 0, 0, intent.SourceDamage, 0));
        bool applied = ApplyRac2Class500Damage(node, damage, "weapon-contact");
        return new(applied, applied && node.Source.NativeClassId != Rac2ExplosiveCrateSession.NativeClass,
            applied ? null : "The native R&C2 crate receiver rejected this contact.");
    }

    private bool ApplyRac2Class500Damage(RuntimeWorldScene.DynamicObjectNode target, Rac2GameplayDamageEvent damageEvent, string source)
    {
        if (!_crates.Contains(target)) return false;
        if (target.Source.NativeClassId == Rac2ExplosiveCrateSession.NativeClass)
        {
            if (!Explosives.Apply(target, damageEvent.Damage)) return false;
            _rac2DamageTransport.Publish(damageEvent);
            GD.Print($"[rac2-explosives] triggered={target.Source.InstanceIndex} state={Explosives.NativeState(target)} route={source}");
            return true;
        }
        if (Rac2CrateSupply.IsSupply(target.Source.NativeClassId))
        {
            var next = Rac2CrateSupply.ApplyDamage(target.Source, target.State, damageEvent);
            if (next is null) return false;
            var dispatchSupply = _rac2DamageTransport.Publish(damageEvent);
            var plan = CrateEffects.PlanBreak(target.Source.NativeClassId);
            CrateSupply.Spawn(target);
            CrateEffects.Break(target, plan);
            target.ApplyState(next);
            if (_crateBodies.TryGetValue(target.Source.InstanceIndex, out var supplyBody)) supplyBody.CollisionLayer = supplyBody.CollisionMask = 0;
            GD.Print($"[gc-damage] seq={dispatchSupply.Sequence} {target.Source.InteractionId} class={target.Source.NativeClassId} {source} supply crate broken");
            return true;
        }
        var resources = _resources;
        var probe = resources.CrateDiagnostics.Probe;
        var dispatch = _rac2DamageTransport.Publish(damageEvent);

        Rac2Class500LifecycleResult? lifecycle;
        try
        {
            lifecycle = Rac2DamageRuntime.ApplyClass500Consequence(
                target.Source,
                target.State,
                damageEvent);
        }
        catch (InvalidDataException ex)
        {
            probe.Status = "strike: target has no usable class-500 authority state";
            GD.PrintErr($"[crate-debug] {target.Source.InteractionId}: {ex.Message}");
            return false;
        }

        if (lifecycle is null)
        {
            probe.Status = $"{source}: recovered damage was not admitted";
            GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId}: no class-500 consequence");
            return false;
        }

        var authored = lifecycle.Authored;
        byte c8 = authored.PvarC8!.Value;
        var route = lifecycle.Route;
        probe.ObserveStrike(target.Source.InstanceIndex, c8, route.ToString(), source);

        Rac2CratePayout payout;
        var breakPlan = CrateEffects.PlanBreak();
        try
        {
            // Reward-session inputs are explicit deterministic choices within
            // recovered native domains, not claims about arbitrary retail save
            // state. Multiplier zero is neutral via native max(1, byte).
            payout = resources.Rac2CrateBolts.PlanClass500Payout(
                authored.Uid, authored.AuthoredBolts, rewardMultiplierByte: 0,
                progressionLikeInput: breakPlan.Budget.Recent, rngMod2: 1);
        }
        catch (InvalidOperationException ex)
        {
            probe.RewardStatus = ex.Message;
            GD.PrintErr($"[crate-bolts] {target.Source.InteractionId}: {ex.Message}");
            return false;
        }

        _ports.CrateReward(target.Root.GlobalPosition, payout);
        CrateEffects.Break(target, breakPlan);
        probe.RewardStatus = $"fresh selector {payout.Selector}: centre {payout.RewardCentreValue} => " +
            $"{string.Join("+", payout.PhysicalPickups.Select(p => p.Denomination))} physical, {payout.DeferredValue} deferred";

        // The Oozla class-500 state-3 path immediately enters the native
        // deactivate helper when authored +0xC8 is zero. State 6 remains visible
        // because that class-family behaviour has not yet been reconstructed.
        target.ApplyState(lifecycle.EntityState);
        if (_crateBodies.TryGetValue(target.Source.InstanceIndex, out var body)) body.CollisionLayer = body.CollisionMask = 0;
        if (route == Rac2Class500PostBreakRoute.Deactivate)
        {
            probe.Broken = true;
        }
        GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId} " +
                 $"{source} flags=0x{damageEvent.Damage.DamageFlags:X8} hp={damageEvent.Damage.DamageHp:0.###} " +
                 $"PVar+C8={c8} => state {Rac2CrateInteraction.BreakTransitionState} -> {route}");
        return true;
    }

    private void DetonateExplosive(RuntimeWorldScene.DynamicObjectNode node, Rac2ExplosiveCrateSession motion)
    {
        var plan = CrateEffects.PlanBreak(Rac2ExplosiveCrateSession.NativeClass);
        Rac2CratePayout? payout = null;
        try
        {
            payout = _resources.Rac2CrateBolts.PlanCratePayout(motion.Uid, motion.AuthoredBolts,
                rewardMultiplierByte: 0, progressionLikeInput: plan.Budget.Recent, rngMod2: 1);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            // A rejected reward is reported, but cannot strand the already
            // bursting source actor as a visible, solid crate or stop its blast.
            _resources.CrateDiagnostics.Probe.RewardStatus = ex.Message;
            GD.PrintErr($"[crate-bolts] {node.Source.InteractionId}: {ex.Message}; detonation continues without a new payout");
        }
        if (payout is not null) _ports.CrateReward(node.Root.GlobalPosition, payout);
        CrateEffects.Break(node, plan);
        if (_crateBodies.TryGetValue(node.Source.InstanceIndex, out var body)) body.CollisionLayer = body.CollisionMask = 0;
        // Native state 3 remains a registered damage producer until its expanding
        // sphere finishes. Hide the mesh without retiring its source identity early.
        node.Root.Visible = false;
        GD.Print($"[rac2-explosives] detonated={node.Source.InstanceIndex} payout={payout?.PhysicalValue ?? 0} deferred={payout?.DeferredValue ?? 0}");
    }

    private bool ApplyExplosiveBlastDamage(RuntimeWorldScene.DynamicObjectNode node, Rac2GameplayDamageEvent damage)
    {
        if (!Current || !damage.Target.Matches(node.Source)) return false;
        if (_crates.Contains(node)) return ApplyRac2Class500Damage(node, damage, "explosive-chain");
        if (!_rac2Class2827Hostiles.TryGetValue(node.Source.InstanceIndex, out var hostile) || !IsLiveHostile(node, hostile)) return false;
        var result = hostile.Apply(damage);
        node.ApplyState(result.EntityState);
        return result.Admitted;
    }

    /// <summary>Keep the existing fractional R&C2 native clock inside this lease.</summary>
    internal void RegisterTicks(GameplayTickScheduler<InputEvent> ticks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_world.Game == ObpSourceGame.Rac2)
        {
            ticks.Register(GameplayTickPhase.NativeWorld, "rac2-native", "Rac2WorldModule", 10, Tick);
        }
        if (_crates.Count > 0)
        {
            ticks.Register(GameplayTickPhase.Physics, "rac2-bolts", "Rac2CrateBoltPresentation", 20, CrateBolts.Tick);
            ticks.Register(GameplayTickPhase.Physics, "rac2-crate-effects", "Rac2CrateEffects", 10, CrateEffects.Tick);
            ticks.Register(GameplayTickPhase.Physics, "rac2-explosive-crates", "Rac2ExplosiveCrateHost", 15, Explosives.Tick);
            ticks.Register(GameplayTickPhase.Physics, "rac2-crate-supply", "Rac2CrateSupplyPresentation", 20, CrateSupply.Tick);
            ticks.Register(GameplayTickPhase.Presentation, "rac2-crate-supply-render", "Rac2CrateSupplyPresentation", 20, _ => CrateSupply.Present());
            ticks.Register(GameplayTickPhase.Presentation, "rac2-crate-explosion-render", "Rac2ExplosiveCrateHost", 20, _ => Explosives.Present());
        }
    }

    internal void Tick(double delta)
    {
        if (!Current || _world.Game != ObpSourceGame.Rac2 || _player is null || !GodotObject.IsInstanceValid(_player) ||
            (_rac2Class2827Hostiles.Count == 0 && _rac2OpeningLift is null && _rac2OpeningDoor is null))
        {
            return;
        }

        _clock.Advance(delta, _ports.Ordinary, TickRac2OpeningLiftNativeTick,
            TickRac2OpeningDoorNativeTick, TickRac2Hostiles);
    }

    private void TickRac2Hostiles()
    {
        foreach (var hostile in _rac2Class2827Hostiles.Values)
            hostile.TickCooldown();
        TickRac2OpeningMsr1NativeTick();
    }

    internal string HudLine()
    {
        if (!Current || _world.Game != ObpSourceGame.Rac2 || _rac2OpeningMsr1.Count == 0)
        {
            return string.Empty;
        }

        string phases = string.Join(", ",
            _rac2OpeningMsr1.Values
                .OrderBy(s => s.Hostile.InstanceIndex)
                .Select(s => $"{s.Hostile.InstanceIndex}:{s.Phase}"));
        return $"GC Nanotech: {_rac2RatchetNanotech.Current}/{_rac2RatchetNanotech.Maximum}   MSR I {phases}";
    }

}

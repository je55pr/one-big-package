using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// One R&C2 world lease owns Aranos native opening sessions, lift and door clocks,
/// class-2827 contacts and class-500 damage transport. The ordinary actor group
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
    private bool _disposed;
    private readonly Rac2WorldLeaseClock _clock;

    internal Rac2WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene, GodotWorldResources resources,
        Rac2WorldPorts ports)
    {
        _world = world;
        _scene = scene;
        _resources = resources;
        _ports = ports;
        _clock = new(ports.Current, ports.SourceValid, world.Game == ObpSourceGame.Rac2 ||
            scene.DynamicObjectNodes?.Any(node => node.Source.SourceGame == ObpSourceGame.Rac2) == true);
    }

    internal bool Current => _clock.Current;

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

        _resources.CrateDiagnostics.Configure(_ports.CrateFocus, _ports.CrateAutoStrike);
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
            ApplyRac2Class500Strike(target, "auto");
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
            bool gameplayTarget = node.Source.NativeClassId == 500 ||
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
    {
        var resources = _resources;
        var probe = resources.CrateDiagnostics.Probe;
        var damageEvent = Rac2DamageRuntime.FromPlayerState20(target.Source);
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
            return;
        }

        if (lifecycle is null)
        {
            probe.Status = $"{source}: recovered damage was not admitted";
            GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId}: no class-500 consequence");
            return;
        }

        var authored = lifecycle.Authored;
        byte c8 = authored.PvarC8!.Value;
        var route = lifecycle.Route;
        probe.ObserveStrike(target.Source.InstanceIndex, c8, route.ToString(), source);

        Rac2Class500Payout payout;
        try
        {
            // Reward-session inputs are explicit deterministic choices within
            // recovered native domains, not claims about arbitrary retail save
            // state. Multiplier zero is neutral via native max(1, byte).
            payout = resources.Rac2CrateBolts.PlanClass500Payout(
                authored.Uid, authored.AuthoredBolts, rewardMultiplierByte: 0,
                progressionLikeInput: 0, rngMod2: 1);
        }
        catch (InvalidOperationException ex)
        {
            probe.RewardStatus = ex.Message;
            GD.PrintErr($"[crate-bolts] {target.Source.InteractionId}: {ex.Message}");
            return;
        }

        _ports.CrateReward(target.Root.GlobalPosition, payout);
        _ports.Audio(target);
        probe.RewardStatus = $"fresh selector {payout.Selector}: centre {payout.RewardCentreValue} => " +
            $"{string.Join("+", payout.PhysicalPickups.Select(p => p.Denomination))} physical, {payout.DeferredValue} deferred";

        // The Oozla class-500 state-3 path immediately enters the native
        // deactivate helper when authored +0xC8 is zero. State 6 remains visible
        // because that class-family behaviour has not yet been reconstructed.
        target.ApplyState(lifecycle.EntityState);
        if (route == Rac2Class500PostBreakRoute.Deactivate)
        {
            probe.Broken = true;
        }
        GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId} " +
                 $"state20 flags=0x{damageEvent.Damage.DamageFlags:X8} hp={damageEvent.Damage.DamageHp:0.###} " +
                 $"PVar+C8={c8} => state {Rac2CrateInteraction.BreakTransitionState} -> {route}");
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

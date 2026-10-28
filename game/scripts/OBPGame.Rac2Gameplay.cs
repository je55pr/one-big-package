using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// GC class-500 host integration. Ordinary primary attack input resolves an
/// authored aimed crate, transports the recovered state-20 damage tuple, and
/// delegates the native break/lifetime decision to the RAC2 gameplay runtime.
/// Debug focus remains an optional development targeting aid only.
/// </summary>
public partial class OBPGame
{
    private Rac2DamageTransportSession _rac2DamageTransport = new();
    private void ResetRac2GameplayHost()
    {
        _rac2DamageTransport = new Rac2DamageTransportSession();
        _rac2Class2827Hostiles.Clear();
        _rac2OpeningMsr1.Clear();
        _rac2OpeningLift = null;
        _rac2OpeningDoor = null;
        _rac2RatchetNanotech = new Rac2RatchetNanotechSession();
        _rac2NativeTickAccumulator = 0d;
    }
    private void ConfigureRac2GameplayHost()
    {
        ResetRac2GameplayHost();
        if (_sceneResult?.DynamicObjectNodes is not { } nodes)
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

        _worldScenes!.Current!.Resources.CrateDiagnostics.Configure(_args.CrateFocus, _args.CrateAutoStrike);
    }

    private void ArmRac2GameplayHost(PlayerHost player)
    {
        if (OrdinaryPlayerSession || _sceneResult?.DynamicObjectNodes?.Any(node => node.Source.SourceGame == ObpSourceGame.Rac2) != true)
        {
            return;
        }

        player.CrateStrikeRequested += OnRac2PrimaryAttackRequested;
        if (_worldScenes?.Current?.Resources.CrateDiagnostics.Probe is { AutoStrike: true, TargetIndex: { } index } &&
            _sceneResult.DynamicObjectNodes.FirstOrDefault(node => node.Source.SourceGame == ObpSourceGame.Rac2 &&
                node.Source.NativeClassId == 500 && node.Source.InstanceIndex == index) is { } target)
        {
            ApplyRac2Class500Strike(target, "auto");
        }
    }
    private void OnRac2PrimaryAttackRequested()
    {
        var target = SelectRac2PrimaryTargetFromAim();
        if (target is null)
        {
            if (_worldScenes?.Current?.Resources.CrateDiagnostics.Probe is { } probe)
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
        if (_worldScenes?.Current?.Resources.CrateDiagnostics.Probe is { FocusRequested: true, TargetIndex: { } index } &&
            _sceneResult?.DynamicObjectNodes?.FirstOrDefault(node => node.Source.SourceGame == ObpSourceGame.Rac2 &&
                node.Source.NativeClassId == 500 && node.Source.InstanceIndex == index) is { } focused
            && IsInstanceValid(focused.Root) && focused.Root.Visible)
        {
            return focused;
        }

        if (_sceneResult?.DynamicObjectNodes is not { } nodes || _player?.Camera is not { } camera)
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
                || !IsInstanceValid(node.Root) || !node.Root.Visible)
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
            _worldHost.PlayRepresentativeAudioOneShot(target.Source);
        }

        GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId} " +
                 $"class2827 hp={result.Health:0.###} cooldown={result.HitCooldownTicks} terminal={result.Terminal}");
    }

    private void ApplyRac2Class500Strike(RuntimeWorldScene.DynamicObjectNode target, string source)
    {
        var resources = _worldScenes!.Current!.Resources;
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

        SpawnCrateBoltPickups(target.Root.GlobalPosition, payout);
        _worldHost.PlayRepresentativeAudioOneShot(target.Source);
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

    private void TickRac2Gameplay(double delta)
    {
        if (_world?.Game != ObpSourceGame.Rac2 || _player is null ||
            (_rac2Class2827Hostiles.Count == 0 && _rac2OpeningLift is null && _rac2OpeningDoor is null))
        {
            return;
        }

        _rac2NativeTickAccumulator += Math.Clamp(delta, 0d, 0.25d);
        int ticks = Math.Min(12, (int)Math.Floor(_rac2NativeTickAccumulator / Rac2NativeTickSeconds));
        if (ticks <= 0)
        {
            return;
        }

        _rac2NativeTickAccumulator -= ticks * Rac2NativeTickSeconds;
        for (int tick = 0; tick < ticks; tick++)
        {
            TickRac2OpeningLiftNativeTick();
            TickRac2OpeningDoorNativeTick();

            if (OrdinaryPlayerSession) continue; // Registered source actor clock owns these updates.
            foreach (var hostile in _rac2Class2827Hostiles.Values)
            {
                hostile.TickCooldown();
            }

            TickRac2OpeningMsr1NativeTick();
        }
    }

    private string GetRac2GameplayHudLine()
    {
        if (_world?.Game != ObpSourceGame.Rac2 || _rac2OpeningMsr1.Count == 0)
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

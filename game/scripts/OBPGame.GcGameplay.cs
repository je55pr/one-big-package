using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// GC class-500 host integration. Ordinary primary attack input resolves an
/// authored aimed crate, transports the recovered state-20 damage tuple, and
/// delegates the native break/lifetime decision to the RAC2 gameplay runtime.
/// Debug focus remains an optional development targeting aid only.
/// </summary>
public partial class OBPGame
{
    private GcDamageTransportSession _gcDamageTransport = new();
    private void ResetGcGameplayHost()
    {
        _crateDebugTarget = null;
        _crateDebugStatus = "off";
        _crateDebugPvarC8 = null;
        _crateDebugRoute = null;
        _crateDebugBroken = false;
        _gcDamageTransport = new GcDamageTransportSession();
        _gcClass2827Hostiles.Clear();
        _gcOpeningMsr1.Clear();
        _gcOpeningLift = null;
        _gcOpeningDoor = null;
        _gcRatchetNanotech = new GcRatchetNanotechSession();
        _gcNativeTickAccumulator = 0d;
        _crateBoltSession = new GcFreshBoltSession();
        _crateRewardStatus = "off";
    }
    private void ConfigureGcGameplayHost()
    {
        ResetGcGameplayHost();
        if (_sceneResult?.DynamicObjectNodes is not { } nodes)
        {
            return;
        }

        var openingLiftNode = nodes.FirstOrDefault(n =>
            n.Source.SourceGame == "rac2" &&
            n.Source.NativeClassId == GcAranosOpeningLiftSession.NativeClassId &&
            n.Source.InstanceIndex == GcAranosOpeningLiftSession.OpeningInstanceIndex);
        if (openingLiftNode is not null)
        {
            _gcOpeningLift = new GcOpeningLiftHostSession(
                openingLiftNode,
                new GcAranosOpeningLiftSession(openingLiftNode.Source));
        }

        var openingDoorNode = nodes.FirstOrDefault(n =>
            n.Source.SourceGame == "rac2" &&
            n.Source.NativeClassId == GcAranosOpeningDoorSession.NativeClassId &&
            n.Source.InstanceIndex == GcAranosOpeningDoorSession.OpeningInstanceIndex);
        if (openingDoorNode is not null)
        {
            _gcOpeningDoor = new GcOpeningDoorHostSession(
                openingDoorNode,
                new GcAranosOpeningDoorSession(openingDoorNode.Source, openingDoorNode.State));
        }

        foreach (var node in nodes.Where(n =>
                     n.Source.SourceGame == "rac2" &&
                     n.Source.NativeClassId == GcClass2827HostileSession.NativeClassId))
        {
            var hostile = new GcClass2827HostileSession(node.Source, node.State);
            _gcClass2827Hostiles[node.Source.InstanceIndex] = hostile;
            if (node.Source.InstanceIndex is 205 or 206)
            {
                _gcOpeningMsr1[node.Source.InstanceIndex] =
                    new GcOpeningMsr1HostSession(node, hostile);
            }
        }

        if (!CrateDebugRequested)
        {
            return;
        }

        _crateDebugTarget = nodes
            .Where(n => n.Source.SourceGame == "rac2" && n.Source.NativeClassId == 500)
            .OrderBy(n => n.Source.InstanceIndex)
            .FirstOrDefault();

        _crateDebugStatus = _crateDebugTarget is null
            ? "requested: no class-500 target"
            : $"ready: {_crateDebugTarget.Source.InteractionId}";

        if (_crateDebugTarget is { } target)
        {
            GD.Print($"[crate-debug] target {target.Source.InteractionId} uid={target.Source.NativeUid?.ToString() ?? "?"}");
        }
    }

    private void ArmGcGameplayHost(PlayerHost player)
    {
        if (_world?.Game != "rac2")
        {
            return;
        }

        player.CrateStrikeRequested += OnGcPrimaryAttackRequested;
        if (_args.CrateAutoStrike && _crateDebugTarget is { } target)
        {
            ApplyGcClass500Strike(target, "auto");
        }
    }
    private void OnGcPrimaryAttackRequested()
    {
        var target = SelectGcPrimaryTargetFromAim();
        if (target is null)
        {
            _crateDebugStatus = "strike: no aimed GC gameplay target";
            GD.Print("[gc-damage] strike ignored: no aimed GC gameplay target");
            return;
        }

        if (target.Source.NativeClassId == GcClass2827HostileSession.NativeClassId)
        {
            ApplyGcClass2827Strike(target);
            return;
        }

        ApplyGcClass500Strike(target, "primary");
    }

    private RuntimeWorldScene.DynamicObjectNode? SelectGcPrimaryTargetFromAim()
    {
        if (CrateDebugRequested && _crateDebugTarget is { } focused
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
                node.Source.NativeClassId == GcClass2827HostileSession.NativeClassId;
            if (node.Source.SourceGame != "rac2" || !gameplayTarget
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

    private void ApplyGcClass2827Strike(RuntimeWorldScene.DynamicObjectNode target)
    {
        if (!_gcClass2827Hostiles.TryGetValue(target.Source.InstanceIndex, out var hostile))
        {
            GD.PrintErr($"[gc-hostile] missing class-2827 session for {target.Source.InteractionId}");
            return;
        }

        var damageEvent = GcDamageRuntime.FromPlayerState20(target.Source);
        var dispatch = _gcDamageTransport.Publish(damageEvent);
        var result = hostile.Apply(damageEvent);
        if (!result.Admitted)
        {
            GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId}: class-2827 damage rejected");
            return;
        }

        target.ApplyState(result.EntityState);
        if (result.Terminal)
        {
            PlayRepresentativeAudioOneShot(AudioPosition(target.Source));
        }

        GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId} " +
                 $"class2827 hp={result.Health:0.###} cooldown={result.HitCooldownTicks} terminal={result.Terminal}");
    }

    private void ApplyGcClass500Strike(RuntimeWorldScene.DynamicObjectNode target, string source)
    {
        var damageEvent = GcDamageRuntime.FromPlayerState20(target.Source);
        var dispatch = _gcDamageTransport.Publish(damageEvent);

        GcClass500LifecycleResult? lifecycle;
        try
        {
            lifecycle = GcDamageRuntime.ApplyClass500Consequence(
                target.Source,
                target.State,
                damageEvent);
        }
        catch (InvalidDataException ex)
        {
            _crateDebugStatus = "strike: target has no usable class-500 authority state";
            GD.PrintErr($"[crate-debug] {target.Source.InteractionId}: {ex.Message}");
            return;
        }

        if (lifecycle is null)
        {
            _crateDebugStatus = $"{source}: recovered damage was not admitted";
            GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId}: no class-500 consequence");
            return;
        }

        var authored = lifecycle.Authored;
        byte c8 = authored.PvarC8!.Value;
        var route = lifecycle.Route;
        _crateDebugTarget = target;
        _crateDebugPvarC8 = c8;
        _crateDebugRoute = route.ToString();
        _crateDebugStatus = $"{source}: state 1 -> 3 -> {route}";

        GcClass500Payout payout;
        try
        {
            // Reward-session inputs are explicit deterministic choices within
            // recovered native domains, not claims about arbitrary retail save
            // state. Multiplier zero is neutral via native max(1, byte).
            payout = _crateBoltSession.PlanClass500Payout(
                authored.Uid, authored.AuthoredBolts, rewardMultiplierByte: 0,
                progressionLikeInput: 0, rngMod2: 1);
        }
        catch (InvalidOperationException ex)
        {
            _crateRewardStatus = ex.Message;
            GD.PrintErr($"[crate-bolts] {target.Source.InteractionId}: {ex.Message}");
            return;
        }

        SpawnCrateBoltPickups(target.Root.GlobalPosition, payout);
        PlayRepresentativeAudioOneShot(AudioPosition(target.Source));
        _crateRewardStatus = $"fresh selector {payout.Selector}: centre {payout.RewardCentreValue} => " +
            $"{string.Join("+", payout.PhysicalPickups.Select(p => p.Denomination))} physical, {payout.DeferredValue} deferred";

        // The Oozla class-500 state-3 path immediately enters the native
        // deactivate helper when authored +0xC8 is zero. State 6 remains visible
        // because that class-family behaviour has not yet been reconstructed.
        target.ApplyState(lifecycle.EntityState);
        if (route == GcClass500PostBreakRoute.Deactivate)
        {
            _crateDebugBroken = true;
        }
        GD.Print($"[gc-damage] seq={dispatch.Sequence} {target.Source.InteractionId} " +
                 $"state20 flags=0x{damageEvent.Damage.DamageFlags:X8} hp={damageEvent.Damage.DamageHp:0.###} " +
                 $"PVar+C8={c8} => state {GcCrateInteraction.BreakTransitionState} -> {route}");
    }

    private void TickGcGameplay(double delta)
    {
        if (_world?.Game != "rac2" || _player is null ||
            (_gcClass2827Hostiles.Count == 0 && _gcOpeningLift is null && _gcOpeningDoor is null))
        {
            return;
        }

        _gcNativeTickAccumulator += Math.Clamp(delta, 0d, 0.25d);
        int ticks = Math.Min(12, (int)Math.Floor(_gcNativeTickAccumulator / GcNativeTickSeconds));
        if (ticks <= 0)
        {
            return;
        }

        _gcNativeTickAccumulator -= ticks * GcNativeTickSeconds;
        for (int tick = 0; tick < ticks; tick++)
        {
            TickGcOpeningLiftNativeTick();
            TickGcOpeningDoorNativeTick();

            foreach (var hostile in _gcClass2827Hostiles.Values)
            {
                hostile.TickCooldown();
            }

            TickGcOpeningMsr1NativeTick();
        }
    }

    private string GetGcGameplayHudLine()
    {
        if (_world?.Game != "rac2" || _gcOpeningMsr1.Count == 0)
        {
            return string.Empty;
        }

        string phases = string.Join(", ",
            _gcOpeningMsr1.Values
                .OrderBy(s => s.Hostile.InstanceIndex)
                .Select(s => $"{s.Hostile.InstanceIndex}:{s.Phase}"));
        return $"GC Nanotech: {_gcRatchetNanotech.Current}/{_gcRatchetNanotech.Maximum}   MSR I {phases}";
    }

}

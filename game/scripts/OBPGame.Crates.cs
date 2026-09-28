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
    private const double GcNativeTickSeconds = 1d / GcClass2827ApproachSession.NativeTicksPerSecond;
    private const float GcOpeningMsr1WitnessActivationDistance = 10f;
    private const int GcOpeningMsr1SecondState3DelayTicks = 105;
    private const int GcOpeningMsr1State5Ticks = 24;
    private const int GcOpeningMsr1State4Ticks = 22;
    private const int GcOpeningMsr1State8Ticks = 16;

    private enum GcOpeningMsr1Phase
    {
        Dormant,
        State3Delay,
        State5,
        State4,
        State8,
        Chase,
        Attack,
        Recovery,
    }

    private sealed class GcOpeningMsr1HostSession(
        RuntimeWorldScene.DynamicObjectNode node,
        GcClass2827HostileSession hostile)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        public GcClass2827HostileSession Hostile { get; } = hostile;
        public GcClass2827ApproachSession Approach { get; } = new();
        public GcClass2827AttackCycleSession AttackCycle { get; } = new();
        public GcOpeningMsr1Phase Phase { get; set; }
        public int PhaseTicks { get; set; }
        public int AttackSequence { get; set; }
        public int AttackCount { get; set; }
    }

    private sealed class GcOpeningLiftHostSession(
        RuntimeWorldScene.DynamicObjectNode node,
        GcAranosOpeningLiftSession lift)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        public GcAranosOpeningLiftSession Lift { get; } = lift;
        public bool CarryingPlayer { get; set; }
        public float RiderOffsetY { get; set; }
        public bool ReachedUpperLogged { get; set; }
    }

    private sealed class GcOpeningDoorHostSession(
        RuntimeWorldScene.DynamicObjectNode node,
        GcAranosOpeningDoorSession door)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        public GcAranosOpeningDoorSession Door { get; } = door;
    }

    private GcDamageTransportSession _gcDamageTransport = new();
    private readonly Dictionary<int, GcClass2827HostileSession> _gcClass2827Hostiles = [];
    private readonly Dictionary<int, GcOpeningMsr1HostSession> _gcOpeningMsr1 = [];
    private GcOpeningLiftHostSession? _gcOpeningLift;
    private GcOpeningDoorHostSession? _gcOpeningDoor;
    private GcRatchetNanotechSession _gcRatchetNanotech = new();
    private double _gcNativeTickAccumulator;
    private RuntimeWorldScene.DynamicObjectNode? _crateDebugTarget;
    private string _crateDebugStatus = "off";
    private byte? _crateDebugPvarC8;
    private string? _crateDebugRoute;
    private bool _crateDebugBroken;
    private GcFreshBoltSession _crateBoltSession = new();
    private string _crateRewardStatus = "off";

    private bool CrateDebugRequested => _args.CrateFocus || _args.CrateAutoStrike;

    private void ResetCrateDebugHarness()
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
    private void ConfigureCrateDebugHarness()
    {
        ResetCrateDebugHarness();
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

    private void ArmCrateDebugHarness(PlayerHost player)
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

    private void TickGcOpeningLiftNativeTick()
    {
        if (_gcOpeningLift is not { } session ||
            _player is null || !IsInstanceValid(_player) ||
            !IsInstanceValid(session.Node.Root))
        {
            return;
        }

        if (session.Lift.Phase == GcAranosOpeningLiftPhase.LowerIdle)
        {
            // Class-local retail mesh bounds (native X/Y -> scene X/Z) are
            // x=-2.785..2.715 and z=-2.951..6.778. A small margin accounts for
            // Ratchet's capsule and keeps the trigger tied to the actual platform.
            Vector3 local = session.Node.Root.ToLocal(_player.GlobalPosition);
            bool riderPresent = IsGcOpeningLiftRiderOnPlatform(local);
            if (session.Lift.TryBeginRise(riderPresent))
            {
                session.CarryingPlayer = true;
                session.RiderOffsetY = _player.GlobalPosition.Y - session.Node.Root.GlobalPosition.Y;
                GD.Print($"[gc-lift] opening class-2753 ride started at native Z={session.Lift.CurrentNativeZ:0.###}");
            }
        }

        var step = session.Lift.AdvanceNativeTick();
        if (Math.Abs(step.DeltaNativeZ) > double.Epsilon)
        {
            Transform3D sceneTransform = session.Node.Root.Transform;
            Vector3 origin = sceneTransform.Origin;
            origin.Y += (float)step.DeltaNativeZ;
            sceneTransform.Origin = origin;
            session.Node.ApplyState(session.Node.State.WithTransform(
                RuntimeWorldScene.ToRuntimeTransform(sceneTransform)));
        }

        if (session.CarryingPlayer)
        {
            Vector3 playerPosition = _player.GlobalPosition;
            playerPosition.Y = session.Node.Root.GlobalPosition.Y + session.RiderOffsetY;
            _player.GlobalPosition = playerPosition;

            // The dynamic lift currently has no Godot physics collider. Preserve
            // platform support at the recovered upper stop until Ratchet
            // actually walks off the authored platform footprint toward the
            // class-2755 door, rather than dropping him through the lift.
            if (step.Phase == GcAranosOpeningLiftPhase.UpperIdle)
            {
                Vector3 local = session.Node.Root.ToLocal(_player.GlobalPosition);
                if (!IsGcOpeningLiftRiderOnPlatform(local))
                {
                    session.CarryingPlayer = false;
                    GD.Print("[gc-lift] opening class-2753 rider left upper platform");
                }
            }
        }

        if (step.Phase == GcAranosOpeningLiftPhase.UpperIdle && !session.ReachedUpperLogged)
        {
            session.ReachedUpperLogged = true;
            GD.Print($"[gc-lift] opening class-2753 ride reached native Z={step.NativeZ:0.###}");
        }
    }

    private static bool IsGcOpeningLiftRiderOnPlatform(Vector3 local) =>
        local.X >= -3.15f && local.X <= 3.08f &&
        local.Z >= -3.32f && local.Z <= 7.15f &&
        local.Y >= -0.65f && local.Y <= 3.25f;

    private void TickGcOpeningDoorNativeTick()
    {
        if (_gcOpeningDoor is not { } session ||
            _player is null || !IsInstanceValid(_player) ||
            !IsInstanceValid(session.Node.Root))
        {
            return;
        }

        if (session.Door.Phase == GcAranosOpeningDoorPhase.Closed)
        {
            double distance = session.Node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition);
            if (session.Door.ObservePlayerDistance(distance))
            {
                session.Node.ApplyState(session.Door.EntityState);
                GD.Print($"[gc-door] opening class-2755 trigger admitted at distance={distance:0.###}");
            }
        }

        var before = session.Door.Phase;
        var after = session.Door.AdvanceNativeTick();
        if (before != GcAranosOpeningDoorPhase.Open &&
            after == GcAranosOpeningDoorPhase.Open)
        {
            GD.Print("[gc-door] opening class-2755 reached native state 2 / latched-open pose");
        }
    }

    private void TickGcOpeningMsr1NativeTick()
    {
        if (_player is null || !IsInstanceValid(_player))
        {
            return;
        }

        Vector3 playerPosition = _player.GlobalPosition;
        bool playerDamageCommitted = false;
        foreach (var session in _gcOpeningMsr1.Values.OrderBy(s => s.Hostile.InstanceIndex))
        {
            if (session.Hostile.IsTerminal ||
                !IsInstanceValid(session.Node.Root) ||
                !session.Node.Root.Visible)
            {
                continue;
            }

            Vector3 position = session.Node.Root.GlobalPosition;
            switch (session.Phase)
            {
                case GcOpeningMsr1Phase.Dormant:
                    {
                        if (position.DistanceTo(playerPosition) > GcOpeningMsr1WitnessActivationDistance)
                        {
                            session.Hostile.AdvanceNativeStateTicks();
                            continue;
                        }

                        session.Phase = GcOpeningMsr1Phase.State3Delay;
                        session.PhaseTicks = session.Hostile.InstanceIndex == 206
                            ? GcOpeningMsr1SecondState3DelayTicks
                            : 0;
                        GD.Print($"[gc-msr1] {session.Node.Source.InteractionId} opening witness gate entered");
                        break;
                    }

                case GcOpeningMsr1Phase.State3Delay:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        if (session.PhaseTicks > 0)
                        {
                            session.PhaseTicks--;
                            break;
                        }

                        EnterGcOpeningMsr1Stage(
                            session,
                            nativeState: 5,
                            GcOpeningMsr1Phase.State5,
                            GcOpeningMsr1State5Ticks);
                        break;
                    }

                case GcOpeningMsr1Phase.State5:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        SettleGcOpeningMsr1Vertical(
                            session,
                            playerPosition.Y,
                            GcOpeningMsr1State4Ticks + GcOpeningMsr1State8Ticks);
                        if (--session.PhaseTicks <= 0)
                        {
                            EnterGcOpeningMsr1Stage(
                                session,
                                nativeState: 4,
                                GcOpeningMsr1Phase.State4,
                                GcOpeningMsr1State4Ticks);
                        }
                        break;
                    }

                case GcOpeningMsr1Phase.State4:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        SettleGcOpeningMsr1Vertical(
                            session,
                            playerPosition.Y,
                            GcOpeningMsr1State8Ticks);
                        if (--session.PhaseTicks <= 0)
                        {
                            EnterGcOpeningMsr1Stage(
                                session,
                                nativeState: 8,
                                GcOpeningMsr1Phase.State8,
                                GcOpeningMsr1State8Ticks);
                        }
                        break;
                    }

                case GcOpeningMsr1Phase.State8:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        SettleGcOpeningMsr1Vertical(session, playerPosition.Y, futureTicks: 0);
                        if (--session.PhaseTicks <= 0)
                        {
                            session.Hostile.NativeState?.Transition(
                                GcClass2827HostileSession.ChaseNativeState,
                                transitionMode: 4);
                            session.Approach.Reset();
                            session.Phase = GcOpeningMsr1Phase.Chase;
                            GD.Print($"[gc-msr1] {session.Node.Source.InteractionId} entered native chase state 12");
                        }
                        break;
                    }

                case GcOpeningMsr1Phase.Chase:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        var step = session.Approach.Advance(
                            position.X,
                            position.Z,
                            playerPosition.X,
                            playerPosition.Z);
                        position.X = (float)step.X;
                        position.Z = (float)step.Z;
                        ApplyGcClass2827Position(session, position);

                        float distance = new Vector2(
                            playerPosition.X - position.X,
                            playerPosition.Z - position.Z).Length();
                        if (session.Hostile.TryEnterAttack(distance, facingError: 0d))
                        {
                            session.AttackSequence =
                                session.AttackCount == 0 && session.Hostile.InstanceIndex == 205
                                    ? GcClass2827HostileSession.AttackSequence16
                                    : GcClass2827HostileSession.AttackSequence27;
                            session.AttackCount++;
                            session.AttackCycle.Begin(session.AttackSequence);
                            session.PhaseTicks = 0;
                            session.Phase = GcOpeningMsr1Phase.Attack;
                        }
                        break;
                    }

                case GcOpeningMsr1Phase.Attack:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        session.PhaseTicks++;

                        if (session.PhaseTicks == GcClass2827AttackCycleSession.ObservedPlayerContactStateTick)
                        {
                            float distance = new Vector2(
                                playerPosition.X - position.X,
                                playerPosition.Z - position.Z).Length();
                            if (!playerDamageCommitted &&
                                distance < GcClass2827HostileSession.AttackEntryDistanceExclusive)
                            {
                                var probe = session.Hostile.ProbeAttackContact(
                                    session.AttackSequence,
                                    GcClass2827HostileSession.AttackContactFrameStart);
                                var damage = session.AttackCycle.TryAggregatePlayerContact(probe.Contacts);
                                if (damage is { } admittedDamage)
                                {
                                    var result = _gcRatchetNanotech.Apply(admittedDamage);
                                    if (result.Admitted)
                                    {
                                        playerDamageCommitted = true;
                                        GD.Print($"[gc-msr1] {session.Node.Source.InteractionId} " +
                                                 $"contact -> Nanotech {result.Current}/{result.Maximum} " +
                                                 $"player-state {result.NativeState}");
                                    }
                                }
                            }
                        }

                        if (session.PhaseTicks >= session.AttackCycle.ObservedDurationTicks())
                        {
                            session.Hostile.NativeState?.Transition(14, transitionMode: 4);
                            session.AttackCycle.End();
                            session.PhaseTicks = 0;
                            session.Phase = GcOpeningMsr1Phase.Recovery;
                        }
                        break;
                    }

                case GcOpeningMsr1Phase.Recovery:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        session.PhaseTicks++;
                        if (session.PhaseTicks >= GcClass2827AttackCycleSession.ObservedRecoveryDurationTicks)
                        {
                            if (_gcRatchetNanotech.PendingHitResolution)
                            {
                                _gcRatchetNanotech.ResolveHitReaction();
                            }

                            session.Hostile.NativeState?.Transition(
                                GcClass2827HostileSession.ChaseNativeState,
                                transitionMode: 4);
                            session.Approach.Reset();
                            session.PhaseTicks = 0;
                            session.Phase = GcOpeningMsr1Phase.Chase;
                        }
                        break;
                    }
            }
        }
    }

    private static void EnterGcOpeningMsr1Stage(
        GcOpeningMsr1HostSession session,
        int nativeState,
        GcOpeningMsr1Phase phase,
        int ticks)
    {
        session.Hostile.NativeState?.Transition(nativeState, transitionMode: 4);
        session.Phase = phase;
        session.PhaseTicks = ticks;
    }

    private static void SettleGcOpeningMsr1Vertical(
        GcOpeningMsr1HostSession session,
        float targetY,
        int futureTicks)
    {
        Vector3 position = session.Node.Root.GlobalPosition;
        int remainingTicks = Math.Max(session.PhaseTicks + futureTicks, 1);
        position.Y += (targetY - position.Y) / remainingTicks;
        ApplyGcClass2827Position(session, position);
    }

    private static void ApplyGcClass2827Position(
        GcOpeningMsr1HostSession session,
        Vector3 position)
    {
        Transform3D sceneTransform = session.Node.Root.Transform;
        sceneTransform.Origin = position;
        var runtimeTransform = RuntimeWorldScene.ToRuntimeTransform(sceneTransform);
        var state = session.Hostile.ApplyPresentationTransform(runtimeTransform);
        session.Node.ApplyState(state);
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

    private bool TryGetCrateFocusPose(RuntimeWorld world, out Vector3 spawn, out float yaw)
    {
        spawn = default;
        yaw = 0f;
        if (!CrateDebugRequested || _crateDebugTarget is not { } target || !IsInstanceValid(target.Root))
        {
            return false;
        }

        Vector3 centre = target.Root.GlobalPosition;
        Vector3 approach = Vector3.Back;
        if (world.Ship is { } ship)
        {
            Vector3 fromCrateToShip = RuntimeWorldScene.ToScene(ship.X, ship.Y, ship.Z) - centre;
            fromCrateToShip.Y = 0f;
            if (fromCrateToShip.LengthSquared() > 1f)
            {
                approach = fromCrateToShip.Normalized();
            }
        }

        spawn = centre + approach * 7f + Vector3.Up * 4f;
        Vector3 look = centre - spawn;
        look.Y = 0f;
        yaw = look.LengthSquared() > 0.01f ? Mathf.Atan2(look.X, look.Z) + Mathf.Pi : 0f;
        return true;
    }
    private string GetCrateDebugHudLine()
    {
        if (_crateDebugStatus == "off")
        {
            return string.Empty;
        }

        string target = _crateDebugTarget?.Source.InteractionId ?? "none";
        return $"Crate debug: {target}   {_crateDebugStatus}\n" +
            $"Bolt payout: {_crateRewardStatus}   collected {_crateBoltSession.CollectedBolts}   " +
            $"outstanding {_crateBoltSession.OutstandingPickupCount}   deferred {_crateBoltSession.DeferredBolts}";
    }

    private CrateDebugSnapshot? GetCrateDebugSnapshot()
    {
        if (_crateDebugStatus == "off")
        {
            return null;
        }

        var target = _crateDebugTarget;
        return new CrateDebugSnapshot(
            TargetInteractionId: target?.Source.InteractionId,
            NativeUid: target?.Source.NativeUid,
            NativeInstanceIndex: target?.Source.InstanceIndex,
            Visible: target is { } t && IsInstanceValid(t.Root) && t.Root.Visible,
            Broken: _crateDebugBroken,
            Status: _crateDebugStatus,
            EventFlags: $"0x{GcPlayerAttackDamage.State20.DamageFlags:X8}",
            EventScalar: GcPlayerAttackDamage.State20.DamageHp,
            PvarC8: _crateDebugPvarC8,
            Route: _crateDebugRoute,
            RewardStatus: _crateRewardStatus,
            CollectedBolts: _crateBoltSession.CollectedBolts,
            OutstandingPickups: _crateBoltSession.OutstandingPickupCount,
            DeferredBolts: _crateBoltSession.DeferredBolts);
    }

    private sealed record CrateDebugSnapshot(
        string? TargetInteractionId,
        int? NativeUid,
        int? NativeInstanceIndex,
        bool Visible,
        bool Broken,
        string Status,
        string EventFlags,
        float EventScalar,
        byte? PvarC8,
        string? Route,
        string RewardStatus,
        int CollectedBolts,
        int OutstandingPickups,
        int DeferredBolts);
}

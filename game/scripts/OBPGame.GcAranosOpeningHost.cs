using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

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

    private readonly Dictionary<int, GcClass2827HostileSession> _gcClass2827Hostiles = [];
    private readonly Dictionary<int, GcOpeningMsr1HostSession> _gcOpeningMsr1 = [];
    private GcOpeningLiftHostSession? _gcOpeningLift;
    private GcOpeningDoorHostSession? _gcOpeningDoor;
    private GcRatchetNanotechSession _gcRatchetNanotech = new();
    private double _gcNativeTickAccumulator;
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

}

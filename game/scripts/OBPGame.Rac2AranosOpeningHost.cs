using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

public partial class OBPGame
{
    private const double Rac2NativeTickSeconds = 1d / Rac2Class2827ApproachSession.NativeTicksPerSecond;
    private const float Rac2OpeningMsr1WitnessActivationDistance = 10f;
    private const int Rac2OpeningMsr1SecondState3DelayTicks = 105;
    private const int Rac2OpeningMsr1State5Ticks = 24;
    private const int Rac2OpeningMsr1State4Ticks = 22;
    private const int Rac2OpeningMsr1State8Ticks = 16;

    private enum Rac2OpeningMsr1Phase
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

    private sealed class Rac2OpeningMsr1HostSession(
        RuntimeWorldScene.DynamicObjectNode node,
        Rac2Class2827HostileSession hostile)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        public Rac2Class2827HostileSession Hostile { get; } = hostile;
        public Rac2Class2827ApproachSession Approach { get; } = new();
        public Rac2Class2827AttackCycleSession AttackCycle { get; } = new();
        public Rac2OpeningMsr1Phase Phase { get; set; }
        public int PhaseTicks { get; set; }
        public int AttackSequence { get; set; }
        public int AttackCount { get; set; }
    }

    private sealed class Rac2OpeningLiftHostSession(
        RuntimeWorldScene.DynamicObjectNode node,
        Rac2AranosOpeningLiftSession lift)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        public Rac2AranosOpeningLiftSession Lift { get; } = lift;
        public bool CarryingPlayer { get; set; }
        public float RiderOffsetY { get; set; }
        public bool ReachedUpperLogged { get; set; }
    }

    private sealed class Rac2OpeningDoorHostSession(
        RuntimeWorldScene.DynamicObjectNode node,
        Rac2AranosOpeningDoorSession door)
    {
        public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
        public Rac2AranosOpeningDoorSession Door { get; } = door;
    }

    private readonly Dictionary<int, Rac2Class2827HostileSession> _rac2Class2827Hostiles = [];
    private readonly Dictionary<int, Rac2OpeningMsr1HostSession> _rac2OpeningMsr1 = [];
    private Rac2OpeningLiftHostSession? _rac2OpeningLift;
    private Rac2OpeningDoorHostSession? _rac2OpeningDoor;
    private Rac2RatchetNanotechSession _rac2RatchetNanotech = new();
    private double _rac2NativeTickAccumulator;
    private void TickRac2OpeningLiftNativeTick()
    {
        if (_rac2OpeningLift is not { } session ||
            _player is null || !IsInstanceValid(_player) ||
            !IsInstanceValid(session.Node.Root))
        {
            return;
        }

        if (session.Lift.Phase == Rac2AranosOpeningLiftPhase.LowerIdle)
        {
            // Class-local retail mesh bounds (native X/Y -> scene X/Z) are
            // x=-2.785..2.715 and z=-2.951..6.778. A small margin accounts for
            // Ratchet's capsule and keeps the trigger tied to the actual platform.
            Vector3 local = session.Node.Root.ToLocal(_player.GlobalPosition);
            bool riderPresent = IsRac2OpeningLiftRiderOnPlatform(local);
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
            if (step.Phase == Rac2AranosOpeningLiftPhase.UpperIdle)
            {
                Vector3 local = session.Node.Root.ToLocal(_player.GlobalPosition);
                if (!IsRac2OpeningLiftRiderOnPlatform(local))
                {
                    session.CarryingPlayer = false;
                    GD.Print("[gc-lift] opening class-2753 rider left upper platform");
                }
            }
        }

        if (step.Phase == Rac2AranosOpeningLiftPhase.UpperIdle && !session.ReachedUpperLogged)
        {
            session.ReachedUpperLogged = true;
            GD.Print($"[gc-lift] opening class-2753 ride reached native Z={step.NativeZ:0.###}");
        }
    }

    private static bool IsRac2OpeningLiftRiderOnPlatform(Vector3 local) =>
        local.X >= -3.15f && local.X <= 3.08f &&
        local.Z >= -3.32f && local.Z <= 7.15f &&
        local.Y >= -0.65f && local.Y <= 3.25f;

    private void TickRac2OpeningDoorNativeTick()
    {
        if (_rac2OpeningDoor is not { } session ||
            _player is null || !IsInstanceValid(_player) ||
            !IsInstanceValid(session.Node.Root))
        {
            return;
        }

        if (session.Door.Phase == Rac2AranosOpeningDoorPhase.Closed)
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
        if (before != Rac2AranosOpeningDoorPhase.Open &&
            after == Rac2AranosOpeningDoorPhase.Open)
        {
            GD.Print("[gc-door] opening class-2755 reached native state 2 / latched-open pose");
        }
    }

    private void TickRac2OpeningMsr1NativeTick()
    {
        if (_player is null || !IsInstanceValid(_player) || !PlayerAlive)
        {
            return;
        }

        Vector3 playerPosition = _player.GlobalPosition;
        bool playerDamageCommitted = false;
        foreach (var session in _rac2OpeningMsr1.Values.OrderBy(s => s.Hostile.InstanceIndex))
        {
            if (session.Hostile.IsTerminal ||
                !IsInstanceValid(session.Node.Root) ||
                !session.Node.Root.Visible)
            {
                continue;
            }

            Vector3 position = session.Node.Root.GlobalPosition;
            if (_actorContactBodies.ContainsKey(ActorTarget(session.Node)) &&
                (Math.Abs(position.Y - playerPosition.Y) > 3 || position.DistanceTo(playerPosition) > Rac2OpeningMsr1WitnessActivationDistance))
                continue; // Explicit grounded-arena target context for relocated actors.
            switch (session.Phase)
            {
                case Rac2OpeningMsr1Phase.Dormant:
                    {
                        if (position.DistanceTo(playerPosition) > Rac2OpeningMsr1WitnessActivationDistance)
                        {
                            session.Hostile.AdvanceNativeStateTicks();
                            continue;
                        }

                        session.Phase = Rac2OpeningMsr1Phase.State3Delay;
                        session.PhaseTicks = session.Hostile.InstanceIndex == 206
                            ? Rac2OpeningMsr1SecondState3DelayTicks
                            : 0;
                        GD.Print($"[gc-msr1] {session.Node.Source.InteractionId} opening witness gate entered");
                        break;
                    }

                case Rac2OpeningMsr1Phase.State3Delay:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        if (session.PhaseTicks > 0)
                        {
                            session.PhaseTicks--;
                            break;
                        }

                        EnterRac2OpeningMsr1Stage(
                            session,
                            nativeState: 5,
                            Rac2OpeningMsr1Phase.State5,
                            Rac2OpeningMsr1State5Ticks);
                        break;
                    }

                case Rac2OpeningMsr1Phase.State5:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        SettleRac2OpeningMsr1Vertical(
                            session,
                            playerPosition.Y,
                            Rac2OpeningMsr1State4Ticks + Rac2OpeningMsr1State8Ticks);
                        if (--session.PhaseTicks <= 0)
                        {
                            EnterRac2OpeningMsr1Stage(
                                session,
                                nativeState: 4,
                                Rac2OpeningMsr1Phase.State4,
                                Rac2OpeningMsr1State4Ticks);
                        }
                        break;
                    }

                case Rac2OpeningMsr1Phase.State4:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        SettleRac2OpeningMsr1Vertical(
                            session,
                            playerPosition.Y,
                            Rac2OpeningMsr1State8Ticks);
                        if (--session.PhaseTicks <= 0)
                        {
                            EnterRac2OpeningMsr1Stage(
                                session,
                                nativeState: 8,
                                Rac2OpeningMsr1Phase.State8,
                                Rac2OpeningMsr1State8Ticks);
                        }
                        break;
                    }

                case Rac2OpeningMsr1Phase.State8:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        SettleRac2OpeningMsr1Vertical(session, playerPosition.Y, futureTicks: 0);
                        if (--session.PhaseTicks <= 0)
                        {
                            session.Hostile.NativeState?.Transition(
                                Rac2Class2827HostileSession.ChaseNativeState,
                                transitionMode: 4);
                            session.Approach.Reset();
                            session.Phase = Rac2OpeningMsr1Phase.Chase;
                            GD.Print($"[gc-msr1] {session.Node.Source.InteractionId} entered native chase state 12");
                        }
                        break;
                    }

                case Rac2OpeningMsr1Phase.Chase:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        var step = session.Approach.Advance(
                            position.X,
                            position.Z,
                            playerPosition.X,
                            playerPosition.Z);
                        position.X = (float)step.X;
                        position.Z = (float)step.Z;
                        ApplyRac2Class2827Position(session, position);
                        position = session.Node.Root.GlobalPosition;

                        float distance = new Vector2(
                            playerPosition.X - position.X,
                            playerPosition.Z - position.Z).Length();
                        double facingError = 0;
                        if (_actorContactBodies.ContainsKey(ActorTarget(session.Node)))
                        {
                            Vector3 toward = playerPosition - position; toward.Y = 0;
                            if (toward.LengthSquared() > 1e-8f)
                                facingError = EnemyForward(session.Node).SignedAngleTo(toward.Normalized(), Vector3.Up);
                        }
                        if (session.Hostile.TryEnterAttack(distance, facingError))
                        {
                            session.AttackSequence =
                                session.AttackCount == 0 && session.Hostile.InstanceIndex == 205
                                    ? Rac2Class2827HostileSession.AttackSequence16
                                    : Rac2Class2827HostileSession.AttackSequence27;
                            session.AttackCount++;
                            session.AttackCycle.Begin(session.AttackSequence);
                            session.PhaseTicks = 0;
                            session.Phase = Rac2OpeningMsr1Phase.Attack;
                        }
                        break;
                    }

                case Rac2OpeningMsr1Phase.Attack:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        session.PhaseTicks++;

                        if (session.PhaseTicks == Rac2Class2827AttackCycleSession.ObservedPlayerContactStateTick)
                        {
                            float distance = new Vector2(
                                playerPosition.X - position.X,
                                playerPosition.Z - position.Z).Length();
                            if (!playerDamageCommitted &&
                                distance < Rac2Class2827HostileSession.AttackEntryDistanceExclusive)
                            {
                                var probe = session.Hostile.ProbeAttackContact(
                                    session.AttackSequence,
                                    Rac2Class2827HostileSession.AttackContactFrameStart);
                                var damage = session.AttackCycle.TryAggregatePlayerContact(probe.Contacts);
                                if (damage is { } admittedDamage)
                                {
                                    if (_playerNative is not null)
                                    {
                                        var admitted = new Rac2RatchetNanotechSession().Apply(admittedDamage);
                                        if (admitted.Admitted)
                                            playerDamageCommitted = ApplyPlayerSourceDamage(session.Node, OBP.RAC2.Rac2Authority.Primary.BuildId,
                                                PlayerSessionNativeState.Rac2DamageAdapter, PlayerSessionNativeState.Rac2DamageDescriptor,
                                                session.AttackCount, admitted.DamageNanotech, admittedDamage.QueryMask).Applied;
                                        break;
                                    }
                                    var result = _rac2RatchetNanotech.Apply(admittedDamage);
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
                            session.Phase = Rac2OpeningMsr1Phase.Recovery;
                        }
                        break;
                    }

                case Rac2OpeningMsr1Phase.Recovery:
                    {
                        session.Hostile.AdvanceNativeStateTicks();
                        session.PhaseTicks++;
                        if (session.PhaseTicks >= Rac2Class2827AttackCycleSession.ObservedRecoveryDurationTicks)
                        {
                            if (_playerNative is null && _rac2RatchetNanotech.PendingHitResolution)
                            {
                                _rac2RatchetNanotech.ResolveHitReaction();
                            }

                            session.Hostile.NativeState?.Transition(
                                Rac2Class2827HostileSession.ChaseNativeState,
                                transitionMode: 4);
                            session.Approach.Reset();
                            session.PhaseTicks = 0;
                            session.Phase = Rac2OpeningMsr1Phase.Chase;
                        }
                        break;
                    }
            }
        }
    }

    private static void EnterRac2OpeningMsr1Stage(
        Rac2OpeningMsr1HostSession session,
        int nativeState,
        Rac2OpeningMsr1Phase phase,
        int ticks)
    {
        session.Hostile.NativeState?.Transition(nativeState, transitionMode: 4);
        session.Phase = phase;
        session.PhaseTicks = ticks;
    }

    private void SettleRac2OpeningMsr1Vertical(
        Rac2OpeningMsr1HostSession session,
        float targetY,
        int futureTicks)
    {
        Vector3 position = session.Node.Root.GlobalPosition;
        int remainingTicks = Math.Max(session.PhaseTicks + futureTicks, 1);
        position.Y += (targetY - position.Y) / remainingTicks;
        ApplyRac2Class2827Position(session, position);
    }

    private void ApplyRac2Class2827Position(
        Rac2OpeningMsr1HostSession session,
        Vector3 position)
    {
        if (_actorContactBodies.TryGetValue(ActorTarget(session.Node), out var body))
        {
            var contact = body.MoveAndCollide(position - body.GlobalPosition);
            if (contact is not null)
            {
                Vector3 remainder = contact.GetRemainder().Slide(contact.GetNormal());
                if (remainder.LengthSquared() > 1e-8f) body.MoveAndCollide(remainder);
            }
            position = body.GlobalPosition;
        }
        Transform3D sceneTransform = session.Node.Root.Transform;
        if (_actorContactBodies.ContainsKey(ActorTarget(session.Node)) && _player is not null)
        {
            Vector3 toward = _player.GlobalPosition - position; toward.Y = 0;
            if (toward.LengthSquared() > 1e-8f)
            {
                // OBP facing projection for the bounded planar context. A full
                // native GC steering controller is not asserted by this adapter.
                float angle = EnemyForward(session.Node).SignedAngleTo(toward.Normalized(), Vector3.Up);
                sceneTransform.Basis = new Basis(Vector3.Up, angle) * sceneTransform.Basis;
            }
        }
        sceneTransform.Origin = position;
        var runtimeTransform = RuntimeWorldScene.ToRuntimeTransform(sceneTransform);
        var state = session.Hostile.ApplyPresentationTransform(runtimeTransform);
        session.Node.ApplyState(state);
    }

}

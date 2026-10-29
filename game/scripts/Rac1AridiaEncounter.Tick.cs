using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using static OBP.Game.Rac1EnemyCoordinates;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed partial class Rac1AridiaEncounter
{
    internal void Tick(Rac1HostedEnemy enemy)
    {
        if (!_host.Owns(enemy) || _host.Player is null) return;
        var node = enemy.Node;
        int instance = node.Source.InstanceIndex;
        if (_rac1AridiaEnemies.Health(instance) <= 0 && !_rac1AridiaEnemies.CanReleaseFromNest(instance))
        {
            _host.SetRac1EnemyFlame(enemy, false);
            if (++enemy.DeathTicks >= 36)
            {
                // Native shark state 26 is a reusable nest pool, not a terminal
                // despawn. Other defeated classes still leave gameplay.
                if (_rac1AridiaEnemies.RetireToNestPool(instance))
                {
                    var native = _host.Context.MobyRuntime.Require(node.Source);
                    _host.Context.MobyRuntime.SetNativeState(native, 26);
                    enemy.Buried = true;
                    enemy.NeedsGroundPlacement = false;
                    enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
                    Hide(enemy);
                }
                else _host.SetRac1HostedPresence(node, RuntimeEntityPresence.Inactive);
            }
            return;
        }

        Vector3 position = node.Root.GlobalPosition;
        Vector3 player = _host.Player.GlobalPosition;
        var facts = new Rac1AridiaEnemyFacts(
            ToEffectNative(position), ToEffectNative(player),
            !_host.Health.Probe().IsDead,
            position.DistanceSquaredTo(player) < 225f &&
                _host.Combat.LineOfSight(position + Vector3.Up * .35f, player + Vector3.Up * .5f),
            node.Source.NativeClassId == Rac1AridiaEnemies.Nest
                ? _rac1AridiaEnemies.WaitingSharkCountForNest(instance)
                : 0,
            LinkedMobyNativeState: ResolveAridiaSharkLinkedMobyState(instance),
            NestAnimationTime: node.Source.NativeClassId == Rac1AridiaEnemies.Nest
                ? enemy.Animation.Position : null);
        var frame = _rac1AridiaEnemies.Step(instance, facts);
        Rac1AridiaConstructorbotNative.Frame? nativeBot = null;
        bool sourceParticlePreview = false;
        bool sourceStateThreeMotion = false;
        bool sourceStateThreeVerticalReached = false;
        float? sourceStateThreeTargetHeight = null;
        bool sourceWalkerMotion = false;
        bool sourceWalkerTouched = false;
        Vector3 sourceWalkerGoal = default;
        float sourceWalkerYawError = 0;
        Vector3 sourceWalkerRequestedWorldStep = default;
        int sourceWalkerProbeFlags = 0;
        if (node.Source.NativeClassId == Rac1AridiaEnemies.Constructorbot)
        {
            int nativeStateAtTickStart = _host.Context.MobyRuntime.Require(node.Source).State.NativeState;
            nativeBot = StepRac1NativeConstructorbot(enemy);
            if (nativeBot is { } sourceFrame)
            {
                sourceStateThreeMotion = sourceFrame.NativeState == 3 &&
                    _host.Context.MobyRuntime.Require(node.Source).State.NativeState == 3 &&
                    _rac1ConstructorbotNativeWaypoints.ContainsKey(instance);
                if (sourceStateThreeMotion)
                {
                    const float frameScale = (float)(1.0 / OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz);
                    Vector3 origin = node.Root.GlobalPosition;
                    Vector3 destination = _rac1ConstructorbotNativeWaypoints[instance];
                    var sourceDisplacement = Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(
                        ToEffectNative(origin), ToEffectNative(destination), frameScale);
                    Vector3 toTarget = player - origin;
                    toTarget.Y = 0;
                    Vector3 forward = Rac1EnemyHost.EnemyForward(node);
                    forward.Y = 0;
                    float yawError = toTarget.LengthSquared() > .00001f &&
                        forward.LengthSquared() > .00001f
                        ? forward.Normalized().SignedAngleTo(toTarget.Normalized(), Vector3.Up) : 0;
                    float nextYaw = Rac1AridiaConstructorbotNative.StepStateThreeYaw(
                        node.Root.Rotation.Y, yawError, frameScale);
                    Vector3 horizontal = FromEffectNative(sourceDisplacement);
                    sourceStateThreeTargetHeight = ProbeRac1ConstructorbotStateThreeFloor(
                        enemy, origin + horizontal);
                    // Native step 1: approach the PVar+0x48 vertical-step
                    // variable toward 10*frameScale. 0x161a84*0x15ed70 is
                    // not independently decoded; use one host tick as the
                    // bounded acceleration argument while preserving the
                    // exact source approach_f recurrence and transition.
                    var vertical = Rac1AridiaConstructorbotNative.StepStateThreeVertical(
                        origin.Y,
                        _rac1ConstructorbotStateThreeVerticalSteps.GetValueOrDefault(instance),
                        sourceStateThreeTargetHeight,
                        frameScale, frameScale);
                    _rac1ConstructorbotStateThreeVerticalSteps[instance] = vertical.VerticalStep;
                    sourceStateThreeVerticalReached = vertical.ReachedFloor;
                    frame = frame with
                    {
                        Motion = ToEffectNative(horizontal +
                            Vector3.Up * (vertical.Height - origin.Y)),
                        Yaw = nextYaw
                    };
                }
                sourceWalkerMotion = sourceFrame.NativeState == nativeStateAtTickStart &&
                    (nativeStateAtTickStart == 5 ||
                     nativeStateAtTickStart == Rac1AridiaConstructorbotNative.RoamingState) &&
                    (nativeStateAtTickStart == 5
                        ? _rac1ConstructorbotNativeWaypoints.TryGetValue(
                            instance, out sourceWalkerGoal)
                        : _rac1ConstructorbotRoamingTargets.TryGetValue(
                            instance, out sourceWalkerGoal));
                if (sourceWalkerMotion)
                {
                    Vector3 worldDelta = sourceWalkerGoal - node.Root.GlobalPosition;
                    worldDelta.Y = 0;
                    float planarDistance = worldDelta.Length();
                    Vector3 forward = Rac1EnemyHost.EnemyForward(node);
                    forward.Y = 0;
                    sourceWalkerYawError = forward.LengthSquared() > .00001f &&
                        planarDistance > .00001f
                        ? forward.Normalized().SignedAngleTo(worldDelta.Normalized(), Vector3.Up)
                        : 0;
                    // L2:0x25b710 uses shared yaw_turn_damped and class612
                    // record+0x3c/0x40/0x44 (0.03/0.3/0.25). Retain the
                    // per-actor angular velocity in walker record+0x14.
                    // The wrapper's timer and collision solver are still
                    // approximated by bounded host observations.
                    var turn = Rac1AridiaConstructorbotNative.StepWalkerDampedYaw(
                        node.Root.Rotation.Y,
                        _rac1ConstructorbotWalkerYawVelocities.GetValueOrDefault(instance),
                        sourceWalkerYawError);
                    _rac1ConstructorbotWalkerYawVelocities[instance] = turn.AngularVelocity;
                    float nextYaw = turn.Yaw;
                    sourceWalkerYawError = turn.RemainingError;
                    float scaledTurn = MathF.Abs(sourceWalkerYawError);
                    float movementScale = Math.Clamp(
                        (Rac1AridiaConstructorbotNative.WalkerYawTolerance - scaledTurn) /
                        Rac1AridiaConstructorbotNative.WalkerYawTolerance, 0, 1);
                    float speed = Rac1AridiaConstructorbotNative.WalkerSpeed(
                        _rac1ConstructorbotWalkerSpeeds.GetValueOrDefault(instance),
                        planarDistance, movementScale);
                    _rac1ConstructorbotWalkerSpeeds[instance] = speed;
                    Vector3 newForward = forward.Rotated(Vector3.Up,
                        nextYaw - node.Root.Rotation.Y).Normalized();
                    sourceWalkerRequestedWorldStep = newForward * MathF.Min(
                        MathF.Max(0, speed), planarDistance);
                    // Native walker_probe checks the candidate ground
                    // BEFORE walker_move_fx. Use a real scene support ray;
                    // never treat a missing floor as completed travel.
                    Vector3 candidate = node.Root.GlobalPosition + sourceWalkerRequestedWorldStep;
                    bool supported = ProbeRac1AridiaWalkerSupport(enemy, candidate,
                        out float floorHeight, out _);
                    sourceWalkerProbeFlags = Rac1AridiaConstructorbotNative.WalkerInitialProbeFlags(
                        supported, Rac1AridiaConstructorbotNative.WalkerInitialRecordFlags);
                    if (sourceWalkerProbeFlags == 0 &&
                        sourceWalkerRequestedWorldStep.Length() >
                        Rac1AridiaConstructorbotNative.WalkerLookaheadSpeedThreshold)
                    {
                        // The native routine checks a SECOND segment 0.57
                        // units ahead. The pure source helper also recovers
                        // its surface-normal test, but Godot's imported
                        // triangle normals have not been proven equivalent
                        // to the PS2 collision hit normal at 0x173e80.
                        // On flat imported Aridia terrain both Godot normals
                        // are +Y, which wrongly signals a native blockage.
                        // Until source-normal parity is recovered, run the
                        // conservative missing-floor guard WITHOUT assuming
                        // those normals match. A missing ahead hit still
                        // blocks with original 0x22 (Constructorbot mode).
                        Vector3 aheadDirection = sourceWalkerRequestedWorldStep;
                        aheadDirection.Y = 0;
                        Vector3 aheadPosition = node.Root.GlobalPosition +
                            aheadDirection.Normalized() *
                            Rac1AridiaConstructorbotNative.WalkerLookaheadDistance;
                        bool aheadSupported = ProbeRac1AridiaWalkerSupport(
                            enemy, aheadPosition, out _, out _);
                        sourceWalkerProbeFlags |= Rac1AridiaConstructorbotNative.WalkerInitialProbeFlags(
                            aheadSupported,
                            Rac1AridiaConstructorbotNative.WalkerInitialRecordFlags);
                    }
                    if (supported && Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
                        node.Root.GlobalPosition.Y, floorHeight,
                        Rac1AridiaConstructorbotNative.WalkerProbeDown,
                        Rac1AridiaConstructorbotNative.WalkerProbeUp))
                        sourceWalkerProbeFlags |= 0x22;
                    frame = frame with
                    {
                        Motion = sourceWalkerProbeFlags == 0
                            ? ToEffectNative(sourceWalkerRequestedWorldStep)
                            : System.Numerics.Vector3.Zero,
                        Yaw = nextYaw
                    };
                }
                // The known state10/11/12 source clips take precedence over
                // the old timed host flamethrower. Unknown remaining states
                // continue using the explicitly provisional host adapter.
                frame = frame with
                {
                    Sequence = enemy.Animation.SupportsSequence(sourceFrame.Sequence)
                        ? sourceFrame.Sequence : frame.Sequence,
                    DamagePlayer = false,
                    FlameActive = false
                };
            }
            var nativeInstance = _host.Context.MobyRuntime.Require(node.Source);
            sourceParticlePreview = Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
                nativeInstance.State.NativeState, _host.NativeFrame,
                _host.IsRac1GameplayActive(node));
        }
        if (node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark &&
            _rac1AridiaEnemies.LinkedActivationSatisfied(instance))
        {
            var native = _host.Context.MobyRuntime.Require(node.Source);
            if (native.State.NativeState == 29)
                _host.Context.MobyRuntime.SetNativeState(native, 1);
        }
        // Read the OLD source attack pose before a state11 completion swaps
        // animation 8 to recovery sequence 8/10 on this same native tick.
        bool jointContact = nativeBot is { JointContactQuery: true } &&
            enemy.Animation.SupportsSequence(8) &&
            AridiaConstructorbotJointTouchesPlayer(enemy);
        enemy.Buried = frame.Buried;
        enemy.Body.CollisionLayer = enemy.Body.CollisionMask = enemy.Buried ? 0u : 1u;
        node.Root.Visible = !enemy.Buried;
        if (enemy.Animation.SupportsSequence(frame.Sequence))
            Rac1EnemyHost.SelectRac1EnemySequence(enemy, frame.Sequence, 6);
        node.Root.Rotation = new(0, frame.Yaw, 0);
        if (sourceStateThreeMotion)
        {
            // Native L2:0x2d7a88..0x2d7aec DIRECTLY writes the Moby
            // position before probing its floor. It does not call the
            // walker collision solver or a generic character-slide here.
            // Godot's generic contact shape can otherwise trap a falling
            // constructorbot on Ratchet's head rather than letting source
            // floor_probe_v find level geometry beneath them.
            Vector3 destination = position + FromEffectNative(frame.Motion);
            enemy.Body.GlobalPosition = destination;
            enemy.Node.Root.GlobalPosition = destination;
            enemy.Grounded = sourceStateThreeVerticalReached &&
                sourceStateThreeTargetHeight is { } terrain &&
                MathF.Abs(destination.Y - terrain) < .035f;
        }
        else if (sourceWalkerMotion && frame.Motion.LengthSquared() > 0)
            sourceWalkerTouched = MoveRac1AridiaWalkerContact(
                enemy, FromEffectNative(frame.Motion));
        else if (frame.Motion.LengthSquared() > 0)
            _host.MoveRac1EnemyContact(enemy, FromEffectNative(frame.Motion));
        else
            enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        if (sourceStateThreeMotion)
        {
            // Native state3 waits until its SECOND approach_f has reached
            // the actual floor probe height; unrelated IsOnFloor alone
            // must not advance the original state machine.
            bool reached = sourceStateThreeVerticalReached &&
                sourceStateThreeTargetHeight is { } floor &&
                MathF.Abs(enemy.Body.GlobalPosition.Y - floor) < .035f;
            var landing = Rac1AridiaConstructorbotNative.StepStateThreeLanding(
                reached ? true : null);
            if (landing.NativeState == 4)
            {
                _host.Context.MobyRuntime.SetNativeState(_host.Context.MobyRuntime.Require(node.Source), 4);
                _rac1ConstructorbotStateThreeVerticalSteps.Remove(instance);
            }
        }
        if (sourceWalkerMotion)
        {
            // walker_record_step checks the native walker_move_fx
            // collision bit1, THEN projected travel along the original
            // velocity with a source 0.2 threshold. No invented 50%
            // movement cutoff or phantom collision from mere grounding.
            int collisionFlags = sourceWalkerProbeFlags;
            if (sourceWalkerProbeFlags == 0)
            {
                float? floor = sourceWalkerTouched
                    ? ProbeRac1ConstructorbotStateThreeFloor(
                        enemy, enemy.Body.GlobalPosition)
                    : null;
                var contact = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
                    ToEffectNative(sourceWalkerRequestedWorldStep),
                    ToEffectNative(enemy.Body.GlobalPosition - position),
                    (sourceWalkerTouched ? Rac1AridiaConstructorbotNative.WalkerTouchedFlag : 0) |
                    (enemy.Grounded ? Rac1AridiaConstructorbotNative.WalkerMoveFloorContactFlag : 0),
                    ToEffectNative(position).Z,
                    floor.HasValue ? ToEffectNative(new Vector3(0, floor.Value, 0)).Z : null);
                collisionFlags |= contact.Flags;
                if (contact.RestorePreviousPosition)
                {
                    // Native source rollback from home when the post-contact
                    // floor elevation exceeds its +8/+0xc reach.
                    enemy.Body.GlobalPosition = position;
                    enemy.Node.Root.GlobalPosition = position;
                }
            }
            // walker_record_step compares XY distance (native X/Y),
            // not full 3D: authored clip-centres can lie above the floor.
            int flags = Rac1AridiaConstructorbotNative.WalkerFlags(
                Rac1AridiaConstructorbotNative.WalkerPlanarDistance(
                    ToEffectNative(enemy.Body.GlobalPosition),
                    ToEffectNative(sourceWalkerGoal)),
                sourceWalkerYawError, collisionFlags);
            int walkingState = _host.Context.MobyRuntime.Require(node.Source).State.NativeState;
            bool returnToHome = walkingState ==
                Rac1AridiaConstructorbotNative.RoamingState &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance, out var anchor) &&
                player.DistanceTo(anchor) <
                    Rac1AridiaConstructorbotNative.ReadEngagementRadius(
                        _host.Context.MobyRuntime.Require(node.Source).PVar.Span) &&
                MathF.Abs(player.Y - enemy.Body.GlobalPosition.Y) < 4f;
            var walker = Rac1AridiaConstructorbotNative.StepWalkerState(
                walkingState, flags,
                resultKnown: sourceWalkerProbeFlags != 0 || enemy.Grounded,
                playerNearAnchor: returnToHome);
            if (walker.NativeState != walkingState)
            {
                _host.Context.MobyRuntime.SetNativeState(_host.Context.MobyRuntime.Require(node.Source),
                    walker.NativeState);
                _rac1ConstructorbotWalkerSpeeds.Remove(instance);
                _rac1ConstructorbotWalkerYawVelocities.Remove(instance);
                if (walkingState == Rac1AridiaConstructorbotNative.RoamingState)
                {
                    if (walker.NativeState == Rac1AridiaConstructorbotNative.RestState)
                        _rac1ConstructorbotRoamExitFlags[instance] = flags;
                    _rac1ConstructorbotRoamingTargets.Remove(instance);
                }
            }
        }
        // In native state8/9 the source event-15 preview exclusively owns
        // the visual gate. Do not let a nearby player's legacy host flame
        // bypass the original four-frame cadence or state9 exclusion.
        // Other unimplemented states retain their provisional host visual.
        bool flameVisible = node.Source.NativeClassId == Rac1AridiaEnemies.Constructorbot
            ? Rac1AridiaConstructorbotNative.HostFlameVisible(
                _host.Context.MobyRuntime.Require(node.Source).State.NativeState,
                frame.FlameActive, sourceParticlePreview)
            : frame.FlameActive;
        _host.SetRac1EnemyFlame(enemy, flameVisible);
        if (jointContact)
            ApplyRac1AridiaPlayerDamage(node);
        else if (frame.DamagePlayer &&
            node.Root.GlobalPosition.DistanceTo(player) <
                (node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark ? 1.25f : 5f))
            ApplyRac1AridiaPlayerDamage(node);
        if (frame.SpawnSandshark) ActivateNearbyRac1AridiaShark(instance, node.Root.GlobalPosition);
    }
}

using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1AridiaEnemySession _rac1AridiaEnemies = new();
    private readonly Dictionary<int, Rac1AridiaConstructorbotNative.Frame> _rac1ConstructorbotStartups = [];
    private readonly Dictionary<int, float> _rac1ConstructorbotWalkerSpeeds = [];
    // Source table at 0x1600ec is the gameplay +0x60 list already decoded
    // as Rac1ClipVolumes: PVar+0x188 selects a record, +0x30 its position.
    private readonly Dictionary<int, Vector3> _rac1ConstructorbotNativeWaypoints = [];
    private IReadOnlyList<Rac1ClipVolume> _rac1AridiaClipVolumes = [];

    private void ResetRac1ConstructorbotStartups()
    {
        _rac1ConstructorbotStartups.Clear();
        _rac1ConstructorbotWalkerSpeeds.Clear();
        _rac1ConstructorbotNativeWaypoints.Clear();
        _rac1AridiaClipVolumes = [];
    }

    /// <summary>
    /// Narrow native class612 support. The original state10 and state11
    /// transitions use Moby+0x70 animation-complete; the host supplies a
    /// clip-wrap approximation, never a proximity timer. State12's full
    /// authored path/target solver is not yet implemented and must NOT be
    /// auto-advanced. Mode8/9 still use provisional movement/host combat.
    /// </summary>
    private Rac1AridiaConstructorbotNative.Frame? StepRac1NativeConstructorbot(Rac1HostedEnemy enemy)
    {
        var instance = _rac1MobyRuntime.Require(enemy.Node.Source);
        int state = instance.State.NativeState;
        if (state is Rac1AridiaConstructorbotNative.ParticleModeState or
            Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            int volumeIndex = Rac1AridiaConstructorbotNative.ReadActivationVolume(instance.PVar.Span);
            if (volumeIndex >= 0)
            {
                if (volumeIndex >= _rac1AridiaClipVolumes.Count)
                    throw new InvalidDataException("Constructorbot references a missing retail activation volume.");
                if (_player is not { } player) return null;
                bool inside = _rac1AridiaClipVolumes[volumeIndex].Contains(
                    ToEffectNative(player.GlobalPosition));
                var active = Rac1AridiaConstructorbotNative.StepActivation(state, inside);
                if (active.NativeState != state)
                    _rac1MobyRuntime.SetNativeState(instance, active.NativeState);
                return active;
            }
            // L2:0x2d7fac uses 3D distance to the SAME native clip-list
            // record's translation +0x30. No oriented-box check for -1.
            if (_player is not { } target || !_rac1ConstructorbotNativeWaypoints.TryGetValue(
                    instance.InstanceIndex, out var sourceWaypoint))
                return null;
            bool close = Rac1AridiaConstructorbotNative.NativeWaypointActivation(
                ToEffectNative(target.GlobalPosition),
                ToEffectNative(sourceWaypoint),
                Rac1AridiaConstructorbotNative.ReadActivationDistance(instance.PVar.Span));
            var activated = Rac1AridiaConstructorbotNative.StepActivation(state, close);
            if (activated.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, activated.NativeState);
            return activated;
        }
        if (state == 2 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var startup) &&
            startup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            int entrySequence = startup.NativeState == Rac1AridiaConstructorbotNative.ParticleModeState
                ? 4 : 16;
            var next = Rac1AridiaConstructorbotNative.StepActivatedAnimation(
                enemy.Animation.Sequence == entrySequence && enemy.Animation.Wrapped, entrySequence);
            if (next.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == 3 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var activatedStartup) &&
            activatedStartup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            // Original 0x1600ec movement waypoint is now decoded.
            // Horizontal displacement and host yaw are applied by the
            // presentation layer, after the engine-neutral state gate.
            // Source floor-height convergence is checked after physics.
            return Rac1AridiaConstructorbotNative.StepStateThreeLanding(null);
        }
        if (state == 4 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var landingStartup) &&
            landingStartup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            var next = Rac1AridiaConstructorbotNative.StepStateFour(
                enemy.Animation.Sequence == 5 && enemy.Animation.Wrapped);
            if (next.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == 5 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var movementStartup) &&
            movementStartup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            // Source 0x25b710 walker flags and speed are applied after
            // the real Godot collision movement. Only source return ==4
            // permits transition to rest; no timed host melee pulse.
            return new(state, 6);
        }
        if (state == 6)
        {
            // L2:0x2d7bb4 uses a different native 0x1e0 return target.
            // We cannot infer its live target from the authored centre;
            // retain source sequence6 until that vector is supplied.
            return new(state, 6);
        }
        if (state == Rac1AridiaConstructorbotNative.RestState)
        {
            if (_player is { } player &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance.InstanceIndex, out var anchor))
            {
                // L2:0x2d7c98 selects the actual gameplay +0x60 record at
                // PVar+0x188 and tests its original +0x30 translation.
                // Unlike the previous fallback, this is a retail waypoint.
                Vector3 position = enemy.Node.Root.GlobalPosition;
                Vector3 target = player.GlobalPosition;
                Vector3 planar = target - position;
                planar.Y = 0;
                Vector3 facing = EnemyForward(enemy.Node);
                facing.Y = 0;
                float facingError = planar.LengthSquared() > .00001f &&
                    facing.LengthSquared() > .00001f
                    ? facing.Normalized().SignedAngleTo(planar.Normalized(), Vector3.Up)
                    : float.PositiveInfinity;
                if (Rac1AridiaConstructorbotNative.State7AdmitsJointContact(
                    target.DistanceTo(anchor), target.Y - position.Y,
                    position.DistanceTo(anchor), facingError,
                    Rac1AridiaConstructorbotNative.ReadEngagementRadius(instance.PVar.Span)))
                {
                    _rac1MobyRuntime.SetNativeState(instance,
                        Rac1AridiaConstructorbotNative.JointContactState);
                    // Native state7 selects attack animation8, then state11
                    // runs its per-tick joint query on the following update.
                    return new(Rac1AridiaConstructorbotNative.JointContactState, 8);
                }
            }
            return new(state, 0);
        }
        if (state == Rac1AridiaConstructorbotNative.AimState)
        {
            // Do not mistake a wrap from a provisional host-selected clip
            // for completion of the source's required animation 0.
            if (enemy.Animation.Sequence != 0) return new(state, 0);
            var next = Rac1AridiaConstructorbotNative.StepAim(enemy.Animation.Wrapped);
            if (next.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == Rac1AridiaConstructorbotNative.JointContactState)
        {
            // Native state11 executes its joint-0 contact query every tick.
            bool clipCompleted = enemy.Animation.Sequence == 8 && enemy.Animation.Wrapped;
            var next = Rac1AridiaConstructorbotNative.StepJointContact(
                clipCompleted, clipCompleted && Rac1EffectRandom.Below(2) != 0);
            if (next.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == Rac1AridiaConstructorbotNative.FollowupState)
        {
            if (_player is null) return new(state, 9);
            Vector3 delta = _player.GlobalPosition - enemy.Node.Root.GlobalPosition;
            float planarDistance = new Vector2(delta.X, delta.Z).Length();
            bool completed = enemy.Animation.Sequence == 9 && enemy.Animation.Wrapped;
            var next = Rac1AridiaConstructorbotNative.StepFollowup(
                completed, planarDistance, delta.Y,
                Rac1AridiaConstructorbotNative.ReadEngagementRadius(instance.PVar.Span));
            if (next.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == Rac1AridiaConstructorbotNative.ContactRecoveryState)
        {
            // Native requires enemy_physics result bit 0x40. Godot's on-floor
            // contact is the host's acknowledged approximation of that flag,
            // never an elapsed-time recovery or an unconditional transition.
            var next = Rac1AridiaConstructorbotNative.StepRecovery(
                enemy.Body.IsOnFloor(), enemy.Animation.Sequence);
            if (next.NativeState != state)
                _rac1MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        return null;
    }

    private bool AridiaConstructorbotJointTouchesPlayer(Rac1HostedEnemy enemy)
    {
        // Host collision adapter for native LEVEL2:0x2d812c..0x2d8228.
        // It starts at the original sequence's semantic joint-0 socket, and
        // approximates the native segment/contact solver with a Godot ray.
        // The native shape, descriptor flags and target filters still differ.
        Vector3 origin = EnemySocket(enemy);
        Vector3 direction = EnemyForward(enemy.Node);
        Vector3 target = origin + direction + Vector3.Down * .1f;
        return EnemyRayTouchesPlayer(enemy, origin, target);
    }

    /// <summary>
    /// Host adapter for the native 0x2583f0 walker_probe vertical segment.
    /// The source uses collision kind 0x22 and hit-record metadata. Godot
    /// supplies a real scene raycast, excluding self and Ratchet. This
    /// deliberately does not infer a floor from IsOnFloor or elapsed ticks.
    /// </summary>
    private bool ProbeRac1AridiaWalkerSupport(
        Rac1HostedEnemy enemy, Vector3 candidate, out float floorY,
        out float normalY)
    {
        var source = Rac1AridiaConstructorbotNative.WalkerProbeHeights(
            ToEffectNative(candidate).Z,
            Rac1AridiaConstructorbotNative.WalkerProbeUp,
            Rac1AridiaConstructorbotNative.WalkerProbeDown);
        Vector3 start = candidate with { Y = source.StartZ };
        Vector3 end = candidate with { Y = source.EndZ };
        floorY = normalY = 0;
        if (start.DistanceSquaredTo(end) < 1e-10f) return false;
        var query = PhysicsRayQueryParameters3D.Create(start, end);
        query.Exclude = [enemy.Body.GetRid(), _player!.GetRid()];
        var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.TryGetValue("position", out var hitPosition) &&
            hit.TryGetValue("normal", out var hitNormal))
        {
            floorY = hitPosition.AsVector3().Y;
            normalY = hitNormal.AsVector3().Y;
            return true;
        }

        // Original walker_probe tests a line in source geometry. Some
        // imported, two-sided triangle meshes have gaps beneath a character
        // centre even though its HOST capsule has genuine floor support.
        // Use the same source +/-0.5 vertical window in a bounded Godot
        // capsule sweep, with all other actors excluded, as a conservative
        // collision-shape adapter. Still require an actual collision.
        var from = enemy.Body.GlobalTransform;
        from.Origin = start;
        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = from,
            Motion = end - start,
            Margin = enemy.Body.SafeMargin,
            RecoveryAsCollision = true,
            MaxCollisions = 4,
            ExcludeBodies = new global::Godot.Collections.Array<Rid>(
                _rac1Enemies.Values.Select(other => other.Body.GetRid())
                    .Append(_player.GetRid()))
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(enemy.Body.GetRid(), parameters, result))
            return false;
        for (int i = 0; i < result.GetCollisionCount(); i++)
        {
            Vector3 normal = result.GetCollisionNormal(i);
            if (normal.Y <= .65f) continue;
            floorY = start.Y + result.GetTravel().Y;
            normalY = normal.Y;
            return true;
        }
        return false;
    }

    private void TickRac1AridiaEnemy(Rac1HostedEnemy enemy)
    {
        if (_player is null) return;
        var node = enemy.Node;
        int instance = node.Source.InstanceIndex;
        if (_rac1AridiaEnemies.Health(instance) <= 0 && !_rac1AridiaEnemies.CanReleaseFromNest(instance))
        {
            SetRac1EnemyFlame(enemy, false);
            if (++enemy.DeathTicks >= 36)
            {
                // Native shark state 26 is a reusable nest pool, not a terminal
                // despawn. Other defeated classes still leave gameplay.
                if (_rac1AridiaEnemies.RetireToNestPool(instance))
                {
                    var native = _rac1MobyRuntime.Require(node.Source);
                    _rac1MobyRuntime.SetNativeState(native, 26);
                    enemy.Buried = true;
                    enemy.NeedsGroundPlacement = false;
                    enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
                    HideBuriedRac1AridiaEnemy(enemy);
                }
                else SetRac1HostedPresence(node, RuntimeEntityPresence.Inactive);
            }
            return;
        }

        Vector3 position = node.Root.GlobalPosition;
        Vector3 player = _player.GlobalPosition;
        var facts = new Rac1AridiaEnemyFacts(
            ToEffectNative(position), ToEffectNative(player),
            !_rac1Nanotech.Probe().IsDead,
            position.DistanceSquaredTo(player) < 225f &&
                BombHasLineOfSight(position + Vector3.Up * .35f, player + Vector3.Up * .5f),
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
        bool sourceWalkerMotion = false;
        Vector3 sourceWalkerGoal = default;
        float sourceWalkerYawError = 0;
        Vector3 sourceWalkerRequestedWorldStep = default;
        int sourceWalkerProbeFlags = 0;
        if (node.Source.NativeClassId == Rac1AridiaEnemies.Constructorbot)
        {
            nativeBot = StepRac1NativeConstructorbot(enemy);
            if (nativeBot is { } sourceFrame)
            {
                sourceStateThreeMotion = sourceFrame.NativeState == 3 &&
                    _rac1MobyRuntime.Require(node.Source).State.NativeState == 3 &&
                    _rac1ConstructorbotNativeWaypoints.ContainsKey(instance);
                if (sourceStateThreeMotion)
                {
                    const float frameScale = (float)(1.0 / Rac1NativeTicksPerSecond);
                    Vector3 origin = node.Root.GlobalPosition;
                    Vector3 destination = _rac1ConstructorbotNativeWaypoints[instance];
                    var sourceDisplacement = Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(
                        ToEffectNative(origin), ToEffectNative(destination), frameScale);
                    Vector3 toTarget = player - origin;
                    toTarget.Y = 0;
                    Vector3 forward = EnemyForward(node);
                    forward.Y = 0;
                    float yawError = toTarget.LengthSquared() > .00001f &&
                        forward.LengthSquared() > .00001f
                        ? forward.Normalized().SignedAngleTo(toTarget.Normalized(), Vector3.Up) : 0;
                    float nextYaw = Rac1AridiaConstructorbotNative.StepStateThreeYaw(
                        node.Root.Rotation.Y, yawError, frameScale);
                    frame = frame with { Motion = sourceDisplacement, Yaw = nextYaw };
                }
                sourceWalkerMotion = sourceFrame.NativeState == 5 &&
                    _rac1MobyRuntime.Require(node.Source).State.NativeState == 5 &&
                    _rac1ConstructorbotNativeWaypoints.TryGetValue(instance, out sourceWalkerGoal);
                if (sourceWalkerMotion)
                {
                    Vector3 worldDelta = sourceWalkerGoal - node.Root.GlobalPosition;
                    worldDelta.Y = 0;
                    float planarDistance = worldDelta.Length();
                    Vector3 forward = EnemyForward(node);
                    forward.Y = 0;
                    sourceWalkerYawError = forward.LengthSquared() > .00001f &&
                        planarDistance > .00001f
                        ? forward.Normalized().SignedAngleTo(worldDelta.Normalized(), Vector3.Up)
                        : 0;
                    const float frameScale = (float)(1.0 / Rac1NativeTicksPerSecond);
                    float nextYaw = Rac1AridiaConstructorbotNative.StepStateThreeYaw(
                        node.Root.Rotation.Y, sourceWalkerYawError, frameScale);
                    // Full native yaw_turn_damped, timer_tick_i32 and
                    // walker_move_fx are NOT reconstructed. Keep the source
                    // speed recurrence but use bounded Godot contact motion.
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
                        out float floorHeight, out float supportNormalY);
                    sourceWalkerProbeFlags = Rac1AridiaConstructorbotNative.WalkerInitialProbeFlags(
                        supported, Rac1AridiaConstructorbotNative.WalkerInitialRecordFlags);
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
            var nativeInstance = _rac1MobyRuntime.Require(node.Source);
            sourceParticlePreview = Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
                nativeInstance.State.NativeState, _rac1AridiaNativeFrameCounter,
                IsRac1GameplayActive(node));
        }
        if (node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark &&
            _rac1AridiaEnemies.LinkedActivationSatisfied(instance))
        {
            var native = _rac1MobyRuntime.Require(node.Source);
            if (native.State.NativeState == 29)
                _rac1MobyRuntime.SetNativeState(native, 1);
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
            SelectRac1EnemySequence(enemy, frame.Sequence, 6);
        node.Root.Rotation = new(0, frame.Yaw, 0);
        if (frame.Motion.LengthSquared() > 0)
            MoveRac1EnemyContact(enemy, FromEffectNative(frame.Motion));
        else
            enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        if (sourceStateThreeMotion)
        {
            // Source state3 transitions only after its floor-height approach
            // reaches the probed floor. Godot ground contact is a host
            // approximation of that signal. A missing floor contact must
            // NEVER be treated as an implicit success or timeout.
            var landing = Rac1AridiaConstructorbotNative.StepStateThreeLanding(
                enemy.Grounded ? true : null);
            if (landing.NativeState == 4)
                _rac1MobyRuntime.SetNativeState(_rac1MobyRuntime.Require(node.Source), 4);
        }
        if (sourceWalkerMotion)
        {
            // Native walker_probe's valid support/missing floor decision
            // now comes from a Godot vertical scene ray. Contact/slide
            // results of walker_move_fx remain a bounded host estimate.
            float requested = sourceWalkerRequestedWorldStep.Length();
            float travelled = (enemy.Body.GlobalPosition - position).Length();
            int collisionFlags = sourceWalkerProbeFlags;
            if (sourceWalkerProbeFlags == 0 && requested > .001f &&
                travelled < requested * .5f)
                collisionFlags |= 0x22;
            int flags = Rac1AridiaConstructorbotNative.WalkerFlags(
                enemy.Body.GlobalPosition.DistanceTo(sourceWalkerGoal),
                sourceWalkerYawError, collisionFlags);
            var walker = Rac1AridiaConstructorbotNative.StepWalkerState(
                5, flags, resultKnown: sourceWalkerProbeFlags != 0 || enemy.Grounded);
            if (walker.NativeState == Rac1AridiaConstructorbotNative.RestState)
            {
                _rac1MobyRuntime.SetNativeState(_rac1MobyRuntime.Require(node.Source),
                    walker.NativeState);
                _rac1ConstructorbotWalkerSpeeds.Remove(instance);
            }
        }
        // Temporary source-particle-15 visual proxy. The native effect is a
        // sparse event at semantic joint0, NOT the host's continuous flame.
        SetRac1EnemyFlame(enemy, frame.FlameActive || sourceParticlePreview);
        if (jointContact)
            ApplyRac1AridiaPlayerDamage(node);
        else if (frame.DamagePlayer &&
            node.Root.GlobalPosition.DistanceTo(player) <
                (node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark ? 1.25f : 5f))
            ApplyRac1AridiaPlayerDamage(node);
        if (frame.SpawnSandshark) ActivateNearbyRac1AridiaShark(instance, node.Root.GlobalPosition);
    }

    private int? ResolveAridiaSharkLinkedMobyState(int instanceIndex)
    {
        int? link = _rac1AridiaEnemies.LinkedActivationMobyIndex(instanceIndex);
        if (!link.HasValue) return null;
        // Native class 1818 is the Aridia Sandmouse. Unless its native
        // controller is actually running, the required state >= 5 cannot be
        // inferred from elapsed time or the player's nearby position.
        return _rac1MobyRuntime.TryGet(new Rac1MobyRuntimeKey(Rac1AridiaEnemies.Sandmouse, link.Value),
            out var moby) && moby is { IsActive: true }
            ? moby.State.NativeState : null;
    }

    private static void HideBuriedRac1AridiaEnemy(Rac1HostedEnemy enemy) =>
        enemy.Node.Root.Visible = false;

    private void RestoreRac1AridiaEnemyVisibility(Rac1HostedEnemy enemy) =>
        enemy.Node.Root.Visible = IsRac1GameplayActive(enemy.Node) && !enemy.Buried;

    private void ApplyRac1AridiaPlayerDamage(RuntimeWorldScene.DynamicObjectNode source)
    {
        if (_rac1Nanotech.Probe().IsDead) return;
        // The original LEVEL2 outgoing damage descriptor is not recovered.
        // Keep this host adapter separate from any claimed native attack flags.
        var damage = new Rac1GameplayDamageEvent(
            new(Rac1GameplayEntityKind.Moby, source.Source.NativeClassId, source.Source.InstanceIndex),
            Rac1GameplayEntityRef.Player, 1);
        var before = _rac1Nanotech.Probe();
        _rac1DamageTransport.Publish(damage);
        var after = _rac1Nanotech.ApplyHostAridiaContactDamage();
        _player!.Rac1GameplayState = after;
        NotifyRac1PlayerDamageAudio(before, after);
        if (after.Nanotech != before.Nanotech)
            RefreshRac1HudState(OBP.RAC1.Presentation.Rac1HudProjection.DamageFeedback(before, after));
    }

    private void ActivateNearbyRac1AridiaShark(int nestInstance, Vector3 nestPosition)
    {
        // L2:0x2dd2a8 walks the nest's authored group (+0x21) and selects
        // class 580 in state 26 only. No distance-based candidate fallback.
        int? candidate = _rac1AridiaEnemies.FindWaitingSharkForNest(nestInstance);
        if (candidate is not int id || !_rac1Enemies.TryGetValue(id, out var shark)) return;
        if (!_rac1AridiaEnemies.ReleaseFromNest(nestInstance, id,
                ToEffectNative(nestPosition), ToEffectNative(shark.Node.Root.GlobalPosition))) return;
        var native = _rac1MobyRuntime.Require(shark.Node.Source);
        _rac1MobyRuntime.SetNativeState(native, Rac1AridiaEnemies.NestReleasedSharkState);
        var origin = nestPosition + Vector3.Up * Rac1AridiaEnemies.NestReleaseHeight;
        shark.Body.GlobalPosition = origin;
        shark.Node.Root.GlobalPosition = origin;
        shark.NeedsGroundPlacement = false;
        shark.Buried = false;
        shark.DeathTicks = 0;
        shark.Body.CollisionLayer = shark.Body.CollisionMask = 1;
        if (shark.Animation.SupportsSequence(Rac1AridiaEnemies.NestReleasedSharkSequence))
            SelectRac1EnemySequence(shark, Rac1AridiaEnemies.NestReleasedSharkSequence, 1);
        GD.Print($"[rac1-aridia] nest released authored class580 instance {id} with native state27/sequence11");
    }

    private bool ApplyRac1AridiaWeaponDamage(RuntimeWorldScene.DynamicObjectNode node,
        Rac1GameplayDamageEvent damage)
    {
        int id = node.Source.InstanceIndex;
        if (!_rac1AridiaEnemies.Damage(id, damage.NativeDamage)) return false;
        var enemy = _rac1Enemies[id];
        bool lethal = _rac1AridiaEnemies.Health(id) == 0;
        if (lethal)
        {
            enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
            enemy.DeathTicks = 0;
            int preferredSequence = node.Source.NativeClassId == Rac1AridiaEnemies.Nest ? 3 : 6;
            if (enemy.Animation.SupportsSequence(preferredSequence))
                SelectRac1EnemySequence(enemy, preferredSequence, 1);
            if (node.Source.NativeClassId is Rac1AridiaEnemies.Sandshark or Rac1AridiaEnemies.Constructorbot)
            {
                // L2:0x2d54e0 (shark) / 0x2d8430 (bot) call moby_drop_bolts
                // with (moby, 0, -1). Both use the shared authored instance
                // reward word at +0x10, not a fabricated fixed enemy amount.
                int selected = SelectRac1CrateReward(Rac1EnemyMotion.RewardCentre(node.Source));
                if (selected > 0)
                    SpawnRac1BoltPickups(node.Root.GlobalPosition, _rac1BoltPickups.Emit(selected));
            }
            // The native nest death path makes an explosion and debris but
            // does not invoke moby_drop_bolts in the translated routine.
        }
        else if (enemy.Animation.SupportsSequence(3))
            SelectRac1EnemySequence(enemy, 3, 1);
        GD.Print($"[rac1-aridia] class{node.Source.NativeClassId} i{id}: " +
            (lethal ? "defeated" : $"hit ({_rac1AridiaEnemies.Health(id)} remaining)"));
        return true;
    }
}

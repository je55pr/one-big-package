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
    private readonly Dictionary<int, float> _rac1ConstructorbotWalkerYawVelocities = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotSweepProbeCounts = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotSphereProbeCounts = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotSphereContactCounts = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotNativeLiftCounts = [];
    private SphereShape3D? _rac1ConstructorbotWalkerSphere;
    private readonly Dictionary<int, float> _rac1ConstructorbotStateThreeVerticalSteps = [];
    // Source table at 0x1600ec is the gameplay +0x60 list already decoded
    // as Rac1ClipVolumes: PVar+0x188 selects a record, +0x30 its position.
    private readonly Dictionary<int, Vector3> _rac1ConstructorbotNativeWaypoints = [];
    private readonly Dictionary<int, Vector3> _rac1ConstructorbotRoamingTargets = [];
    // Captures the source walker bitmask only when state6 exits to idle.
    // This lets the retail encounter distinguish arrival (bit4) from a
    // confirmed obstruction (bit2) without reconstructing past contacts.
    private readonly Dictionary<int, int> _rac1ConstructorbotRoamExitFlags = [];
    private IReadOnlyList<Rac1ClipVolume> _rac1AridiaClipVolumes = [];

    private void ResetRac1ConstructorbotStartups()
    {
        _rac1ConstructorbotStartups.Clear();
        _rac1ConstructorbotWalkerSpeeds.Clear();
        _rac1ConstructorbotWalkerYawVelocities.Clear();
        _rac1ConstructorbotSweepProbeCounts.Clear();
        _rac1ConstructorbotSphereProbeCounts.Clear();
        _rac1ConstructorbotSphereContactCounts.Clear();
        _rac1ConstructorbotNativeLiftCounts.Clear();
        _rac1ConstructorbotStateThreeVerticalSteps.Clear();
        _rac1ConstructorbotNativeWaypoints.Clear();
        _rac1ConstructorbotRoamingTargets.Clear();
        _rac1ConstructorbotRoamExitFlags.Clear();
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
        if (state == Rac1AridiaConstructorbotNative.RoamingState)
        {
            // Native 0x2d7bb4 feeds PVar+0x1e0, which is generated
            // ONLY by state7 after source animation completion. If the
            // target has not been observed do not invent a destination.
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
            if (_player is { } nearby &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance.InstanceIndex,
                    out var home) &&
                nearby.GlobalPosition.DistanceTo(home) <
                    Rac1AridiaConstructorbotNative.ReadEngagementRadius(instance.PVar.Span) &&
                MathF.Abs(nearby.GlobalPosition.Y - enemy.Node.Root.GlobalPosition.Y) < 4f &&
                enemy.Node.Root.GlobalPosition.DistanceTo(home) >= 1f)
            {
                // L2:0x2d7de4: in-range player, actor still too far from
                // source waypoint for joint attack; approach home first.
                _rac1MobyRuntime.SetNativeState(instance, 5);
                return new(5, 6);
            }
            var roam = Rac1AridiaConstructorbotNative.StepStateSevenRoam(
                enemy.Animation.Sequence == 0 && enemy.Animation.Wrapped);
            if (roam.NativeState == Rac1AridiaConstructorbotNative.RoamingState &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance.InstanceIndex,
                    out var roamAnchor))
            {
                var target = Rac1AridiaConstructorbotNative.CreateRoamingDestination(
                    ToEffectNative(roamAnchor), Rac1EffectRandom.Angle(),
                    Rac1EffectRandom.Range(1f, 2f), Rac1EffectRandom.Range(1f, 2f));
                _rac1ConstructorbotRoamingTargets[instance.InstanceIndex] =
                    FromEffectNative(target);
                _rac1ConstructorbotRoamExitFlags.Remove(instance.InstanceIndex);
                _rac1ConstructorbotWalkerSpeeds.Remove(instance.InstanceIndex);
                _rac1ConstructorbotWalkerYawVelocities.Remove(instance.InstanceIndex);
                _rac1MobyRuntime.SetNativeState(instance, roam.NativeState);
            }
            return roam;
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
    /// Host floor-height adapter for native state3 floor_probe_v(.5, moby, 0).
    /// The retail API's full vertical extent and hit filtering still need
    /// verification. Search a bounded 40-unit downward ray in real geometry;
    /// a Godot capsule sweep preserves a usable floor if the imported mesh
    /// lacks support directly beneath the Moby centre. No inferred height.
    /// </summary>
    private float? ProbeRac1ConstructorbotStateThreeFloor(
        Rac1HostedEnemy enemy, Vector3 position)
    {
        const float hostMaximumDescent = 40f;
        Vector3 from = position + Vector3.Up * .5f;
        Vector3 to = position - Vector3.Up * hostMaximumDescent;
        var ray = PhysicsRayQueryParameters3D.Create(from, to);
        ray.Exclude = [enemy.Body.GetRid(), _player!.GetRid()];
        var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.TryGetValue("position", out var hitPoint) &&
            hit.TryGetValue("normal", out var normal) &&
            normal.AsVector3().Y > .65f)
            return hitPoint.AsVector3().Y;

        var transform = enemy.Body.GlobalTransform;
        transform.Origin = from;
        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = transform,
            Motion = to - from,
            Margin = enemy.Body.SafeMargin,
            RecoveryAsCollision = true,
            MaxCollisions = 4,
            ExcludeBodies = new global::Godot.Collections.Array<Rid>(
                _rac1Enemies.Values.Select(other => other.Body.GetRid())
                    .Append(_player.GetRid()))
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(enemy.Body.GetRid(), parameters, result))
            return null;
        for (int i = 0; i < result.GetCollisionCount(); i++)
            if (result.GetCollisionNormal(i).Y > .65f)
                return from.Y + result.GetTravel().Y;
        return null;
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

    /// <summary>
    /// Host counterpart of sphere_collide_v's corrected-centre loop.
    /// Godot CollideShape provides contact pairs, NOT the original hit
    /// record +0x30. Choose the nearest bounded world contact from
    /// observed Godot pairs, apply its separation to the query sphere,
    /// and re-query at most six times. Original Moby eligibility/primitive
    /// masks and PS2 triangle flags are NOT inferred from host capsules.
    /// The original actor's body is only moved by the separate host
    /// motion adapter after these tentative corrections have finished.
    /// </summary>
    private Rac1AridiaConstructorbotNative.WalkerPushoutFrame
        ProbeRac1AridiaWalkerSpherePushouts(Rac1HostedEnemy enemy, Vector3 proposed)
    {
        _rac1ConstructorbotWalkerSphere ??= new SphereShape3D
        {
            Radius = Rac1AridiaConstructorbotNative.WalkerCollisionRadius
        };
        int instance = enemy.Node.Source.InstanceIndex;
        int observedContacts = 0;
        var excluded = new global::Godot.Collections.Array<Rid>(
            _rac1Enemies.Values.Select(other => other.Body.GetRid())
                .Append(_player!.GetRid()));
        var result = Rac1AridiaConstructorbotNative.StepSpherePushouts(
            ToEffectNative(proposed), sourceCandidate =>
            {
                _rac1ConstructorbotSphereProbeCounts[instance] =
                    _rac1ConstructorbotSphereProbeCounts.GetValueOrDefault(instance) + 1;
                var candidate = FromEffectNative(sourceCandidate);
                using var query = new PhysicsShapeQueryParameters3D
                {
                    Shape = _rac1ConstructorbotWalkerSphere,
                    Transform = new(Basis.Identity,
                        candidate + Vector3.Up *
                        Rac1AridiaConstructorbotNative.WalkerCollisionUp),
                    Exclude = excluded
                };
                var pairs = enemy.Body.GetWorld3D().DirectSpaceState.CollideShape(query, 8);
                // Native sphere_collide_v retains the NEAREST world
                // surface/primitive contact to its query centre; selecting
                // Godot's deepest/largest correction instead can wrongly
                // prefer a farther surface at corners. The observed Godot
                // pairs are still only a host approximation of the source
                // primitive records and collision-centre recurrence.
                // Godot provides at most eight points (four pairs).
                // Keep this hot-path sample on the stack rather than
                // allocating a List for each of the six native attempts.
                Span<Rac1AridiaConstructorbotNative.WalkerSpherePointPair> candidates =
                    stackalloc Rac1AridiaConstructorbotNative.WalkerSpherePointPair[4];
                int count = 0;
                for (int pair = 0; pair + 1 < pairs.Count && count < candidates.Length;
                    pair += 2)
                    candidates[count++] = new(
                        ToEffectNative(pairs[pair]),
                        ToEffectNative(pairs[pair + 1]));
                // sphere_collide_v queries at Moby+record+4 up, not at
                // Moby's foot position. Compare distances from that
                // raised native centre while retaining foot-relative push.
                var sourceSphereCentre = sourceCandidate +
                    System.Numerics.Vector3.UnitZ *
                    Rac1AridiaConstructorbotNative.WalkerCollisionUp;
                var nearest = Rac1AridiaConstructorbotNative.NearestSpherePairCorrection(
                    sourceSphereCentre, candidates[..count],
                    Rac1AridiaConstructorbotNative.WalkerCollisionRadius);
                if (nearest is null) return null;
                observedContacts++;
                return sourceCandidate + nearest.Value;
            });
        if (observedContacts > 0)
            _rac1ConstructorbotSphereContactCounts[instance] =
                _rac1ConstructorbotSphereContactCounts.GetValueOrDefault(instance) +
                observedContacts;
        return result;
    }

    /// <summary>
    /// Source walker_move_v 0x258830 post-movement vertical segment.
    /// This is deliberately separate from the earlier +/-0.5 step-admission
    /// probe: record+0x30 is zero for class612, so the original ray ends
    /// only 0.05 below the proposed native Moby origin. It requires a real
    /// imported terrain hit, not an inferred floor from the host capsule.
    /// Godot's triangle normals and Moby filtering still differ from PS2.
    /// </summary>
    private float? ProbeRac1AridiaWalkerGroundLift(
        Rac1HostedEnemy enemy, Vector3 candidate)
    {
        var segment = Rac1AridiaConstructorbotNative.WalkerGroundSegment(
            ToEffectNative(candidate),
            Rac1AridiaConstructorbotNative.WalkerCollisionUp,
            Rac1AridiaConstructorbotNative.ConstructorbotFloorDrop);
        var query = PhysicsRayQueryParameters3D.Create(
            FromEffectNative(segment.Start), FromEffectNative(segment.End));
        query.Exclude = new global::Godot.Collections.Array<Rid>(
            _rac1Enemies.Values.Select(other => other.Body.GetRid())
                .Append(_player!.GetRid()));
        var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("position", out var contact) ||
            !hit.TryGetValue("normal", out var normal) ||
            normal.AsVector3().Y <= .65f)
            return null;
        return Rac1AridiaConstructorbotNative.WalkerGroundLift(
            ToEffectNative(candidate).Z,
            ToEffectNative(contact.AsVector3()).Z,
            Rac1AridiaConstructorbotNative.ConstructorbotFloorDrop);
    }

    /// <summary>
    /// Bounded Godot collision adapter for source walker_move_fx. Unlike
    /// generic enemy movement this returns whether an ACTUAL collision
    /// occurred; the native walker uses that fact to distinguish contact
    /// bit1 from poor forward progress bit2. Godot's three slide attempts
    /// replace native swept segment + six sphere pushouts, pending a
    /// complete PS2-to-host collision equivalence mapping.
    /// </summary>
    private bool MoveRac1AridiaWalkerContact(Rac1HostedEnemy enemy, Vector3 remaining)
    {
        bool touched = false;
        // Source walker_move_v sweeps only if requested 3D travel exceeds
        // the initializer's 0x280 / 1024 = 0.625 radius. The segment
        // extends one radius past the proposed endpoint. The Godot ray
        // observes imported static collision, not source segment_collide
        // hit-record metadata or moby interpenetration; character contact
        // below remains a separate bounded host adapter.
        Vector3 previous = enemy.Body.GlobalPosition;
        var sweep = Rac1AridiaConstructorbotNative.BuildWalkerSweep(
            ToEffectNative(previous), ToEffectNative(remaining),
            Rac1AridiaConstructorbotNative.WalkerCollisionRadius,
            Rac1AridiaConstructorbotNative.WalkerCollisionUp);
        if (sweep is { } sourceSweep)
        {
            int actor = enemy.Node.Source.InstanceIndex;
            _rac1ConstructorbotSweepProbeCounts[actor] =
                _rac1ConstructorbotSweepProbeCounts.GetValueOrDefault(actor) + 1;
            var query = PhysicsRayQueryParameters3D.Create(
                FromEffectNative(sourceSweep.Start),
                FromEffectNative(sourceSweep.End));
            query.Exclude = new global::Godot.Collections.Array<Rid>(
                _rac1Enemies.Values.Select(other => other.Body.GetRid())
                    .Append(_player!.GetRid()));
            var hit = enemy.Body.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.TryGetValue("position", out var hitPoint))
            {
                Vector3 sourceStopped = FromEffectNative(
                    Rac1AridiaConstructorbotNative.WalkerSweptHitPosition(
                        ToEffectNative(hitPoint.AsVector3()), sourceSweep,
                        Rac1AridiaConstructorbotNative.WalkerCollisionUp));
                remaining = sourceStopped - previous;
                touched = true;
            }
        }
        // Source performs sphere_collide_v even when the segment was
        // skipped for a sub-radius step. Translate its six returned
        // corrected centres through the host's point-pair query before
        // applying the final bounded Godot character movement.
        var pushout = ProbeRac1AridiaWalkerSpherePushouts(
            enemy, previous + remaining);
        if (pushout.Contacts > 0)
        {
            remaining = FromEffectNative(pushout.Position) - previous;
            touched = true;
        }
        for (int slide = 0; slide < 3 && remaining.LengthSquared() > 1e-9f; slide++)
        {
            var collision = enemy.Body.MoveAndCollide(remaining);
            if (collision is null) break;
            touched = true;
            enemy.Reaction?.AcceptHostContact(ToEffectNative(collision.GetNormal()));
            remaining = collision.GetRemainder().Slide(collision.GetNormal());
        }
        // The retail walker_move_v raises its Moby 30% toward a source
        // segment-observed floor above the candidate. Apply that correction
        // before the generic Godot shape snap, which would otherwise replace
        // the partial lift with a full floor relocation. Use a bounded
        // shape move to avoid moving through a ceiling; if it cannot lift,
        // retain the existing observed-contact grounding fallback.
        float? sourceLift = ProbeRac1AridiaWalkerGroundLift(
            enemy, enemy.Body.GlobalPosition);
        bool appliedNativeLift = false;
        if (sourceLift is > 0)
        {
            float previousY = enemy.Body.GlobalPosition.Y;
            enemy.Body.MoveAndCollide(Vector3.Up * sourceLift.Value);
            appliedNativeLift = enemy.Body.GlobalPosition.Y > previousY + .00001f;
            if (appliedNativeLift)
            {
                int actor = enemy.Node.Source.InstanceIndex;
                _rac1ConstructorbotNativeLiftCounts[actor] =
                    _rac1ConstructorbotNativeLiftCounts.GetValueOrDefault(actor) + 1;
            }
        }
        bool rising = enemy.Reaction?.Velocity.Z > 0;
        enemy.Grounded = !rising && (appliedNativeLift ||
            TryGroundRac1Enemy(enemy, upwardReach: 0, downwardReach: .3f));
        enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        return touched;
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
            int nativeStateAtTickStart = _rac1MobyRuntime.Require(node.Source).State.NativeState;
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
                    Vector3 forward = EnemyForward(node);
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
            MoveRac1EnemyContact(enemy, FromEffectNative(frame.Motion));
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
                _rac1MobyRuntime.SetNativeState(_rac1MobyRuntime.Require(node.Source), 4);
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
            int walkingState = _rac1MobyRuntime.Require(node.Source).State.NativeState;
            bool returnToHome = walkingState ==
                Rac1AridiaConstructorbotNative.RoamingState &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance, out var anchor) &&
                player.DistanceTo(anchor) <
                    Rac1AridiaConstructorbotNative.ReadEngagementRadius(
                        _rac1MobyRuntime.Require(node.Source).PVar.Span) &&
                MathF.Abs(player.Y - enemy.Body.GlobalPosition.Y) < 4f;
            var walker = Rac1AridiaConstructorbotNative.StepWalkerState(
                walkingState, flags,
                resultKnown: sourceWalkerProbeFlags != 0 || enemy.Grounded,
                playerNearAnchor: returnToHome);
            if (walker.NativeState != walkingState)
            {
                _rac1MobyRuntime.SetNativeState(_rac1MobyRuntime.Require(node.Source),
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

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
    private bool AridiaConstructorbotJointTouchesPlayer(Rac1HostedEnemy enemy)
    {
        // Host collision adapter for native LEVEL2:0x2d812c..0x2d8228.
        // It starts at the original sequence's semantic joint-0 socket, and
        // approximates the native segment/contact solver with a Godot ray.
        // The native shape, descriptor flags and target filters still differ.
        Vector3 origin = _host.EnemySocket(enemy);
        Vector3 direction = Rac1EnemyHost.EnemyForward(enemy.Node);
        Vector3 target = origin + direction + Vector3.Down * .1f;
        return _host.EnemyRayTouchesPlayer(enemy, origin, target);
    }

    /// <summary>
    /// Host floor-height adapter for native state3 floor_probe_v(.5, moby, 0).
    /// The retail API's full vertical extent and hit filtering still need
    /// verification. Search a bounded 40-unit downward ray in real geometry;
    /// a Godot capsule sweep preserves a usable floor if the imported mesh
    /// lacks support directly beneath the Moby centre. No inferred height.
    /// </summary>
    internal float? ProbeRac1ConstructorbotStateThreeFloor(
        Rac1HostedEnemy enemy, Vector3 position)
    {
        const float hostMaximumDescent = 40f;
        Vector3 from = position + Vector3.Up * .5f;
        Vector3 to = position - Vector3.Up * hostMaximumDescent;
        // Terrain queries must not report another hosted enemy capsule as
        // floor. Share exclusions with the shape-sweep fallback below.
        var excludedActors = ActorExclusions();
        var ray = PhysicsRayQueryParameters3D.Create(from, to);
        ray.Exclude = excludedActors;
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
            ExcludeBodies = excludedActors
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(enemy.Body.GetRid(), parameters, result))
            return null;
        for (int i = 0; i < result.GetCollisionCount(); i++)
            if (result.GetCollisionNormal(i).Y > .65f)
                // BodyTestMotion travel is the capsule origin displacement,
                // not the actual contact surface's elevation. Landing must
                // follow the imported terrain, including at ray-missed edges.
                return result.GetCollisionPoint(i).Y;
        return null;
    }

    /// <summary>
    /// Host adapter for the native 0x2583f0 walker_probe vertical segment.
    /// The source uses collision kind 0x22 and hit-record metadata. Godot
    /// supplies a real scene raycast, excluding all hosted actors and Ratchet.
    /// This never infers a floor from IsOnFloor or elapsed ticks.
    /// </summary>
    internal bool ProbeRac1AridiaWalkerSupport(
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
        // Match the capsule sweep's actor exclusions. A different enemy
        // sitting over terrain must not become the source walker floor.
        var excludedActors = ActorExclusions();
        var query = PhysicsRayQueryParameters3D.Create(start, end);
        query.Exclude = excludedActors;
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
            ExcludeBodies = excludedActors
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(enemy.Body.GetRid(), parameters, result))
            return false;
        for (int i = 0; i < result.GetCollisionCount(); i++)
        {
            Vector3 normal = result.GetCollisionNormal(i);
            if (normal.Y <= .65f) continue;
            // The sweep reports body-origin travel separately from its
            // terrain contact. Walker's +/-0.5 floor tolerance is measured
            // against the actual surface, not the capsule's elevated origin.
            floorY = result.GetCollisionPoint(i).Y;
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
    internal Rac1AridiaConstructorbotNative.WalkerPushoutFrame
        ProbeRac1AridiaWalkerSpherePushouts(Rac1HostedEnemy enemy, Vector3 proposed)
    {
        _rac1ConstructorbotWalkerSphere ??= new SphereShape3D
        {
            Radius = Rac1AridiaConstructorbotNative.WalkerCollisionRadius
        };
        int instance = enemy.Node.Source.InstanceIndex;
        var excluded = ActorExclusions();
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
                // Count the native contact at the point of observation. This
                // avoids inferring a nullable result from the callback later.
                _rac1ConstructorbotSphereContactCounts[instance] =
                    _rac1ConstructorbotSphereContactCounts.GetValueOrDefault(instance) + 1;
                return sourceCandidate + nearest.Value;
            });
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
    internal float? ProbeRac1AridiaWalkerGroundLift(
        Rac1HostedEnemy enemy, Vector3 candidate)
    {
        var segment = Rac1AridiaConstructorbotNative.WalkerGroundSegment(
            ToEffectNative(candidate),
            Rac1AridiaConstructorbotNative.WalkerCollisionUp,
            Rac1AridiaConstructorbotNative.ConstructorbotFloorDrop);
        var query = PhysicsRayQueryParameters3D.Create(
            FromEffectNative(segment.Start), FromEffectNative(segment.End));
        query.Exclude = ActorExclusions();
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
    internal bool MoveRac1AridiaWalkerContact(Rac1HostedEnemy enemy, Vector3 remaining)
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
            query.Exclude = ActorExclusions();
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
            _host.TryGroundRac1Enemy(enemy, upwardReach: 0, downwardReach: .3f));
        enemy.Node.Root.GlobalPosition = enemy.Body.GlobalPosition;
        return touched;
    }
}

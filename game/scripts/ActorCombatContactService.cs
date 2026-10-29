using OBP.Core;
using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record ActorContactPorts(Func<PhysicsDirectSpaceState3D> Space,
    Func<global::Godot.Collections.Array<Rid>> Exclusions, Func<Vector3, Vector3, bool> LineOfSight,
    Action<string> Status, Func<long?> WrenchGeneration, Action WrenchFeedback);

internal sealed class ActorCombatContactService(ActorWorldHost world, ActorContactPorts ports, Func<IEnumerable<HostedActor>> views)
{
    internal IEnumerable<WeaponContact> Trace(WeaponQuery query, Vector3 muzzle)
    {
        Vector3 from = new(query.From.X, query.From.Y, query.From.Z), to = new(query.To.X, query.To.Y, query.To.Z);
        var geometry = ReadGeometry().Where(target => FusionDamagePolicy.AdmitsQueryTarget(query.Shot.Weapon, target.Key)).ToArray();
        if (query.Kind == WeaponQueryKind.Sphere)
        {
            foreach (var target in geometry)
            {
                Vector3 nearest = new(Mathf.Clamp(from.X, target.Bounds.Position.X, target.Bounds.End.X), Mathf.Clamp(from.Y, target.Bounds.Position.Y, target.Bounds.End.Y), Mathf.Clamp(from.Z, target.Bounds.Position.Z, target.Bounds.End.Z));
                if (nearest.DistanceSquaredTo(from) <= query.Radius * query.Radius && ports.LineOfSight(muzzle, nearest))
                    yield return new(query.Index, target.Key, 0, muzzle.DistanceTo(nearest));
            }
            yield break;
        }
        float length = from.DistanceTo(to);
        if (length < .000001f) yield break;
        var ray = PhysicsRayQueryParameters3D.Create(from, to); ray.Exclude = ports.Exclusions(); ray.HitFromInside = true;
        var hit = ports.Space().IntersectRay(ray);
        float closest = hit.TryGetValue("position", out var point) ? from.DistanceTo(point.AsVector3()) / length : float.PositiveInfinity;
        WeaponTargetKey? key = null;
        foreach (var target in geometry)
        {
            if (!ActorContactGeometry.SegmentBoxFraction(from, to, target.Bounds.Grow(query.Radius), out float fraction) || fraction > closest + .00001f) continue;
            closest = fraction; key = target.Key;
        }
        if (float.IsFinite(closest)) yield return new(query.Index, key, Mathf.Clamp(closest, 0, 1), length * closest);
    }

    internal IEnumerable<(WeaponTargetKey Key, RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)> ReadGeometry()
    {
        foreach (var actor in views().OrderBy(actor => actor.Registration.Instance.Target.SourceGame)
            .ThenBy(actor => actor.Registration.Instance.Target.NativeClassId).ThenBy(actor => actor.Registration.Instance.Target.InstanceIndex))
        {
            if (!actor.Observe().Alive || !GodotObject.IsInstanceValid(actor.Node.Root)) continue;
            var key = actor.Registration.Instance.Target;
            Aabb? bounds = null;
            foreach (var mesh in actor.Node.Root.GetChildren().OfType<MeshInstance3D>())
            { var next = mesh.GlobalTransform * mesh.GetAabb(); bounds = bounds?.Merge(next) ?? next; }
            if (bounds is { } box) yield return (key, actor.Node, box);
        }
    }

    internal bool Apply(WeaponDamageIntent intent)
    {
        var result = world.ApplyContact(intent);
        if (result is null) return false;
        if (!result.Applied && result.Reason is { } reason) ports.Status(reason);
        return result.Applied;
    }

    /// <summary>
    /// OBP cross-source contact bridge for an already-emitted native R&C1 flame
    /// particle. Does not advance particles, decide native victim consequences,
    /// or replace the original R&C1 contact path. Each particle has an independent
    /// source-qualified receipt set; source-owned actor adapters decide damage.
    /// </summary>
    internal IReadOnlyList<WeaponDamageIntent> ResolveFlame(
        SourceWeaponKey weapon, long generation, long particleSequence,
        Vector3 position, float radius, float damage, uint nativeFlags,
        ISet<WeaponTargetKey> particleVictims)
    {
        ArgumentNullException.ThrowIfNull(particleVictims);
        if (!world.Active || weapon.SourceGame != ObpSourceGame.Rac1 ||
            weapon.NativeItemId != (int)Rac1WeaponId.Pyrocitor ||
            generation < 0 || particleSequence < 0 ||
            !position.IsFinite() || !float.IsFinite(radius) || radius <= 0f ||
            !float.IsFinite(damage) || damage <= 0f)
            return Array.Empty<WeaponDamageIntent>();

        var accepted = new List<WeaponDamageIntent>();
        foreach (var (key, node, bounds) in ReadGeometry())
        {
            // R&C1 retail victim contacts are still handled by its native
            // weapon-world path. This explicit OBP route only spans sources.
            if (key.SourceGame == ObpSourceGame.Rac1 || particleVictims.Contains(key) ||
                !ActorContactGeometry.FlamePointIntersects(bounds, position, radius) ||
                !ports.LineOfSight(position, node.Root.GlobalPosition + Vector3.Up * .4f))
                continue;

            var intent = new WeaponDamageIntent(
                new(weapon, generation, particleSequence), key,
                damage, nativeFlags, "native-flame");
            if (!Apply(intent)) continue;
            particleVictims.Add(key);
            accepted.Add(intent);
        }
        return accepted;
    }

    internal void ResolveBlast(SourceWeaponKey weapon, long generation, long sequence,
        Vector3 origin, float radius, float damage, uint flags)
    {
        if (!world.Active) return;
        foreach (var target in ReadGeometry().Where(target => target.Key.SourceGame != ObpSourceGame.Rac1))
        {
            Vector3 closest = new(Mathf.Clamp(origin.X, target.Bounds.Position.X, target.Bounds.End.X),
                Mathf.Clamp(origin.Y, target.Bounds.Position.Y, target.Bounds.End.Y), Mathf.Clamp(origin.Z, target.Bounds.Position.Z, target.Bounds.End.Z));
            if (closest.DistanceTo(origin) > radius || !ports.LineOfSight(origin, closest)) continue;
            Apply(new(new(weapon, generation, sequence), target.Key, damage, flags, "native-blast"));
        }
    }

    internal void ResolveWrench(Vector3 previousHand, Vector3 previousTip, Vector3 hand, Vector3 tip, float radius, float damage)
    {
        if (!world.Active || ports.WrenchGeneration() is not { } generation) return;
        foreach (var target in ReadGeometry().Where(target => target.Key.SourceGame != ObpSourceGame.Rac1))
        {
            var bounds = target.Bounds.Grow(radius);
            if (!(ActorContactGeometry.WrenchSegmentIntersects(bounds, previousHand, previousTip) || ActorContactGeometry.WrenchSegmentIntersects(bounds, hand, tip) ||
                ActorContactGeometry.WrenchSegmentIntersects(bounds, previousHand, hand) || ActorContactGeometry.WrenchSegmentIntersects(bounds, previousTip, tip) ||
                ActorContactGeometry.WrenchSegmentIntersects(bounds, previousHand, tip) || ActorContactGeometry.WrenchSegmentIntersects(bounds, previousTip, hand))) continue;
            if (Apply(new(new(PlayerEquipmentRegistrations.Wrench, generation, 0), target.Key,
                damage, Rac1WrenchAttackProfile.NativeDamageFlags, "native-wrench")))
                ports.WrenchFeedback();
        }
    }
}

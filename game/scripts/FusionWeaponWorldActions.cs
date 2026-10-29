using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>Physics projection for the existing native sessions and source-qualified actor ports.</summary>
internal sealed class FusionWeaponWorldActions(FusionWeaponContactPorts ports)
{
    internal Vector3 Muzzle(SourceWeaponKey selected, IReadOnlyDictionary<SourceWeaponKey, FusionMeshView> held)
    {
        var player = ports.Player() ?? throw new InvalidOperationException("Weapon physics requires a player.");
        var forward = ports.Forward();
        Vector3 muzzle = player.GlobalPosition + Vector3.Up * .9f + forward * .5f;
        if (held.TryGetValue(selected, out var view))
        {
            var point = FusionWeaponMount.Muzzle(selected);
            muzzle = view.ToGlobal(new Vector3(point.X, point.Y, point.Z));
        }
        var ray = PhysicsRayQueryParameters3D.Create(player.GlobalPosition + Vector3.Up * .8f, muzzle);
        ray.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.TryGetValue("position", out var position) ? position.AsVector3() - forward * .03f : muzzle;
    }

    internal Vector3 Aim(SourceWeaponKey selected, Vector3 origin)
    {
        float range = selected == FusionWeaponSession.Lancer ? Rac2LancerProfile.ProjectileRange : Rac3ShockCannonProfile.RayLength;
        var forward = ports.Forward();
        bool ordinary = ports.Ordinary();
        return ports.Geometry().Where(target => target.Active && (ordinary
                ? FusionDamagePolicy.AdmitsQueryTarget(selected, target.Key) : FusionDamagePolicy.Admits(target.Key)))
            .Where(target => selected != FusionWeaponSession.Lancer || target.Key.SourceGame != ObpSourceGame.Rac1 || target.Key.NativeClassId != Rac1ClassIds.VeldinToad)
            .Select(target => target.Bounds.GetCenter()).Where(point => point.DistanceSquaredTo(origin) is > .1f && point.DistanceSquaredTo(origin) < range * range &&
                (point - origin).Normalized().Dot(forward) > .9f && ports.LineOfSight(origin, point))
            .OrderBy(point => point.DistanceSquaredTo(origin)).FirstOrDefault(origin + forward * range);
    }

    internal IEnumerable<WeaponContact> Trace(WeaponQuery query, Vector3 muzzle)
    {
        if (ports.Ordinary()) return ports.TraceOrdinary(query, muzzle);
        return TraceParity(query, muzzle);
    }

    private IEnumerable<WeaponContact> TraceParity(WeaponQuery query, Vector3 muzzle)
    {
        Vector3 from = ToGodot(query.From), to = ToGodot(query.To);
        var candidates = ports.Geometry().Where(target => target.Active && !target.RewardEmitted && FusionDamagePolicy.Admits(target.Key))
            .OrderBy(target => target.Key.NativeClassId).ThenBy(target => target.Key.InstanceIndex);
        if (query.Kind == WeaponQueryKind.Sphere)
        {
            foreach (var target in candidates)
            {
                var bounds = target.Bounds;
                Vector3 nearest = new(Mathf.Clamp(from.X, bounds.Position.X, bounds.End.X), Mathf.Clamp(from.Y, bounds.Position.Y, bounds.End.Y), Mathf.Clamp(from.Z, bounds.Position.Z, bounds.End.Z));
                if (nearest.DistanceSquaredTo(from) > query.Radius * query.Radius || !ports.LineOfSight(muzzle, nearest)) continue;
                yield return new(query.Index, target.Key, 0, muzzle.DistanceTo(nearest));
            }
            yield break;
        }
        var ray = PhysicsRayQueryParameters3D.Create(from, to);
        ray.Exclude = ports.Exclusions();
        ray.HitFromInside = true;
        var hit = ports.Player()!.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        float length = from.DistanceTo(to);
        float closest = hit.TryGetValue("position", out var point) ? from.DistanceTo(point.AsVector3()) / length : float.PositiveInfinity;
        WeaponTargetKey? key = null;
        foreach (var target in candidates)
        {
            if (!ActorContactGeometry.SegmentBoxFraction(from, to, target.Bounds.Grow(query.Radius), out float fraction) || fraction > closest + .00001f) continue;
            closest = fraction;
            key = target.Key;
        }
        if (float.IsFinite(closest)) yield return new(query.Index, key, Mathf.Clamp(closest, 0, 1), length * closest);
    }

    internal bool Apply(WeaponDamageIntent intent) => ports.Apply(intent);
    internal Vector3 Forward => ports.Forward();
    private static Vector3 ToGodot(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}

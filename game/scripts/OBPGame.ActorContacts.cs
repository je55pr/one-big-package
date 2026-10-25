using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private IEnumerable<WeaponContact> TraceRegisteredWeaponQuery(WeaponQuery query, Vector3 muzzle)
    {
        Vector3 from = ToGodot(query.From), to = ToGodot(query.To);
        var geometry = RegisteredCombatGeometry().Where(target => FusionDamagePolicy.AdmitsQueryTarget(query.Shot.Weapon, target.Key)).ToArray();
        if (query.Kind == WeaponQueryKind.Sphere)
        {
            foreach (var target in geometry)
            {
                Vector3 nearest = new(Mathf.Clamp(from.X, target.Bounds.Position.X, target.Bounds.End.X), Mathf.Clamp(from.Y, target.Bounds.Position.Y, target.Bounds.End.Y), Mathf.Clamp(from.Z, target.Bounds.Position.Z, target.Bounds.End.Z));
                if (nearest.DistanceSquaredTo(from) <= query.Radius * query.Radius && BombHasLineOfSight(muzzle, nearest))
                    yield return new(query.Index, target.Key, 0, muzzle.DistanceTo(nearest));
            }
            yield break;
        }
        float length = from.DistanceTo(to);
        if (length < .000001f) yield break;
        var ray = PhysicsRayQueryParameters3D.Create(from, to); ray.Exclude = Rac1DamageableRayExclusions(); ray.HitFromInside = true;
        foreach (var body in _actorContactBodies.Values)
            if (IsInstanceValid(body)) ray.Exclude.Add(body.GetRid());
        var hit = _player!.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        float closest = hit.TryGetValue("position", out var point) ? from.DistanceTo(point.AsVector3()) / length : float.PositiveInfinity;
        WeaponTargetKey? key = null;
        foreach (var target in geometry)
        {
            if (!SegmentBoxFraction(from, to, target.Bounds.Grow(query.Radius), out float fraction) || fraction > closest + .00001f) continue;
            closest = fraction; key = target.Key;
        }
        if (float.IsFinite(closest)) yield return new(query.Index, key, Mathf.Clamp(closest, 0, 1), length * closest);
    }

    private IEnumerable<(WeaponTargetKey Key, RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)> RegisteredCombatGeometry()
    {
        foreach (var (key, actor) in _actorViews.OrderBy(pair => pair.Key.SourceGame, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.NativeClassId).ThenBy(pair => pair.Key.InstanceIndex))
        {
            if (!actor.Observe().Alive || !IsInstanceValid(actor.Node.Root)) continue;
            Aabb? bounds = null;
            foreach (var mesh in actor.Node.Root.GetChildren().OfType<MeshInstance3D>())
            { var next = mesh.GlobalTransform * mesh.GetAabb(); bounds = bounds?.Merge(next) ?? next; }
            if (bounds is { } box) yield return (key, actor.Node, box);
        }
    }

    private bool ApplyRegisteredWeaponContact(WeaponDamageIntent intent)
    {
        if (_actors is null || _actors.Find(intent.Target) is not { } actor) return false;
        var result = _actors.Apply(actor.Registration.Instance, intent);
        if (!result.Applied && result.Reason is { } reason) _rac1CombatStatus = reason;
        return result.Applied;
    }

    private void ResolveRegisteredBlast(SourceWeaponKey weapon, long generation, long sequence,
        Vector3 origin, float radius, float damage, uint flags)
    {
        if (_actors is null) return;
        foreach (var target in RegisteredCombatGeometry().Where(target => target.Key.SourceGame != "rac1"))
        {
            Vector3 closest = new(Mathf.Clamp(origin.X, target.Bounds.Position.X, target.Bounds.End.X),
                Mathf.Clamp(origin.Y, target.Bounds.Position.Y, target.Bounds.End.Y), Mathf.Clamp(origin.Z, target.Bounds.Position.Z, target.Bounds.End.Z));
            if (closest.DistanceTo(origin) > radius || !BombHasLineOfSight(origin, closest)) continue;
            ApplyRegisteredWeaponContact(new(new(weapon, generation, sequence), target.Key, damage, flags, "native-blast"));
        }
    }

    private void ResolveRegisteredWrenchContacts(Vector3 previousHand, Vector3 previousTip, Vector3 hand, Vector3 tip, float radius, float damage)
    {
        if (_actors is null || _player is null) return;
        foreach (var target in RegisteredCombatGeometry().Where(target => target.Key.SourceGame != "rac1"))
        {
            var bounds = target.Bounds.Grow(radius);
            if (!(WrenchSegmentIntersects(bounds, previousHand, previousTip) || WrenchSegmentIntersects(bounds, hand, tip) ||
                WrenchSegmentIntersects(bounds, previousHand, hand) || WrenchSegmentIntersects(bounds, previousTip, tip) ||
                WrenchSegmentIntersects(bounds, previousHand, tip) || WrenchSegmentIntersects(bounds, previousTip, hand))) continue;
            if (ApplyRegisteredWeaponContact(new(new(PlayerEquipmentRegistrations.Wrench, _player.Rac1WrenchFrame.Generation, 0), target.Key,
                damage, Rac1WrenchAttackProfile.NativeDamageFlags, "native-wrench")) && _rac1PlayerAudio?.WrenchContact(false) is { } cue)
                PlayRac1PlayerSound(cue);
        }
    }
}

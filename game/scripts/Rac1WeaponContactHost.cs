using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

// Only source-world target/contact facts are borrowed. The persistent inventory,
// damage authority, effect factory and weapon family sessions remain elsewhere.
internal sealed record Rac1WeaponTargetPorts(
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> Targets,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> QueenTargets,
    Func<int, Node3D?> MorphView,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> IsGameplayActive,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> IsQueenNode,
    Func<bool> QueenDamageAdmitted,
    Func<int, bool> RewardEmitted,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> ArsenalTargets,
    Func<int, bool> IsMorphed);

internal sealed record Rac1WeaponContactPhysicsPorts(
    Func<PlayerHost?> Player,
    Func<Vector3> Forward,
    Func<IEnumerable<CollisionObject3D>> DamageableBodies,
    Action<global::Godot.Collections.Array<Rid>> AddRayExclusions);

/// <summary>
/// Source-world contact and target geometry shared by R&C1 native weapon
/// families and the combat/contact host. One cached geometry/exclusion view
/// per physics frame; world retirement invalidates every borrowed reference.
/// No gameplay damage or native timing rules are inferred here.
/// </summary>
internal sealed class Rac1WeaponContactHost(
    Rac1WeaponTargetPorts targets, Rac1WeaponContactPhysicsPorts physics) : IDisposable
{
    private ulong _geometryFrame = ulong.MaxValue;
    private (RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)[] _geometry = [];
    private ulong _occlusionFrame = ulong.MaxValue;
    private Rid _occlusionPlayer;
    private global::Godot.Collections.Array<Rid> _occlusionExclusions = [];
    // Native 1efc70 primitive/body representation is still unresolved; retain
    // the existing host sweep adapter rather than inventing PS2 collision.
    private readonly SphereShape3D _bombContactShape = new() { Radius = .12f };
    private bool _disposed;

    internal void Invalidate()
    {
        _geometryFrame = _occlusionFrame = ulong.MaxValue;
        _geometry = [];
        _occlusionPlayer = default;
        _occlusionExclusions = [];
    }

    internal IReadOnlyList<(RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)> Geometry
    {
        get
        {
            if (_disposed) return [];
            ulong frame = Engine.GetPhysicsFrames();
            if (_geometryFrame == frame) return _geometry;
            _geometryFrame = frame;
            var geometry = new List<(RuntimeWorldScene.DynamicObjectNode, Aabb)>();
            foreach (var node in targets.Targets().Concat(targets.QueenTargets()))
            {
                if (!GodotObject.IsInstanceValid(node.Root) ||
                    (node.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId
                        ? !targets.QueenDamageAdmitted()
                        : !targets.IsGameplayActive(node)) ||
                    targets.RewardEmitted(node.Source.InstanceIndex)) continue;
                Aabb? bounds = null;
                var visual = targets.MorphView(node.Source.InstanceIndex) ?? node.Root;
                foreach (var mesh in visual.GetChildren().OfType<MeshInstance3D>())
                {
                    var world = mesh.GlobalTransform * mesh.GetAabb();
                    bounds = bounds?.Merge(world) ?? world;
                }
                if (bounds is { } box) geometry.Add((node, box));
            }
            return _geometry = geometry.ToArray();
        }
    }

    internal IEnumerable<RuntimeWorldScene.DynamicObjectNode> Targets => Geometry
        .Select(entry => entry.Node).Where(node =>
            node.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId
                ? targets.QueenDamageAdmitted()
                : targets.IsGameplayActive(node));

    internal bool IsAimTarget(RuntimeWorldScene.DynamicObjectNode node) =>
        !_disposed && (node.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId
            ? targets.IsQueenNode(node)
            : targets.IsGameplayActive(node) && !targets.RewardEmitted(node.Source.InstanceIndex));

    internal Vector3 Aim(Vector3 origin, float distance, float coneDot)
    {
        var forward = physics.Forward();
        var target = targets.Targets().Concat(targets.QueenTargets()).Where(IsAimTarget)
            .Select(node => node.Root.GlobalPosition + Vector3.Up * .45f)
            .Where(point => point.DistanceSquaredTo(origin) is > .1f && point.DistanceSquaredTo(origin) < distance * distance &&
                (point - origin).Normalized().Dot(forward) > coneDot && HasLineOfSight(origin, point))
            .OrderBy(point => point.DistanceSquaredTo(origin)).FirstOrDefault();
        return target == Vector3.Zero ? origin + forward * distance : target;
    }

    // Arsenal target selection intentionally scans enemy candidates only, not
    // the wider crate/queen damage geometry used by shared contact queries.
    // Keep the authored .5f aim offset, root-distance ordering and occlusion.
    internal RuntimeWorldScene.DynamicObjectNode? FindArsenalTarget(Vector3 origin, float range, float cone = -.1f,
        IEnumerable<int>? excluded = null)
    {
        if (_disposed) return null;
        var skip = excluded?.ToHashSet();
        var forward = physics.Forward();
        return targets.ArsenalTargets().Where(node => IsAimTarget(node) && !targets.IsMorphed(node.Source.InstanceIndex) &&
                skip?.Contains(node.Source.InstanceIndex) != true)
            .Where(node =>
            {
                var delta = node.Root.GlobalPosition + Vector3.Up * .5f - origin;
                return delta.LengthSquared() < range * range && delta.LengthSquared() > .00001f &&
                    delta.Normalized().Dot(forward) > cone && HasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f);
            }).OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault();
    }

    internal (Vector3 Point, RuntimeWorldScene.DynamicObjectNode? Target)? Contact(Vector3 from, Vector3 to, float radius)
    {
        var player = physics.Player();
        if (_disposed || player is null || from.DistanceSquaredTo(to) < .000001f) return null;
        var ray = PhysicsRayQueryParameters3D.Create(from, to); ray.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        float closest = hit.TryGetValue("position", out var point) ? from.DistanceTo(point.AsVector3()) : float.PositiveInfinity;
        RuntimeWorldScene.DynamicObjectNode? target = null;
        Vector3 position = hit.Count > 0 ? point.AsVector3() : to;
        foreach (var (candidate, bounds) in Geometry)
        {
            if (!IsAimTarget(candidate) || !ActorContactGeometry.SegmentBoxFraction(from, to, bounds.Grow(radius), out float fraction)) continue;
            var contact = from.Lerp(to, fraction); float length = from.DistanceTo(contact);
            if (length > closest + .001f) continue;
            closest = length; target = candidate; position = contact;
        }
        return float.IsFinite(closest) ? (position, target) : null;
    }

    internal Vector3? ProbeBombWorldContact(Vector3 from, Vector3 to)
    {
        var player = physics.Player();
        if (_disposed || player is null) return null;
        var space = player.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _bombContactShape,
            Transform = new(Basis.Identity, from),
            Motion = to - from,
            Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() },
        };
        // CastMotion ignores an initial overlap. Preserve this explicit
        // start intersection to prevent spawning through a nearby wall.
        if (space.IntersectShape(query, 1).Count != 0) return from;
        var fractions = space.CastMotion(query);
        return fractions.Length >= 2 && fractions[0] < 1 ? from.Lerp(to, fractions[0]) : null;
    }

    internal bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        // Retired world contacts must never admit a hit via a stale callback.
        // Keep the existing no-player and degenerate-ray behaviour while live.
        if (_disposed) return false;
        var player = physics.Player();
        if (player is null || from.DistanceSquaredTo(to) < .0001f) return true;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = RayExclusions();
        return player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    internal global::Godot.Collections.Array<Rid> RayExclusions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var player = physics.Player() ?? throw new InvalidOperationException("World queries require a player.");
        // Damageable bodies are queried separately from walls; excluding them
        // prevents a crate from shielding itself and unrelated stacked crates.
        if (_occlusionFrame != Engine.GetPhysicsFrames() || _occlusionPlayer != player.GetRid())
        {
            _occlusionFrame = Engine.GetPhysicsFrames();
            _occlusionPlayer = player.GetRid();
            _occlusionExclusions = [player.GetRid()];
            foreach (var body in physics.DamageableBodies())
                if (GodotObject.IsInstanceValid(body)) _occlusionExclusions.Add(body.GetRid());
            physics.AddRayExclusions(_occlusionExclusions);
        }
        return _occlusionExclusions;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Invalidate();
        _bombContactShape.Dispose();
    }
}

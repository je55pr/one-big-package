using Godot;
using OBP.Core.Math;
using OBP.Godot;
using OBP.RAC1.Camera;

namespace OBP.Game;

public partial class PlayerHost
{
    /// <summary>Godot contact facts for the native camera's bounded probe sequence.</summary>
    private sealed class CameraGeometry(PlayerHost player) : IRac1CameraGeometry
    {
        private readonly SphereShape3D _sphere = new();
        private readonly PhysicsShapeQueryParameters3D _shapeQuery = new();
        private readonly PhysicsRayQueryParameters3D _rayQuery = new() { HitFromInside = true };
        private readonly global::Godot.Collections.Array<Rid> _exclude = [];

        public void BeginFrame()
        {
            _exclude.Clear();
            _exclude.Add(player.GetRid());
        }

        public Rac1CameraContact? Sphere(Vec3 centerNativeZUp, double radius)
        {
            Vector3 center = ToScene(centerNativeZUp);
            _sphere.Radius = (float)radius;
            _shapeQuery.Shape = _sphere;
            _shapeQuery.Margin = 0f;
            _shapeQuery.Transform = new(Basis.Identity, center);
            for (int i = 0; i < 64; i++)
            {
                _shapeQuery.Exclude = _exclude;
                var hit = player.GetWorld3D().DirectSpaceState.GetRestInfo(_shapeQuery);
                if (hit.Count == 0) return null;
                if (SkipBody(hit)) continue;
                Vector3 surface = (Vector3)hit["point"];
                Vector3 normal = ((Vector3)hit["normal"]).Normalized();
                return new(ScenePlayerToNative(surface), ScenePlayerToNative(surface + normal * (float)radius));
            }
            return null;
        }

        public Vec3? Segment(Vec3 fromNativeZUp, Vec3 toNativeZUp)
        {
            _rayQuery.From = ToScene(fromNativeZUp);
            _rayQuery.To = ToScene(toNativeZUp);
            for (int i = 0; i < 64; i++)
            {
                _rayQuery.Exclude = _exclude;
                var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(_rayQuery);
                if (hit.Count == 0) return null;
                if (SkipBody(hit)) continue;
                return ScenePlayerToNative((Vector3)hit["position"]);
            }
            return null;
        }

        // 0x002e7d20 temporarily masks ordinary moby collision while probing.
        // The admitted solid/push classes remain; no collision body is mutated.
        private bool SkipBody(global::Godot.Collections.Dictionary hit)
        {
            Node? body = hit.TryGetValue("collider", out var collider) ? collider.AsGodotObject() as Node :
                hit.TryGetValue("collider_id", out var id) ? GodotObject.InstanceFromId(id.AsUInt64()) as Node : null;
            int? cls = RuntimeWorldScene.FindDynamicObjectOwner(body)?.NativeClassId;
            bool skip = body is CharacterBody3D || cls is { } value &&
                value is not (0x72 or 0xb or 0x2c or 0x9a or 0xff or 0x392 or 0x452 or 0x353);
            if (skip && hit.TryGetValue("rid", out var rid)) _exclude.Add(rid.AsRid());
            return skip;
        }

        private static Vector3 ToScene(Vec3 native) => new((float)-native.X, (float)native.Z, (float)native.Y);
    }
}

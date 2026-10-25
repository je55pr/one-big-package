using Godot;
using OBP.Godot;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost : CharacterBody3D
{
    /// <summary>
    /// Native source queries run in Z-up space. The scene uses (-X,Z,Y).
    /// Godot supplies triangle contacts; RAC1 owns their admission and target.
    /// </summary>
    private Rac1LedgeCandidate? ProbeRac1Ledge(
        double solidFloorHeight,
        bool predictMotion = true,
        Rac1NativeVector3? positionOverride = null)
    {
        var sourcePosition = ScenePlayerToNative(GlobalPosition);
        var position = positionOverride ?? new Rac1NativeVector3(
            sourcePosition.X, sourcePosition.Y, sourcePosition.Z);
        // Godot's previous resolved displacement is an adapter for native
        // P+0x110. External-transport equivalence has not been established.
        const double frequency = Rac1RatchetMovementController.UpdateHz;
        var previousMotion = new Rac1NativeVector3(
            -Velocity.X / frequency, Velocity.Z / frequency, Velocity.Y / frequency);
        var input = new Rac1LedgeProbeInput(
            position,
            predictMotion ? previousMotion : Rac1NativeVector3.Zero,
            _rac1Yaw.CurrentYaw,
            solidFloorHeight,
            _rac1Movement.CurrentAirGravityPerUpdate,
            predictMotion ? 2 : 0);
        return Rac1LedgeProbe.TryFindCandidate(
            input,
            CastRac1TraversalRay,
            OverlapsRac1TraversalBody,
            out var candidate)
            ? candidate
            : null;
    }

    private Rac1TraversalRayHit? CastRac1TraversalRay(Rac1TraversalRay ray)
    {
        var query = PhysicsRayQueryParameters3D.Create(
            LedgeNativeToScene(ray.From), LedgeNativeToScene(ray.To));
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || !hit.ContainsKey("position") || !hit.ContainsKey("normal"))
            return null;

        var position = (Vector3)hit["position"];
        var normal = (Vector3)hit["normal"];
        var nativePosition = new Rac1NativeVector3(-position.X, position.Z, position.Y);
        var nativeNormal = new Rac1NativeVector3(-normal.X, normal.Z, normal.Y);
        if (hit.ContainsKey("collider") &&
            hit["collider"].As<Node>() is RuntimeWorldScene.RuntimeCollisionBody3D collisionBody &&
            hit.ContainsKey("face_index") &&
            collisionBody.MaterialIdForFace((int)hit["face_index"], OBP.RAC1.Rac1Authority.Primary) is int material &&
            material is >= byte.MinValue and <= byte.MaxValue)
        {
            return Rac1TraversalRayHit.StaticWorld(nativePosition, nativeNormal, material);
        }
        // Native masks2/4 have different world-query membership. Godot's
        // current static mesh shares one collision layer, so both use its ray
        // executor. Unidentified/dynamic tops fail closed above this boundary.
        return new Rac1TraversalRayHit(nativePosition, nativeNormal, -1, TraversalAllowed: false);
    }

    private bool OverlapsRac1TraversalBody(Rac1TraversalBodyQuery body)
    {
        // An enclosing capsule is an OBP clearance approximation for native
        // 0x1f19a0, whose R5900 radial/axial solver is not duplicated by Godot.
        // This only annotates the candidate's climb clearance; it cannot admit
        // an otherwise rejected top, wall, or ceiling contact.
        using var shape = new CapsuleShape3D
        {
            Radius = (float)body.HorizontalRadius,
            Height = (float)(body.AxialSpan + body.HorizontalRadius * 2d),
        };
        var origin = body.Origin with { Z = body.Origin.Z + body.AxialSpan * 0.5d };
        using var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity, LedgeNativeToScene(origin)),
            Exclude = new global::Godot.Collections.Array<Rid> { GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count != 0;
    }

    private static Vector3 LedgeNativeToScene(Rac1NativeVector3 native) =>
        new((float)-native.X, (float)native.Z, (float)native.Y);
}

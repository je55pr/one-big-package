using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>
/// Adapter: a neutral <see cref="RuntimeWorld"/> (pure data, no engine or
/// game-format types) → a Godot scene sub-tree. One <see cref="ArrayMesh"/> /
/// <see cref="MeshInstance3D"/> per welded per-material render mesh, decoded RGBA
/// → <see cref="ImageTexture"/> / <see cref="StandardMaterial3D"/>, and a
/// <see cref="StaticBody3D"/> + trimesh <see cref="CollisionShape3D"/> per decoded
/// collision blob. This is the only place Godot and the runtime world model meet,
/// and it is game-independent — the same builder serves the GC importer today and
/// the R&amp;C1 / Up Your Arsenal importers later.
///
/// <para>
/// <c>RuntimeWorld</c> coordinates are "OBP space": native Z-up <c>(x, y, z)</c>
/// mapped to <c>(x, z, y)</c> by the importer. That swap is an <em>odd</em>
/// permutation, i.e. a reflection — the level is a mirror image of the real game.
/// <see cref="ToScene"/> corrects the handedness (negates X) for everything the
/// adapter emits: geometry, collision, camera and the player spawn.
/// </para>
/// </summary>
public static partial class RuntimeWorldScene
{
    /// <summary>OBP space → Godot: un-mirror by negating X (see the type remarks).</summary>
    public static Vector3 ToScene(double x, double y, double z) => new((float)(-x), (float)y, (float)z);

    /// <summary>OBP-space yaw (radians about +Y) → Godot yaw after the X flip.</summary>
    public static float ToSceneYaw(double obpYaw) => (float)(-obpYaw);

    /// <summary>OBP-space rotation axis → Godot after the X reflection (axial-vector transform).</summary>
    public static Vector3 ToSceneRotationAxis(double x, double y, double z) => new((float)x, (float)-y, (float)-z);

    /// <summary>Stable node name used to keep independently animated presentation groups separate.</summary>
    public static string PresentationGroupNodeName(string group) => $"PresentationGroup_{group}";

    public sealed record Result(
        Node3D Root,
        Node3D? SkyRoot,
        int MeshInstances,
        int Triangles,
        int Textures,
        int CollisionBodies,
        int CollisionTriangles,
        int TieInstances,
        int ShrubInstances,
        int MobyInstances,
        int AnimatedMobies = 0,
        System.Collections.Generic.IReadOnlyList<AnimatedMesh>? AnimatedMeshes = null,
        int DynamicObjects = 0,
        System.Collections.Generic.IReadOnlyList<DynamicObjectNode>? DynamicObjectNodes = null);

    /// <summary>
    /// Scene root for one preserved dynamic gameplay object. This carries source
    /// identity only; it deliberately adds no collision shape or physics policy.
    /// Class/game-specific hosts may parent recovered colliders beneath it and
    /// later recover the owning native object by walking ancestors.
    /// </summary>
    public sealed record Options
    {
        /// <summary>Render the sky shells (grouped under <see cref="Result.SkyRoot"/> so the host can pin them to the camera).</summary>
        public bool IncludeSky { get; init; } = true;

        /// <summary>Render the death-height plane (a large debug quad well below the level).</summary>
        public bool IncludeDeathPlane { get; init; }

        /// <summary>Render the moby instance markers (cubes for triggers / spawners with no decoded mesh).</summary>
        public bool IncludeMobyMarkers { get; init; } = true;

        /// <summary>Build a <see cref="StaticBody3D"/> + trimesh collider per decoded blob — the surface the debug capsule stands on.</summary>
        public bool IncludeCollision { get; init; } = true;

        /// <summary>Also add a translucent debug overlay mesh over each collision body.</summary>
        public bool ShowCollisionDebug { get; init; }

        /// <summary>Build only the animated mobies (skip static geometry, collision, sky) — the MobySequence showcase.</summary>
        public bool OnlyAnimatedMobies { get; init; }
    }

    /// <summary>Assemble one game-neutral runtime world in a fixed presentation order.</summary>
    public static Result Build(RuntimeWorld world, string name = "World", Options? options = null)
    {
        options ??= new Options();
        var root = new Node3D { Name = name };
        var skyRoot = new Node3D { Name = "Sky" };
        var mats = new WorldMaterialFactory(world);

        var (staticMeshInstances, staticTriangles, skyMeshes) =
            BuildStaticMeshes(world, root, skyRoot, options, mats);
        var (dynamicObjectCount, dynamicMeshInstances, dynamicTriangles, dynamicNodes) =
            BuildDynamicObjects(world, root, options, mats);
        var (animatedMeshInstances, animatedTriangles, animatedPlayers) =
            BuildAnimatedMeshes(world, root, mats);

        if (mats.UntexturedTriangleReport.Count > 0)
        {
            string report = string.Join(", ", mats.UntexturedTriangleReport.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"));
            GD.Print($"[RuntimeWorldScene] untextured triangles (fallback tint): {report}");
        }

        var (collisionBodies, collisionTriangles) = BuildCollisionBodies(world, root, options);

        int Kind(string kind) => world.Meshes.Count(m => m.AssetKind == kind);

        return new Result(
            root,
            skyMeshes > 0 ? skyRoot : null,
            staticMeshInstances + dynamicMeshInstances,
            staticTriangles + dynamicTriangles + animatedTriangles,
            mats.TextureCount,
            collisionBodies,
            collisionTriangles,
            Kind("tie"),
            Kind("shrub"),
            Kind("moby"),
            animatedMeshInstances,
            animatedPlayers,
            dynamicObjectCount,
            dynamicNodes);
    }


    /// <summary>Convert an OBP-space object matrix to Godot's X-unmirrored scene transform.</summary>
    public static Transform3D ToSceneTransform(RuntimeObjectTransform transform)
    {
        var m = transform.Matrix;
        if (m.Length != 16)
        {
            return Transform3D.Identity;
        }

        // Scene conversion is F*M*F where F = diag(-1, 1, 1, 1): local
        // vertices are X-flipped by ToScene(), so the basis must be conjugated
        // by the same reflection and the world-space translation X-flipped.
        var x = new Vector3((float)m[0], (float)-m[1], (float)-m[2]);
        var y = new Vector3((float)-m[4], (float)m[5], (float)m[6]);
        var z = new Vector3((float)-m[8], (float)m[9], (float)m[10]);
        var origin = new Vector3((float)-m[12], (float)m[13], (float)m[14]);
        return new Transform3D(new Basis(x, y, z), origin);
    }

    /// <summary>
    /// Convert a Godot scene transform back to the neutral OBP-space affine matrix.
    /// This is the inverse of <see cref="ToSceneTransform"/> for runtime transforms.
    /// </summary>
    public static RuntimeObjectTransform ToRuntimeTransform(Transform3D transform)
    {
        Vector3 x = transform.Basis.X;
        Vector3 y = transform.Basis.Y;
        Vector3 z = transform.Basis.Z;
        Vector3 origin = transform.Origin;

        return new RuntimeObjectTransform(
        [
            x.X, -x.Y, -x.Z, 0d,
            -y.X, y.Y, y.Z, 0d,
            -z.X, z.Y, z.Z, 0d,
            -origin.X, origin.Y, origin.Z, 1d,
        ]);
    }

    /// <summary>Park a camera outside the world bounds, looking at their centre.</summary>
    public static void FrameCamera(Camera3D camera, ObpBounds bounds, float azimuthDegrees = 35f, float elevationDegrees = 28f)
    {
        var a = ToScene(bounds.Min.X, bounds.Min.Y, bounds.Min.Z);
        var b = ToScene(bounds.Max.X, bounds.Max.Y, bounds.Max.Z);
        var min = new Vector3(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
        var max = new Vector3(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));
        var centre = (min + max) * 0.5f;
        float radius = Mathf.Max(1f, (max - min).Length() * 0.5f);

        float az = Mathf.DegToRad(azimuthDegrees);
        float el = Mathf.DegToRad(elevationDegrees);
        var dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));

        camera.Position = centre + dir * (radius * 1.0f);
        camera.LookAt(centre, Vector3.Up);
        camera.Far = radius * 12f;
        camera.Near = Mathf.Max(0.05f, radius * 0.01f);
    }
}

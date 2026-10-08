using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

public static partial class RuntimeWorldScene
{
    // Collision identities, faces, and optional collision visualization.
    /// <summary>
    /// Static collision body that preserves its source collision blob so a host
    /// contact query can recover the native triangle/material identity. The scene
    /// emits each source triangle twice with opposite winding, therefore Godot
    /// face n maps deterministically to source triangle n / 2.
    /// </summary>
    public sealed class RuntimeCollisionBody3D : StaticBody3D
    {
        public RuntimeCollisionBlob? SourceCollision { get; private set; }
        public int CollisionBlobIndex { get; private set; } = -1;

        public void Configure(RuntimeCollisionBlob sourceCollision, int collisionBlobIndex)
        {
            SourceCollision = sourceCollision ?? throw new ArgumentNullException(nameof(sourceCollision));
            CollisionBlobIndex = collisionBlobIndex;
        }

        public int? SourceTriangleIndexForFace(int godotFaceIndex) =>
            SourceCollision is null
                ? null
                : SourceTriangleIndexForDoubledFace(
                    godotFaceIndex,
                    SourceCollision.Triangles);

        public int? MaterialIdForFace(int godotFaceIndex) =>
            SourceCollision is null
                ? null
                : MaterialIdForDoubledFace(SourceCollision, godotFaceIndex);
    }

    public static int? SourceTriangleIndexForDoubledFace(
        int godotFaceIndex,
        int sourceTriangleCount)
    {
        if (godotFaceIndex < 0 || sourceTriangleCount < 0)
            return null;

        int triangleIndex = godotFaceIndex / 2;
        return triangleIndex < sourceTriangleCount
            ? triangleIndex
            : null;
    }

    public static int? MaterialIdForDoubledFace(
        RuntimeCollisionBlob sourceCollision,
        int godotFaceIndex)
    {
        ArgumentNullException.ThrowIfNull(sourceCollision);
        int? triangleIndex = SourceTriangleIndexForDoubledFace(
            godotFaceIndex,
            sourceCollision.Triangles);
        if (!triangleIndex.HasValue)
            return null;

        return triangleIndex.Value < sourceCollision.TriangleMaterialIds.Length
            ? sourceCollision.TriangleMaterialIds[triangleIndex.Value]
            : null;
    }


    private static (int Bodies, int Triangles) BuildCollisionBodies(
        RuntimeWorld world, Node3D root, Options options)
    {
        int collisionBodies = 0;
        int collisionTriangles = 0;
        if (options.IncludeCollision && !options.OnlyAnimatedMobies)
        {
            for (int i = 0; i < world.CollisionMeshes.Count; i++)
            {
                var blob = world.CollisionMeshes[i];
                if (blob.Indices.Length < 3 || blob.Positions.Length < 9)
                {
                    continue;
                }

                // ConcavePolygonShape3D wants a flat triangle soup: 3 vertices per
                // face, un-indexed. The RC octree triangulation has inconsistent
                // winding, which makes a CharacterBody3D fall through faces whose
                // normal points away — so emit every triangle twice, both windings.
                int triCount = blob.Indices.Length / 3;
                var faces = new Vector3[triCount * 6];
                for (int t = 0; t < triCount; t++)
                {
                    var a = Vertex(blob, blob.Indices[t * 3]);
                    var b = Vertex(blob, blob.Indices[t * 3 + 1]);
                    var c = Vertex(blob, blob.Indices[t * 3 + 2]);
                    faces[t * 6] = a;
                    faces[t * 6 + 1] = b;
                    faces[t * 6 + 2] = c;
                    faces[t * 6 + 3] = c;
                    faces[t * 6 + 4] = b;
                    faces[t * 6 + 5] = a;
                }

                var body = new RuntimeCollisionBody3D { Name = $"Collision{i}" };
                body.Configure(blob, i);
                body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces } });
                root.AddChild(body);
                collisionBodies++;
                collisionTriangles += blob.Triangles;

                if (options.ShowCollisionDebug)
                {
                    body.AddChild(CollisionDebugMesh(blob, i));
                }
            }
        }


        return (collisionBodies, collisionTriangles);
    }

    private static Vector3 Vertex(RuntimeCollisionBlob blob, int index)
    {
        int v = index * 3;
        return ToScene(blob.Positions[v], blob.Positions[v + 1], blob.Positions[v + 2]);
    }

    private static MeshInstance3D CollisionDebugMesh(RuntimeCollisionBlob blob, int index)
    {
        var verts = new Vector3[blob.VertexCount];
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = ToScene(blob.Positions[i * 3], blob.Positions[i * 3 + 1], blob.Positions[i * 3 + 2]);
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Index] = blob.Indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var tint = index == 0 ? new Color(0.20f, 0.85f, 1.00f) : new Color(1.00f, 0.55f, 0.20f);
        return new MeshInstance3D
        {
            Name = $"CollisionDebug{index}",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(tint.R, tint.G, tint.B, 0.35f),
                RenderPriority = 1,
            },
        };
    }
}

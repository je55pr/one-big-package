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
    /// contact query can recover the native triangle/material identity through
    /// an explicit face map after redundant octree references are consolidated.
    /// </summary>
    public sealed class RuntimeCollisionBody3D : StaticBody3D
    {
        public RuntimeCollisionBlob? SourceCollision { get; private set; }
        public int CollisionBlobIndex { get; private set; } = -1;
        private int[] _sourceTriangles = [];

        public void Configure(RuntimeCollisionBlob sourceCollision, int collisionBlobIndex, int[] sourceTriangles)
        {
            SourceCollision = sourceCollision ?? throw new ArgumentNullException(nameof(sourceCollision));
            CollisionBlobIndex = collisionBlobIndex;
            _sourceTriangles = sourceTriangles ?? throw new ArgumentNullException(nameof(sourceTriangles));
        }

        public int? SourceTriangleIndexForFace(int godotFaceIndex) =>
            SourceCollision is null
                ? null
                : RuntimeWorldScene.SourceTriangleIndexForFace(_sourceTriangles, godotFaceIndex);

        public int? MaterialIdForFace(int godotFaceIndex) =>
            SourceCollision is null
                ? null
                : RuntimeWorldScene.MaterialIdForFace(SourceCollision, _sourceTriangles, godotFaceIndex);
    }

    public static int? SourceTriangleIndexForFace(int[] sourceTriangles, int godotFaceIndex)
    {
        ArgumentNullException.ThrowIfNull(sourceTriangles);
        return godotFaceIndex >= 0 && godotFaceIndex < sourceTriangles.Length
            ? sourceTriangles[godotFaceIndex] : null;
    }

    public static int? MaterialIdForFace(RuntimeCollisionBlob sourceCollision, int[] sourceTriangles, int godotFaceIndex)
    {
        ArgumentNullException.ThrowIfNull(sourceCollision);
        int? triangleIndex = SourceTriangleIndexForFace(sourceTriangles, godotFaceIndex);
        if (!triangleIndex.HasValue)
            return null;

        return triangleIndex.Value >= 0 && triangleIndex.Value < sourceCollision.TriangleMaterialIds.Length
            ? sourceCollision.TriangleMaterialIds[triangleIndex.Value]
            : null;
    }

    public sealed record CollisionFacePlan(Vector3[] Faces, int[] SourceTriangleIndices);

    private readonly record struct CollisionFaceKey(Vector3 A, Vector3 B, Vector3 C, int? Material);

    /// <summary>
    /// A host shape needs each geometric surface once, rather than the native
    /// octree's repeated references. Deduplicate only exact scene-space geometry
    /// with the same material, preserving the first source identity. Back-face
    /// collision supplies two-sided contact without overlapping reverse copies.
    /// The source collision blob is retained verbatim.
    /// </summary>
    public static CollisionFacePlan BuildCollisionFacePlan(RuntimeCollisionBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var seen = new System.Collections.Generic.HashSet<CollisionFaceKey>();
        var faces = new System.Collections.Generic.List<Vector3>();
        var sources = new System.Collections.Generic.List<int>();
        for (int triangle = 0; triangle < blob.Triangles; triangle++)
        {
            Vector3 a = Vertex(blob, blob.Indices[triangle * 3]);
            Vector3 b = Vertex(blob, blob.Indices[triangle * 3 + 1]);
            Vector3 c = Vertex(blob, blob.Indices[triangle * 3 + 2]);
            Vector3 x = a, y = b, z = c;
            static int Compare(Vector3 left, Vector3 right)
            {
                int comparison = left.X.CompareTo(right.X);
                if (comparison == 0) comparison = left.Y.CompareTo(right.Y);
                return comparison == 0 ? left.Z.CompareTo(right.Z) : comparison;
            }
            if (Compare(x, y) > 0) (x, y) = (y, x);
            if (Compare(y, z) > 0) (y, z) = (z, y);
            if (Compare(x, y) > 0) (x, y) = (y, x);
            int? material = triangle < blob.TriangleMaterialIds.Length ? blob.TriangleMaterialIds[triangle] : null;
            if (!seen.Add(new(x, y, z, material))) continue;
            faces.Add(a); faces.Add(b); faces.Add(c);
            sources.Add(triangle);
        }
        return new(faces.ToArray(), sources.ToArray());
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

                CollisionFacePlan plan = BuildCollisionFacePlan(blob);

                var body = new RuntimeCollisionBody3D { Name = $"Collision{i}" };
                body.Configure(blob, i, plan.SourceTriangleIndices);
                body.AddChild(new CollisionShape3D
                {
                    Shape = new ConcavePolygonShape3D { Data = plan.Faces, BackfaceCollision = true },
                });
                root.AddChild(body);
                collisionBodies++;
                collisionTriangles += plan.SourceTriangleIndices.Length;
                GD.Print($"[collision] blob {i}: {blob.Triangles:N0} source references -> " +
                    $"{plan.SourceTriangleIndices.Length:N0} two-sided shape faces");

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

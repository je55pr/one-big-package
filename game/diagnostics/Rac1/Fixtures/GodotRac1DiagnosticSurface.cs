using Godot;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

/// <summary>Explicit authored diagnostic geometry; preserves the existing source face/material fixture.</summary>
internal static class GodotRac1DiagnosticSurface
{
    public static void Add(Node3D parent, string name, int rawFaceType, params (Vector3 A, Vector3 B, Vector3 C)[] triangles)
    {
        var positions = new double[triangles.Length * 9];
        var indices = new int[triangles.Length * 3];
        var faceTypes = Enumerable.Repeat(rawFaceType, triangles.Length).ToArray();
        for (int triangle = 0; triangle < triangles.Length; triangle++)
        {
            var (a, b, c) = triangles[triangle];
            Vector3[] vertices = [a, b, c];
            for (int vertex = 0; vertex < 3; vertex++)
            {
                Vector3 value = vertices[vertex];
                int offset = triangle * 9 + vertex * 3;
                positions[offset] = -value.X;
                positions[offset + 1] = value.Y;
                positions[offset + 2] = value.Z;
                indices[triangle * 3 + vertex] = triangle * 3 + vertex;
            }
        }
        var body = new RuntimeWorldScene.RuntimeCollisionBody3D { Name = name };
        var blob = Rac1DiagnosticCollision.Create(positions, indices, faceTypes);
        var plan = RuntimeWorldScene.BuildCollisionFacePlan(blob);
        body.Configure(blob, -1, plan.SourceTriangleIndices);
        body.AddChild(new CollisionShape3D
        {
            Shape = new ConcavePolygonShape3D { Data = plan.Faces, BackfaceCollision = true },
        });
        parent.AddChild(body);
    }
}

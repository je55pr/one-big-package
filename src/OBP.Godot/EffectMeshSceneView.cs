using Godot;
using OBP.Runtime;

namespace OBP.Godot;

/// <summary>Shared neutral triangle presentation for source quads, ribbons and specialised beam/mesh layers.</summary>
public sealed class EffectMeshSceneView(Node3D parent) : IDisposable
{
    private readonly List<MeshInstance3D> _surfaces = [];
    public int SurfaceCount { get; private set; }
    public void Sample(IReadOnlyList<RuntimeObjectMesh> surfaces, Func<RuntimeObjectMesh, Material> material)
        => Sample(surfaces, (_, surface) => material(surface));
    public void Sample(IReadOnlyList<RuntimeObjectMesh> surfaces, Func<int, RuntimeObjectMesh, Material> material)
    {
        while (_surfaces.Count < surfaces.Count)
        {
            var node = new MeshInstance3D { Mesh = new ArrayMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            parent.AddChild(node); _surfaces.Add(node);
        }
        SurfaceCount = surfaces.Count;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            var node = _surfaces[i]; node.Visible = i < surfaces.Count;
            if (!node.Visible) { node.MaterialOverride = null; ((ArrayMesh)node.Mesh).ClearSurfaces(); continue; }
            var source = surfaces[i]; Upload((ArrayMesh)node.Mesh, source);
            node.MaterialOverride = material(i, source);
        }
    }
    /// <summary>Also serves retained source ribbon callers that already own their scene-node lifetime.</summary>
    public static void Upload(ArrayMesh mesh, RuntimeObjectMesh source)
    {
        int count = source.Positions.Length / 3;
        var vertices = new Vector3[count]; var uv = new Vector2[count]; var colors = new Color[count];
        for (int v = 0; v < count; v++)
        {
            vertices[v] = RuntimeWorldScene.ToScene(source.Positions[v * 3], source.Positions[v * 3 + 1], source.Positions[v * 3 + 2]);
            uv[v] = new(source.Uvs[v * 2], source.Uvs[v * 2 + 1]);
            colors[v] = source.Colors is { } c ? new(c[v * 4], c[v * 4 + 1], c[v * 4 + 2], c[v * 4 + 3]) : Colors.White;
        }
        using var arrays = new global::Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.Index] = source.Indices;
        mesh.ClearSurfaces(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
    public void Clear()
    {
        foreach (var node in _surfaces)
        {
            var mesh = node.Mesh; node.Visible = false; node.QueueFree(); mesh.Dispose();
        }
        _surfaces.Clear(); SurfaceCount = 0;
    }
    public void Dispose() => Clear();
}

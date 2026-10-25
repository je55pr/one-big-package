using Godot;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Owned, reusable meshes for original fusion assets and the source beam.</summary>
public sealed partial class FusionMeshView : Node3D
{
    private readonly List<MeshInstance3D> _surfaces = [];
    public void Sample(IReadOnlyList<RuntimeObjectMesh> surfaces, FusionWeaponAssets assets)
    {
        while (_surfaces.Count < surfaces.Count)
        {
            var node = new MeshInstance3D { Mesh = new ArrayMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node); _surfaces.Add(node);
        }
        for (int i = 0; i < _surfaces.Count; i++)
        {
            var node = _surfaces[i]; node.Visible = i < surfaces.Count;
            if (!node.Visible) continue;
            var source = surfaces[i];
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
            var mesh = (ArrayMesh)node.Mesh; mesh.ClearSurfaces(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            node.MaterialOverride = assets.MaterialFor(source);
        }
    }

    public override void _ExitTree()
    {
        foreach (var node in _surfaces) node.Mesh.Dispose();
        _surfaces.Clear();
    }
}

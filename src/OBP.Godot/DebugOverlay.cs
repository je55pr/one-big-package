using Godot;
using OBP.Runtime;

namespace OBP.Godot;

/// <summary>
/// Runtime inspection layers over a built <see cref="RuntimeWorldScene.Result"/>:
/// per-asset-kind isolate / tint, collision wireframe, world-bounds box and
/// lighting gizmos. Every layer is idempotent and fully reversible — kind tint
/// and visibility go through <see cref="MeshInstance3D.MaterialOverlay"/> and the
/// <c>Visible</c> flag (never the source material), and the generated meshes live
/// under one <c>DebugOverlay</c> node that is freed with the world sub-tree.
///
/// <para>Replaces the old <c>OBP_KIND_DEBUG</c> env var and the
/// <c>--collision-debug</c>-only translucent fill.</para>
/// </summary>
public sealed class DebugOverlay
{
    [System.Flags]
    public enum Layer
    {
        None = 0,
        KindTint = 1 << 0,
        CollisionWire = 1 << 1,
        WorldBounds = 1 << 2,
        EnvGizmos = 1 << 3,
        HideSky = 1 << 4,
    }

    /// <summary>Cycle order for <see cref="IsolateNextKind"/> — null means "show every kind".</summary>
    public static readonly string?[] IsolationCycle =
        { null, "tfrag", "tie", "shrub", "moby", "moby-marker", "sky" };

    private static readonly System.Collections.Generic.Dictionary<string, Color> KindColour = new()
    {
        ["tfrag"] = new Color(0.20f, 1.00f, 0.30f),
        ["tie"] = new Color(1.00f, 0.30f, 0.25f),
        ["shrub"] = new Color(1.00f, 0.95f, 0.20f),
        ["moby"] = new Color(0.30f, 0.55f, 1.00f),
        ["moby-marker"] = new Color(1.00f, 0.35f, 0.75f),
        ["sky"] = new Color(0.55f, 0.55f, 0.60f),
    };

    private readonly RuntimeWorldScene.Result _result;
    private readonly RuntimeWorld _world;
    private readonly Node3D _root;
    private Node3D? _gizmos;

    private Layer _active;
    private int _isolationCursor;

    public DebugOverlay(RuntimeWorldScene.Result result, RuntimeWorld world)
    {
        _result = result;
        _world = world;
        _root = result.Root;
    }

    public Layer Active => _active;

    public string? IsolatedKind => IsolationCycle[_isolationCursor];

    public bool IsOn(Layer layer) => (_active & layer) != 0;

    public void Toggle(Layer layer) => Set(layer, !IsOn(layer));

    public void Set(Layer layer, bool on)
    {
        _active = on ? _active | layer : _active & ~layer;
        Apply();
    }

    /// <summary>Step to the next kind in <see cref="IsolationCycle"/> (wraps back to "all").</summary>
    public void IsolateNextKind()
    {
        _isolationCursor = (_isolationCursor + 1) % IsolationCycle.Length;
        Apply();
    }

    public void Clear()
    {
        _active = Layer.None;
        _isolationCursor = 0;
        Apply();
    }

    /// <summary>A compact one-line summary of what is on, for a HUD.</summary>
    public string StatusLine()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (IsolatedKind is { } k)
        {
            parts.Add($"isolate:{k}");
        }

        foreach (Layer l in System.Enum.GetValues<Layer>())
        {
            if (l != Layer.None && IsOn(l))
            {
                parts.Add(l.ToString());
            }
        }

        return parts.Count == 0 ? "overlays: off" : "overlays: " + string.Join(" ", parts);
    }

    private Node3D Gizmos()
    {
        if (_gizmos is null || !GodotObject.IsInstanceValid(_gizmos))
        {
            _gizmos = new Node3D { Name = "DebugOverlay" };
            _root.AddChild(_gizmos);
        }

        return _gizmos;
    }

    private void ClearGizmos()
    {
        if (_gizmos is { } g && GodotObject.IsInstanceValid(g))
        {
            g.QueueFree();
        }

        _gizmos = null;
    }

    private void Apply()
    {
        string? isolate = IsolatedKind;
        bool tint = IsOn(Layer.KindTint);
        bool hideSky = IsOn(Layer.HideSky);

        foreach (var mi in Walk(_root))
        {
            if (_gizmos is not null && (mi.GetParent()?.Name == _gizmos.Name))
            {
                continue; // never restyle our own gizmo meshes
            }

            string kind = KindOf(mi);
            bool visible = isolate is null || kind == isolate;
            if (hideSky && kind == "sky")
            {
                visible = false;
            }

            mi.Visible = visible;
            ApplyTint(mi, tint ? KindColour.GetValueOrDefault(kind, new Color(1f, 0f, 1f)) : null);
        }

        if (_result.SkyRoot is { } sky && GodotObject.IsInstanceValid(sky))
        {
            sky.Visible = !hideSky && (isolate is null || isolate == "sky");
        }

        // Collision bodies: keep physics, just toggle the fill/wire mesh.
        foreach (var child in _root.GetChildren())
        {
            if (child is StaticBody3D body)
            {
                body.Visible = true; // physics stays; visuals handled below
            }
        }

        ClearGizmos();
        if (IsOn(Layer.CollisionWire))
        {
            BuildCollisionWire();
        }

        if (IsOn(Layer.WorldBounds))
        {
            BuildBoundsBox();
        }

        if (IsOn(Layer.EnvGizmos))
        {
            BuildEnvGizmos();
        }
    }

    // --- collision wireframe ------------------------------------------------

    private void BuildCollisionWire()
    {
        var parent = Gizmos();
        for (int i = 0; i < _world.CollisionMeshes.Count; i++)
        {
            var blob = _world.CollisionMeshes[i];
            if (blob.Indices.Length < 3)
            {
                continue;
            }

            int tris = blob.Indices.Length / 3;
            var verts = new Vector3[tris * 6];
            var cols = new Color[tris * 6];
            for (int t = 0; t < tris; t++)
            {
                var a = V(blob, blob.Indices[t * 3]);
                var b = V(blob, blob.Indices[t * 3 + 1]);
                var c = V(blob, blob.Indices[t * 3 + 2]);
                int matId = t < blob.TriangleMaterialIds.Length ? blob.TriangleMaterialIds[t] : 0;
                var col = MaterialColour(matId);
                verts[t * 6] = a; verts[t * 6 + 1] = b;
                verts[t * 6 + 2] = b; verts[t * 6 + 3] = c;
                verts[t * 6 + 4] = c; verts[t * 6 + 5] = a;
                for (int k = 0; k < 6; k++)
                {
                    cols[t * 6 + k] = col;
                }
            }

            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Color] = cols;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

            parent.AddChild(new MeshInstance3D
            {
                Name = $"CollisionWire{i}",
                Mesh = mesh,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    DisableFog = true,
                    RenderPriority = 2,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    // --- world bounds ----------------------------------------------------

    private void BuildBoundsBox()
    {
        var b = _world.Bounds;
        var lo = RuntimeWorldScene.ToScene(b.Min.X, b.Min.Y, b.Min.Z);
        var hi = RuntimeWorldScene.ToScene(b.Max.X, b.Max.Y, b.Max.Z);
        var min = new Vector3(Mathf.Min(lo.X, hi.X), Mathf.Min(lo.Y, hi.Y), Mathf.Min(lo.Z, hi.Z));
        var max = new Vector3(Mathf.Max(lo.X, hi.X), Mathf.Max(lo.Y, hi.Y), Mathf.Max(lo.Z, hi.Z));
        Gizmos().AddChild(WireBox(min, max, new Color(1f, 0.8f, 0.2f), "WorldBounds"));
    }

    // --- lighting gizmos -----------------------------------------------

    private void BuildEnvGizmos()
    {
        if (_world.Lighting is not { } lighting)
        {
            GD.Print($"[DebugOverlay] {_world.Game} {_world.DisplayName}: no decoded lighting — env gizmos unavailable");
            return;
        }

        var parent = Gizmos();

        foreach (var s in lighting.EnvSamples)
        {
            var p = RuntimeWorldScene.ToScene(s.Position.X, s.Position.Y, s.Position.Z);
            parent.AddChild(new MeshInstance3D
            {
                Name = "EnvSample",
                Position = p,
                Mesh = new SphereMesh { Radius = 2.5f, Height = 5f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = Unlit(new Color((float)s.HeroColour.R, (float)s.HeroColour.G, (float)s.HeroColour.B)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        for (int i = 0; i < lighting.EnvTransitions.Count; i++)
        {
            var (cx, cy, cz, r) = lighting.EnvTransitions[i].BoundingSphere;
            var c = RuntimeWorldScene.ToScene(cx, cy, cz);
            parent.AddChild(WireBox(c - (Vector3.One * (float)r), c + (Vector3.One * (float)r),
                new Color(0.4f, 0.9f, 1f), $"EnvTransition{i}"));
        }

        for (int i = 0; i < lighting.DirLights.Count; i++)
        {
            var d = lighting.DirLights[i].DirectionA;
            var dir = new Vector3(-(float)d.X, (float)d.Y, (float)d.Z); // OBP travel -> Godot (mirror X)
            if (dir.LengthSquared() < 1e-4f)
            {
                continue;
            }

            var centre = RuntimeWorldScene.ToScene(_world.Bounds.Center.X, _world.Bounds.Max.Y, _world.Bounds.Center.Z);
            var from = centre - (dir.Normalized() * 40f);
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { from, centre };
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            var col = lighting.DirLights[i].ColourA;
            parent.AddChild(new MeshInstance3D
            {
                Name = $"DirLight{i}",
                Mesh = mesh,
                MaterialOverride = Unlit(new Color((float)col.R, (float)col.G, (float)col.B)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    // --- helpers -------------------------------------------------------

    private static MeshInstance3D WireBox(Vector3 min, Vector3 max, Color colour, string name)
    {
        Vector3[] c =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] e = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        var verts = new Vector3[e.Length];
        for (int i = 0; i < e.Length; i++)
        {
            verts[i] = c[e[i]];
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = Unlit(colour),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static StandardMaterial3D Unlit(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = c,
        DisableFog = true,
        RenderPriority = 2,
    };

    private static Color MaterialColour(int id)
    {
        // Stable pseudo-colour per native collision material id.
        float h = (id * 0.61803398875f) % 1f;
        return Color.FromHsv(h, 0.65f, 1f);
    }

    private const string OverlayName = "obp_debug_tint";

    private static void ApplyTint(MeshInstance3D mi, Color? tint)
    {
        if (tint is null)
        {
            if (mi.MaterialOverlay is StandardMaterial3D existing && existing.ResourceName == OverlayName)
            {
                mi.MaterialOverlay = null;
            }

            return;
        }

        if (mi.MaterialOverlay is StandardMaterial3D m && m.ResourceName == OverlayName)
        {
            m.AlbedoColor = tint.Value;
            return;
        }

        mi.MaterialOverlay = new StandardMaterial3D
        {
            ResourceName = OverlayName,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = tint.Value,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D> Walk(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi)
            {
                yield return mi;
            }

            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static string KindOf(MeshInstance3D mi)
    {
        string n = mi.Name;
        int underscore = n.LastIndexOf('_');
        return underscore > 0 ? n[..underscore] : n;
    }

    private static Vector3 V(RuntimeCollisionBlob blob, int index)
    {
        int i = index * 3;
        return RuntimeWorldScene.ToScene(blob.Positions[i], blob.Positions[i + 1], blob.Positions[i + 2]);
    }
}

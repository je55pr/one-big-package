using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>
/// Typed, borrowed presentation inputs; this world-scoped owner never stores
/// native inventory, damage targets or the persistent player random generator.
/// </summary>
internal sealed record Rac1ArsenalBeamPorts(
    Func<Camera3D?> Camera,
    Func<Rac1EffectAssets?> Effects,
    Func<Rac1WeaponId> Equipped,
    Func<int?> FixedSeed,
    Func<float, float, float> SignedRandom);

/// <summary>
/// Per-world Tesla ribbons and Morph-o-Ray beam view. The recovered
/// Rac1TeslaPresentation owns native ribbon phases, while this class owns
/// Godot node/mesh/material lifetime and keeps the distinct visual RNG streams.
/// </summary>
internal sealed class Rac1ArsenalBeamPresenter(Node3D sceneRoot, Rac1ArsenalBeamPorts ports) : IDisposable
{
    private readonly List<MeshInstance3D> _beams = [];
    private readonly Rac1TeslaPresentation _teslaPresentation = new();
    private readonly Dictionary<int, ShaderMaterial> _teslaMaterials = [];
    private Rac1PickupRandom? _teslaEffectRandom;
    private StandardMaterial3D? _plainBeamMaterial;
    private bool _disposed;

    internal IReadOnlyList<bool> VisibleSlots() => _disposed ? [] : _beams.Select(beam => beam.Visible).ToArray();
    internal IReadOnlyList<bool> ShaderSlots() => _disposed ? [] : _beams.Select(beam => beam.MaterialOverride is ShaderMaterial).ToArray();

    internal void BeginFrame()
    {
        if (_disposed) return;
        foreach (var beam in _beams) beam.Visible = false;
    }

    internal void AdvanceTesla()
    {
        if (_disposed) return;
        _teslaPresentation.Advance();
    }

    internal void Reset()
    {
        if (_disposed) return;
        foreach (var beam in _beams)
            if (GodotObject.IsInstanceValid(beam)) beam.QueueFree();
        _beams.Clear();
        _teslaPresentation.Reset();
        _teslaMaterials.Clear();
        _teslaEffectRandom = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }

    internal void DrawTesla(int index, Vector3 from, Vector3 to) =>
        Draw(index, from, to, new(.3f, .6f, 1));

    internal void DrawMorph(int index, Vector3 from, Vector3 to) =>
        Draw(index, from, to, new(.4f, 1, .25f));

    private void Draw(int index, Vector3 from, Vector3 to, Color color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ports.Equipped() == Rac1WeaponId.TeslaClaw && ports.Effects() is { } effects)
        {
            DrawRac1TeslaRibbons(index, from, to, effects);
            return;
        }
        while (_beams.Count <= index)
        {
            var beam = new MeshInstance3D
            {
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                }
            };
            sceneRoot.AddChild(beam); _beams.Add(beam);
        }
        _beams[index].MaterialOverride = _plainBeamMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var mesh = _beams[index].Mesh as ImmediateMesh ?? new ImmediateMesh();
        mesh.ClearSurfaces(); var side = (to - from).Cross(Vector3.Up).Normalized() * .045f;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        var points = new List<Vector3> { from };
        for (int part = 1; part < 10; part++) points.Add(from.Lerp(to, part / 10f) + new Vector3(
            ports.SignedRandom(0, .13f), ports.SignedRandom(0, .13f), ports.SignedRandom(0, .13f)));
        points.Add(to);
        for (int part = 0; part + 1 < points.Count; part++)
            foreach (var p in new[] { points[part] - side, points[part] + side, points[part + 1] + side,
                points[part] - side, points[part + 1] + side, points[part + 1] - side })
            { mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(p); }
        mesh.SurfaceEnd(); _beams[index].Mesh = mesh; _beams[index].Visible = true;
    }

    private void DrawRac1TeslaRibbons(int index, Vector3 from, Vector3 to, Rac1EffectAssets effects)
    {
        _teslaEffectRandom ??= Rac1PickupRandom.FromSeed(unchecked((uint)(ports.FixedSeed() ?? System.Environment.TickCount)));
        var ribbons = _teslaPresentation.ProjectTargetLine(Rac1LootPresentation.ToEffectNative(from), Rac1LootPresentation.ToEffectNative(to), _teslaEffectRandom);
        Vector3 eye = ports.Camera()?.GlobalPosition ?? from + Vector3.Up * 5;
        for (int layer = 0; layer < ribbons.Count; layer++)
        {
            int slot = index * 4 + layer;
            while (_beams.Count <= slot)
            {
                var node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                sceneRoot.AddChild(node); _beams.Add(node);
            }
            var ribbon = ribbons[layer];
            if (!_teslaMaterials.TryGetValue(ribbon.Texture, out var material))
            {
                var texture = effects.FxTextures[ribbon.Texture];
                material = new ShaderMaterial { Shader = new Shader { Code = """
                    shader_type spatial;
                    render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;
                    uniform sampler2D art : source_color, filter_linear, repeat_enable;
                    void fragment() {
                        vec4 texel = texture(art, UV);
                        ALBEDO = texel.rgb * COLOR.rgb;
                        ALPHA = texel.a * COLOR.a;
                    }
                    """ } };
                material.SetShaderParameter("art", ImageTexture.CreateFromImage(Image.CreateFromData(texture.Width,
                    texture.Height, false, Image.Format.Rgba8, texture.Rgba)));
                _teslaMaterials.Add(ribbon.Texture, material);
            }
            var beam = _beams[slot]; beam.MaterialOverride = material;
            var mesh = beam.Mesh as ArrayMesh;
            if (mesh is null) { var previous = beam.Mesh; mesh = new ArrayMesh(); beam.Mesh = mesh; previous?.Dispose(); }
            var positions = new List<double>(); var uvs = new List<float>(); var colors = new List<float>();
            uint rgba = ribbon.Color;
            var tint = new Color((rgba & 255) / 128f, (rgba >> 8 & 255) / 128f, (rgba >> 16 & 255) / 128f, (rgba >> 24) / 128f);
            var points = ribbon.Points.Select(Rac1LootPresentation.FromEffectNative).ToArray();
            var left = new Vector3[points.Length]; var right = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
                Vector3 side = tangent.Cross(points[i] - eye);
                if (side.LengthSquared() < .000001f) side = tangent.Cross(Vector3.Right);
                if (side.LengthSquared() < .000001f) side = Vector3.Forward;
                side = side.Normalized() * ribbon.HalfWidth;
                left[i] = points[i] - side; right[i] = points[i] + side;
            }
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector3 la = left[i], ra = right[i], lb = left[i + 1], rb = right[i + 1];
                if (ribbon.EndExtension > 0)
                {
                    // 0x2bae40..2bafdc brings each blue segment's ends onto
                    // the nearer camera sphere, then extends it along its axis.
                    Vector3 pa = points[i], pb = points[i + 1];
                    float da = pa.DistanceTo(eye), db = pb.DistanceTo(eye);
                    if (da < db && db > .000001f) pb = eye + (pb - eye) * (da / db);
                    else if (da > .000001f) pa = eye + (pa - eye) * (db / da);
                    Vector3 extension = (pb - pa) * ribbon.EndExtension;
                    pa -= extension; pb += extension;
                    Vector3 side = (pb - pa).Cross(pa - eye).Normalized() * ribbon.HalfWidth;
                    la = pa - side; ra = pa + side; lb = pb - side; rb = pb + side;
                }
                float a = _teslaPresentation.TexturePhase, b = a + 1;
                Add(la, a, 0, i); Add(ra, a, 1, i); Add(rb, b, 1, i + 1);
                Add(la, a, 0, i); Add(rb, b, 1, i + 1); Add(lb, b, 0, i + 1);
            }
            EffectMeshSceneView.Upload(mesh, new(effects.FxTextures[ribbon.Texture].AssetKind, ribbon.Texture,
                positions.ToArray(), uvs.ToArray(), Enumerable.Range(0, positions.Count / 3).ToArray(), colors.ToArray()));
            beam.Visible = true;

            void Add(Vector3 p, float u, float v, int point)
            {
                var vertexColor = tint;
                if (point == 0 || point == points.Length - 1) vertexColor.A = 0;
                positions.AddRange([-p.X, p.Y, p.Z]); uvs.AddRange([u, v]);
                colors.AddRange([vertexColor.R, vertexColor.G, vertexColor.B, vertexColor.A]);
            }
        }
    }
}

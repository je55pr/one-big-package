using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Presents Novalis's scrolling water (river, plumber-entrance stream, waterfalls, pulsing pools) from
/// the level's own overlay records. The native draw callbacks submit each layer as one triangle strip
/// twice - GS context 1 with the first texture, scroll and wobble, context 2 with the second - each
/// blended by a constant alpha (ALPHA FIX / 128) over the frame buffer. The shader folds the two
/// blends into one equivalent blend. Frame-driven state (scroll, wobble phase, height blend) runs on
/// the native 60 Hz frame count.
/// </summary>
internal sealed class Rac1NovalisScrollingWaterMechanism(Rac1AuthoredMechanismPorts ports) : Rac1AuthoredMechanism
{
    private const double NativeFrameRate = 60;
    /// <summary>Drawn after the level's blended geometry: submerged plants and bank foliage sit under the water (the original draws water after the world).</summary>
    internal const int WaterRenderPriority = 1;

    private sealed record LayerView(
        Rac1NovalisScrollingWater.Layer Source, MeshInstance3D Node, ShaderMaterial Material);

    private readonly List<LayerView> _layers = [];
    private readonly Dictionary<int, ImageTexture> _textures = [];
    private Node3D? _root;
    private Shader? _shader;
    private double _frames;

    internal int LayerCount => _layers.Count;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (world.Game != OBP.Core.ObpSourceGame.Rac1 || world.LevelId != Rac1NovalisScrollingWater.NativeLevelId) return;
        if (ports.SourcePath() is not { } path)
        {
            GD.PushError("[rac1-novalis-strips] source disc path unavailable; scrolling water is not presented.");
            return;
        }
        IReadOnlyList<Rac1NovalisScrollingWater.Layer> layers;
        IReadOnlyDictionary<int, RuntimeTexture> fx;
        try
        {
            using var reader = new FileRandomAccessReader(path);
            var level = Rac1DiscIndex.Read(reader).Levels.Single(candidate => candidate.LevelId == world.LevelId);
            layers = Rac1NovalisScrollingWater.Read(Rac1ExecutableOverlay.Read(reader, level));
            var wanted = layers.SelectMany(layer => new[] { layer.Texture0, layer.Texture1 }).ToHashSet();
            fx = Rac1FxTextures.Read(Rac1LevelCore.Open(reader, level), wanted);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException or OverflowException)
        {
            GD.PushError($"[rac1-novalis-strips] retail scrolling-water tables unavailable; not presented: {exception.Message}");
            return;
        }

        _root = new Node3D { Name = "Rac1NovalisScrollingWater", TopLevel = true };
        scene.Root.AddChild(_root);
        _shader = new Shader { Code = ShaderCode };
        foreach (var layer in layers)
        {
            var material = new ShaderMaterial { Shader = _shader, RenderPriority = WaterRenderPriority };
            material.SetShaderParameter("tex0", TextureFor(fx, layer.Texture0));
            material.SetShaderParameter("tex1", TextureFor(fx, layer.Texture1));
            material.SetShaderParameter("fix_alpha", new Vector2(layer.Alpha0 / 128f, layer.Alpha1 / 128f));
            material.SetShaderParameter("amplitude", layer.WobbleAmplitude);
            var node = new MeshInstance3D
            {
                Name = $"Class{layer.NativeClass}_{layer.SourceAddress:x}",
                Mesh = BuildMesh(layer),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 4f,
            };
            _root.AddChild(node);
            _layers.Add(new(layer, node, material));
        }
        Apply();
        GD.Print($"[rac1-novalis-strips] {_layers.Count} native scrolling layers presented " +
                 $"({string.Join(", ", layers.GroupBy(l => l.NativeClass).Select(g => $"class {g.Key} x{g.Count()}"))})");
    }

    internal override void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player)
    {
        if (phase != Rac1AuthoredTickPhase.Visual || _layers.Count == 0) return;
        _frames += Math.Max(0, delta) * NativeFrameRate;
        Apply();
    }

    private void Apply()
    {
        long frame = (long)_frames;
        foreach (var view in _layers)
        {
            var layer = view.Source;
            // Scroll accumulators advance by direction * rate per frame (record +0x20..+0x2c, +0x10 / +0x14).
            float u0 = Rac1NovalisScrollingWater.Scroll(layer.Du0, layer.Rate0, frame);
            float v0 = Rac1NovalisScrollingWater.Scroll(layer.Dv0, layer.Rate0, frame);
            float u1 = Rac1NovalisScrollingWater.Scroll(layer.Du1, layer.Rate1, frame);
            float v1 = Rac1NovalisScrollingWater.Scroll(layer.Dv1, layer.Rate1, frame);
            view.Material.SetShaderParameter("scroll", new Vector4(u0, v0, u1, v1));
            // (seed * 64 + phase) * 8 modulo 4096 depends on the 24-bit frame phase only modulo 512.
            view.Material.SetShaderParameter("phase", (float)((frame * layer.FrameStep) & 511));
            view.Material.SetShaderParameter("height_mix", Rac1NovalisScrollingWater.HeightMix(layer.PulsesHeight, frame));
        }
    }

    private ImageTexture TextureFor(IReadOnlyDictionary<int, RuntimeTexture> fx, int id)
    {
        if (_textures.TryGetValue(id, out var existing)) return existing;
        var source = fx[id];
        var texture = ImageTexture.CreateFromImage(Image.CreateFromData(source.Width, source.Height, false,
            Image.Format.Rgba8, source.Rgba));
        _textures.Add(id, texture);
        return texture;
    }

    /// <summary>Native Z-up vertex (x, y, z) is scene (-x, z, y); the second height is the resting position.</summary>
    private static ArrayMesh BuildMesh(Rac1NovalisScrollingWater.Layer layer)
    {
        int count = layer.Vertices.Count;
        var positions = new Vector3[count];
        var uvs = new Vector2[count];
        var extra = new Vector2[count];
        var colours = new Color[count];
        var colour = new Color(layer.ColourR / 255f, layer.ColourG / 255f, layer.ColourB / 255f, 1f);
        for (int i = 0; i < count; i++)
        {
            var v = layer.Vertices[i];
            positions[i] = new(-v.X, v.Z1, v.Y);
            uvs[i] = new(v.U, v.V);
            extra[i] = new(v.Z0 - v.Z1, v.WobbleSeed);
            colours[i] = colour;
        }
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = extra;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.TriangleStrip, arrays);
        return mesh;
    }

    public override void Dispose()
    {
        if (_root is not null && GodotObject.IsInstanceValid(_root)) _root.QueueFree();
        _root = null;
        foreach (var view in _layers) view.Material.Dispose();
        _layers.Clear();
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        _shader?.Dispose();
        _shader = null;
    }

    // Native vertex builder 0x262618: the wobble angle is ((seed * 64 + phase) * 8 & 0xfff) / 4096 * 2pi - pi,
    // offset (sin, cos) * amplitude (record +0x18), added to the first scroll and subtracted from the second.
    // The two sequential constant-alpha blends are folded into one: the second pass lies on top, so
    // result = D(1-a0)(1-a1) + a0(1-a1)C0 + a1 C1. GS colour modulation saturates per pass (0x80 = 1.0).
    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;
        uniform sampler2D tex0 : source_color, filter_linear, repeat_enable;
        uniform sampler2D tex1 : source_color, filter_linear, repeat_enable;
        uniform vec4 scroll = vec4(0.0);
        uniform vec2 fix_alpha = vec2(0.375);
        uniform float phase = 0.0;
        uniform float amplitude = 0.0;
        uniform float height_mix = 0.0;
        varying vec2 uv_first;
        varying vec2 uv_second;
        void vertex() {
            float index = mod((UV2.y * 64.0 + phase) * 8.0, 4096.0);
            float angle = index / 4096.0 * 6.2831855 - 3.1415927;
            vec2 wobble = amplitude * vec2(sin(angle), cos(angle));
            uv_first = UV + scroll.xy + wobble;
            uv_second = UV + scroll.zw - wobble;
            VERTEX.y += UV2.x * height_mix;
        }
        void fragment() {
            vec3 tint = COLOR.rgb * (255.0 / 128.0);
            vec3 first = min(texture(tex0, uv_first).rgb * tint, vec3(1.0));
            vec3 second = min(texture(tex1, uv_second).rgb * tint, vec3(1.0));
            float a0 = fix_alpha.x;
            float a1 = fix_alpha.y;
            float coverage = 1.0 - (1.0 - a0) * (1.0 - a1);
            ALBEDO = (a0 * (1.0 - a1) * first + a1 * second) / max(coverage, 0.0001);
            ALPHA = coverage;
        }
        """;
}

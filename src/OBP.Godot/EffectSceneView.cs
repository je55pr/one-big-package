using Godot;
using OBP.Core;
using OBP.Runtime;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>Lease-owned presentation only: qualified resources, batched sprites, neutral meshes and admitted light facts.</summary>
public sealed class EffectSceneView : IDisposable
{
    private sealed record Batch(MultiMeshInstance3D Node, MultiMesh Instances, QuadMesh Mesh, ShaderMaterial Material);
    private readonly Node3D _root;
    private readonly EffectMeshSceneView _meshes;
    private readonly Dictionary<ObpSourceRef, ImageTexture> _textures = [];
    private readonly Dictionary<(RuntimeBlendEquation Blend, bool Test, bool Write, bool Fade, RuntimeEffectSpriteProjection Projection), Shader> _shaders = [];
    private readonly Dictionary<(ObpSourceRef Texture, RuntimeEffectDrawState State, RuntimeEffectSpriteProjection Projection), Batch> _batches = [];
    private readonly Dictionary<(ObpSourceRef Texture, RuntimeMaterialPresentation? State), StandardMaterial3D> _meshMaterials = [];
    private readonly List<OmniLight3D> _lights = [];
    private bool _disposed;
    public int RenderedCount { get; private set; }
    public int DrawCalls { get; private set; }
    public int TextureCount => _textures.Count;
    public int BatchCount => _batches.Count;
    public int LightCount => _lights.Count(l => l.Visible);
    public EffectSceneView(Node3D parent)
    {
        _root = new() { Name = "SourceEffects" }; parent.AddChild(_root); _meshes = new(_root);
    }
    public void Render(IEnumerable<RuntimeEffectFrame> frames, IReadOnlyList<RuntimeEffectTexture> resources, float aspect = 1)
    {
        var bindings = resources.ToDictionary(t => t.Source, t => t.Texture);
        foreach (var batch in _batches.Values) batch.Instances.VisibleInstanceCount = 0;
        RenderedCount = DrawCalls = 0;
        var output = frames.ToArray();
        foreach (var group in output.SelectMany(f => f.Sprites).GroupBy(p => (p.Texture, p.Draw, p.Projection)))
        {
            var key = group.Key;
            if (!bindings.TryGetValue(key.Texture, out var art)) throw new InvalidDataException("Effect texture is unavailable: " + key.Texture);
            if (!_batches.TryGetValue(key, out var batch))
            {
                var material = SpriteMaterial(key.Texture, art, key.Draw, key.Projection);
                var mesh = new QuadMesh { Size = Vector2.One, Material = material };
                var instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh };
                var node = new MultiMeshInstance3D { Multimesh = instances, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                _root.AddChild(node); _batches.Add(key, batch = new(node, instances, mesh, material));
            }
            batch.Material.SetShaderParameter("aspect", aspect);
            var sprites = group.ToArray();
            if (batch.Instances.InstanceCount < sprites.Length)
                batch.Instances.InstanceCount = Math.Max(16, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)sprites.Length));
            batch.Instances.VisibleInstanceCount = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
            {
                var p = sprites[i];
                var position = RuntimeWorldScene.ToScene(p.Position.X, p.Position.Y, p.Position.Z);
                batch.Instances.SetInstanceTransform(i, new(Basis.Identity.Scaled(Vector3.One * p.Size), position));
                batch.Instances.SetInstanceColor(i, new(p.Color.X, p.Color.Y, p.Color.Z, p.Color.W));
                batch.Instances.SetInstanceCustomData(i, new(p.ProjectedAxes.X, p.ProjectedAxes.Y, p.Rotation, 0));
            }
            RenderedCount += sprites.Length; DrawCalls++;
        }
        var meshOutput = output.SelectMany(f => f.Meshes).ToArray();
        _meshes.Sample(meshOutput.Select(m => m.Surface).ToArray(), (i, _) => MeshMaterial(meshOutput[i], bindings)); DrawCalls += _meshes.SurfaceCount;
        var lights = output.SelectMany(f => f.Lights).ToArray();
        if (lights.Length > 8) throw new NotSupportedException("This effect presentation admits at most eight explicitly selected lights.");
        while (_lights.Count < lights.Length) { var light = new OmniLight3D(); _root.AddChild(light); _lights.Add(light); }
        for (int i = 0; i < _lights.Count; i++)
        {
            var light = _lights[i]; light.Visible = i < lights.Length;
            if (!light.Visible) continue;
            var fact = lights[i]; light.Position = RuntimeWorldScene.ToScene(fact.Position.X, fact.Position.Y, fact.Position.Z);
            light.LightColor = new(fact.Color.X, fact.Color.Y, fact.Color.Z); light.LightEnergy = fact.Energy; light.OmniRange = fact.Range;
        }
        foreach (var key in _batches.Keys.Where(k => !bindings.ContainsKey(k.Texture)).ToArray())
        { Release(_batches[key]); _batches.Remove(key); }
        foreach (var key in _meshMaterials.Keys.Where(k => !bindings.ContainsKey(k.Texture)).ToArray())
        { _meshMaterials[key].Dispose(); _meshMaterials.Remove(key); }
        foreach (var key in _textures.Keys.Where(k => !bindings.ContainsKey(k)).ToArray())
        { _textures[key].Dispose(); _textures.Remove(key); }
        if (_batches.Count == 0) { foreach (var shader in _shaders.Values) shader.Dispose(); _shaders.Clear(); }
    }
    private ImageTexture Texture(ObpSourceRef source, RuntimeTexture data)
    {
        if (_textures.TryGetValue(source, out var texture)) return texture;
        using var image = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgba8, data.Rgba);
        _textures.Add(source, texture = ImageTexture.CreateFromImage(image)); return texture;
    }
    private ShaderMaterial SpriteMaterial(ObpSourceRef source, RuntimeTexture texture, RuntimeEffectDrawState state, RuntimeEffectSpriteProjection projection)
    {
        if (state.Blend is not (RuntimeBlendEquation.SourceAlpha or RuntimeBlendEquation.AdditiveSourceAlpha))
            throw new NotSupportedException("This sprite blend equation is not admitted.");
        var shaderKey = (state.Blend, state.DepthTest, state.DepthWrite, state.DepthFade, projection);
        if (!_shaders.TryGetValue(shaderKey, out var shader))
        {
            string blend = state.Blend == RuntimeBlendEquation.AdditiveSourceAlpha ? "blend_add" : "blend_mix";
            string depth = state.DepthWrite ? "depth_draw_always" : "depth_draw_never";
            string test = state.DepthTest ? "" : ", depth_test_disabled";
            string vertex = projection == RuntimeEffectSpriteProjection.ProjectedBillboard ? """
                vec4 center = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
                vec2 axes = INSTANCE_CUSTOM.xy;
                vec2 offset = mat2(vec2(axes.x, axes.y), vec2(-axes.y, axes.x)) * (VERTEX.xy * 2.0);
                POSITION = center + vec4(offset.x / aspect, offset.y, 0.0, 0.0) * center.w;
                particle_depth = -MODELVIEW_MATRIX[3].z;
                """ : """
                float s = sin(INSTANCE_CUSTOM.z), c = cos(INSTANCE_CUSTOM.z);
                VERTEX.xy = mat2(vec2(c,s), vec2(-s,c)) * VERTEX.xy;
                MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
                MODELVIEW_MATRIX *= mat4(vec4(length(MODEL_MATRIX[0].xyz),0,0,0), vec4(0,length(MODEL_MATRIX[1].xyz),0,0), vec4(0,0,length(MODEL_MATRIX[2].xyz),0), vec4(0,0,0,1));
                particle_depth = -MODELVIEW_MATRIX[3].z;
                """;
            shader = new() { Code = $$"""
                shader_type spatial;
                render_mode unshaded, {{blend}}, cull_disabled, {{depth}}{{test}}, fog_disabled;
                uniform sampler2D art : source_color, filter_linear, repeat_disable;
                uniform float aspect = 1.0;
                uniform float near_depth;
                uniform float far_depth;
                varying float particle_depth;
                void vertex() { {{vertex}} }
                void fragment() {
                    vec4 texel = texture(art, UV);
                    float fade = {{(state.DepthFade ? "clamp(min((far_depth - particle_depth) / 16.0, particle_depth - near_depth), 0.0, 1.0)" : "1.0")}};
                    ALBEDO = texel.rgb * COLOR.rgb;
                    ALPHA = texel.a * COLOR.a * fade;
                }
                """ };
            _shaders.Add(shaderKey, shader);
        }
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("art", Texture(source, texture)); material.SetShaderParameter("near_depth", state.NearFade);
        material.SetShaderParameter("far_depth", state.FarFade); return material;
    }
    private Material MeshMaterial(RuntimeEffectMesh fact, IReadOnlyDictionary<ObpSourceRef, RuntimeTexture> bindings)
    {
        var key = (fact.Texture, fact.Surface.MaterialPresentation);
        if (_meshMaterials.TryGetValue(key, out var material)) return material;
        if (!bindings.TryGetValue(fact.Texture, out var texture)) throw new InvalidDataException("Effect mesh texture is unavailable.");
        var plan = WorldMaterialFactory.ResolveNativePresentation(fact.Surface.MaterialPresentation);
        material = new()
        {
            AlbedoTexture = Texture(fact.Texture, texture),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            TextureRepeat = plan.TextureRepeat ?? true,
            TextureFilter = plan.TextureFilter ?? BaseMaterial3D.TextureFilterEnum.Linear,
            BlendMode = plan.BlendMode,
            Transparency = plan.Transparency ?? BaseMaterial3D.TransparencyEnum.Disabled
        };
        _meshMaterials.Add(key, material); return material;
    }
    public void Clear()
    {
        _meshes.Clear();
        foreach (var batch in _batches.Values)
            Release(batch);
        foreach (var material in _meshMaterials.Values) material.Dispose();
        foreach (var shader in _shaders.Values) shader.Dispose();
        foreach (var texture in _textures.Values) texture.Dispose();
        foreach (var light in _lights) { light.Visible = false; light.QueueFree(); }
        _batches.Clear(); _meshMaterials.Clear(); _shaders.Clear(); _textures.Clear(); _lights.Clear(); RenderedCount = DrawCalls = 0;
    }
    private static void Release(Batch batch)
    { batch.Node.Visible = false; batch.Node.QueueFree(); batch.Instances.Dispose(); batch.Mesh.Dispose(); batch.Material.Dispose(); }
    public void Dispose() { if (_disposed) return; Clear(); _root.QueueFree(); _disposed = true; }
}

using Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Presentation of source-owned sprite records; no emission or motion rules live here.</summary>
public sealed partial class Rac1ParticleView : Node3D
{
    private sealed record Batch(MultiMeshInstance3D Node, MultiMesh Instances);
    private readonly Dictionary<(int Texture, Rac1ParticleSession.Blend Blend, byte Depth), Batch> _batches = [];
    private readonly Dictionary<int, ImageTexture> _textures = [];
    private readonly Dictionary<(Rac1ParticleSession.Blend Blend, byte Depth), Shader> _shaders = [];
    public int RenderedCount { get; private set; }
    public int DrawCalls { get; private set; }

    public void Render(Rac1ParticleSession session, IReadOnlyDictionary<int, RuntimeTexture> textures)
    {
        foreach (var batch in _batches.Values) batch.Instances.VisibleInstanceCount = 0;
        RenderedCount = DrawCalls = 0;
        foreach (var group in session.Live.Where(p => p.Size > 0 && p.Remaining > 0)
            .GroupBy(p => (p.Texture, p.Blending, p.DepthRange)))
        {
            if (!textures.TryGetValue(group.Key.Texture, out var texture)) continue;
            if (!_batches.TryGetValue(group.Key, out var batch))
            {
                var mesh = new QuadMesh { Size = Vector2.One, Material = Material(group.Key.Blending, group.Key.DepthRange, texture) };
                var instances = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    UseCustomData = true,
                    Mesh = mesh
                };
                var node = new MultiMeshInstance3D
                {
                    Name = $"Part{texture.TextureId}_{group.Key.Blending}",
                    Multimesh = instances,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                };
                AddChild(node); _batches.Add(group.Key, batch = new(node, instances));
            }
            var particles = group.ToArray();
            if (batch.Instances.InstanceCount < particles.Length)
                batch.Instances.InstanceCount = Math.Max(16, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)particles.Length));
            batch.Instances.VisibleInstanceCount = particles.Length;
            for (int i = 0; i < particles.Length; i++)
            {
                var p = particles[i];
                var position = new Vector3(-p.Position.X, p.Position.Z, p.Position.Y);
                batch.Instances.SetInstanceTransform(i, new(Basis.Identity.Scaled(Vector3.One * p.Size), position));
                batch.Instances.SetInstanceColor(i, new((p.Color & 255) / 128f, (p.Color >> 8 & 255) / 128f,
                    (p.Color >> 16 & 255) / 128f, (p.Color >> 24) / 128f));
                batch.Instances.SetInstanceCustomData(i, new(p.Angle / 256f, 0, 0, 0));
            }
            RenderedCount += particles.Length; DrawCalls++;
        }
    }

    private ShaderMaterial Material(Rac1ParticleSession.Blend blend, byte depth, RuntimeTexture texture)
    {
        if (!_textures.TryGetValue(texture.TextureId, out var image))
            _textures.Add(texture.TextureId, image = ImageTexture.CreateFromImage(
                Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba)));
        if (!_shaders.TryGetValue((blend, depth), out var shader))
        {
            string blending = blend == Rac1ParticleSession.Blend.Additive ? "blend_add" : "blend_mix";
            shader = new Shader { Code = $$"""
                shader_type spatial;
                render_mode unshaded, {{blending}}, cull_disabled, depth_draw_never, fog_disabled;
                uniform sampler2D art : source_color, filter_linear, repeat_disable;
                uniform float near_depth;
                uniform float far_depth;
                varying float particle_depth;
                void vertex() {
                    float angle = INSTANCE_CUSTOM.x * 6.28318530718;
                    float s = sin(angle), c = cos(angle);
                    VERTEX.xy = mat2(vec2(c,s), vec2(-s,c)) * VERTEX.xy;
                    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
                    MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz),0,0,0), vec4(0,length(MODEL_MATRIX[1].xyz),0,0), vec4(0,0,length(MODEL_MATRIX[2].xyz),0), vec4(0,0,0,1));
                    particle_depth = -MODELVIEW_MATRIX[3].z;
                }
                void fragment() {
                    vec4 texel = texture(art, UV);
                    float fade = clamp(min((far_depth - particle_depth) / 16.0, particle_depth - near_depth), 0.0, 1.0);
                    ALBEDO = texel.rgb * COLOR.rgb;
                    ALPHA = texel.a * COLOR.a * fade;
                }
                """ };
            _shaders.Add((blend, depth), shader);
        }
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("art", image);
        material.SetShaderParameter("near_depth", (depth & 15) * .25f);
        material.SetShaderParameter("far_depth", (depth & 0xf0) * 2f);
        return material;
    }
}

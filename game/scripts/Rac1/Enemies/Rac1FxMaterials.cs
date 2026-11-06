using Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Shared Godot materials and mesh writer for recovered R&amp;C1 Fx-bank surfaces. The GS alpha registers
/// 0x44 / 0x48 become alpha / additive blending; vertex colours arrive as bytes/255 and the shader restores
/// the PS2 0x80 = 1.0 range (alpha 0xFF therefore saturates, as on hardware).
/// </summary>
/// <param name="priorityBias">Added to the render priority of every material; the particle pass of the original draws after mesh callbacks, so cluster meshes use a negative bias.</param>
internal sealed class Rac1FxMaterials(IReadOnlyDictionary<int, RuntimeTexture> textures, int priorityBias = 0) : IDisposable
{
    private readonly Dictionary<(int Texture, Rac1FxBlend Blend), ShaderMaterial> _materials = [];
    private readonly Dictionary<Rac1FxBlend, Shader> _shaders = [];
    private readonly Dictionary<int, ImageTexture> _images = [];

    internal ShaderMaterial Get(int texture, Rac1FxBlend blend)
    {
        if (_materials.TryGetValue((texture, blend), out var material)) return material;
        if (!textures.TryGetValue(texture, out var source))
            throw new InvalidDataException($"R&C1 effect needs Fx texture {texture}, which the level did not provide.");
        if (!_shaders.TryGetValue(blend, out var shader))
            _shaders.Add(blend, shader = new Shader { Code = $$"""
                shader_type spatial;
                render_mode unshaded, {{(blend == Rac1FxBlend.Additive ? "blend_add" : "blend_mix")}}, cull_disabled, depth_draw_never, fog_disabled;
                uniform sampler2D art : source_color, filter_linear, repeat_enable;
                void fragment() {
                    vec4 texel = texture(art, UV);
                    vec4 tint = COLOR * (255.0 / 128.0);
                    ALBEDO = texel.rgb * tint.rgb;
                    ALPHA = clamp(texel.a * tint.a, 0.0, 1.0);
                }
                """ });
        if (!_images.TryGetValue(texture, out var image))
            _images.Add(texture, image = ImageTexture.CreateFromImage(Image.CreateFromData(source.Width, source.Height, false,
                Image.Format.Rgba8, source.Rgba)));
        material = new ShaderMaterial { Shader = shader, RenderPriority = priorityBias + (blend == Rac1FxBlend.Alpha ? 1 : 0) };
        material.SetShaderParameter("art", image);
        _materials.Add((texture, blend), material);
        return material;
    }

    /// <summary>Rewrites <paramref name="mesh"/> with one surface per (texture, blend), alpha-blended last. Native Z-up positions.</summary>
    internal void Write(ImmediateMesh mesh, IEnumerable<Rac1FxSurface> surfaces, Func<System.Numerics.Vector3, Vector3> toGodot)
    {
        mesh.ClearSurfaces();
        foreach (var group in surfaces.GroupBy(s => (s.Texture, s.Blend)).OrderBy(g => g.Key.Blend == Rac1FxBlend.Alpha))
        {
            mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, Get(group.Key.Texture, group.Key.Blend));
            foreach (var surface in group)
                foreach (int corner in surface.Indices)
                {
                    uint rgba = surface.Colors[corner];
                    mesh.SurfaceSetColor(new((rgba & 255) / 255f, (rgba >> 8 & 255) / 255f, (rgba >> 16 & 255) / 255f, (rgba >> 24) / 255f));
                    mesh.SurfaceSetUV(new(surface.Uvs[corner].X, surface.Uvs[corner].Y));
                    mesh.SurfaceAddVertex(toGodot(surface.Positions[corner]));
                }
            mesh.SurfaceEnd();
        }
    }

    public void Dispose()
    {
        foreach (var material in _materials.Values) material.Dispose();
        foreach (var shader in _shaders.Values) shader.Dispose();
        foreach (var image in _images.Values) image.Dispose();
        _materials.Clear(); _shaders.Clear(); _images.Clear();
    }
}

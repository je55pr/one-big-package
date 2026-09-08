using Godot;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>
/// Owns the decoded textures and the <see cref="StandardMaterial3D"/> cache for
/// one built world, and is the single place a Godot material is created from a
/// neutral <see cref="RuntimeMesh"/> / <see cref="RuntimeObjectMesh"/> /
/// <see cref="RuntimeAnimatedMesh"/>. The decisions (alpha handling, culling,
/// vertex-colour use, the tfrag bake curve, emission) live in the engine-neutral
/// <see cref="MaterialModel"/>; this only translates them to Godot.
///
/// <para>World geometry is <see cref="BaseMaterial3D.ShadingModeEnum.Unshaded"/>
/// throughout — the baked PS2 vertex colour is the lighting.</para>
/// </summary>
public sealed class WorldMaterialFactory
{
    private static readonly System.Collections.Generic.Dictionary<string, Color> KindTint = new()
    {
        ["tfrag"] = new Color(0.64f, 0.62f, 0.58f),
        ["tie"] = new Color(0.68f, 0.64f, 0.57f),
        ["shrub"] = new Color(0.40f, 0.52f, 0.34f),
        ["moby"] = new Color(0.66f, 0.61f, 0.53f),
        ["moby-marker"] = new Color(0.95f, 0.35f, 0.55f),
        ["sky"] = new Color(0.42f, 0.52f, 0.62f),
        ["death-plane"] = new Color(0.22f, 0.55f, 0.35f),
    };

    private readonly System.Collections.Generic.Dictionary<(string, int), ImageTexture> _textures = new();
    private readonly System.Collections.Generic.Dictionary<(string, int), AlphaProfile> _alpha = new();
    private readonly System.Collections.Generic.Dictionary<(string, int), double> _luminance = new();
    private readonly System.Collections.Generic.Dictionary<(string, int), StandardMaterial3D> _cache = new();
    private readonly System.Collections.Generic.Dictionary<string, int> _untexturedTris = new();

    public WorldMaterialFactory(RuntimeWorld world)
    {
        foreach (var t in world.Textures)
        {
            if (t.Width <= 0 || t.Height <= 0 || t.Rgba.Length != t.Width * t.Height * 4)
            {
                continue;
            }

            var key = (t.AssetKind, t.TextureId);
            _alpha[key] = AlphaProfile.Analyse(t.Rgba);
            _luminance[key] = MaterialModel.MeanLuminance(t.Rgba);
            var image = Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, t.Rgba);
            _textures[key] = ImageTexture.CreateFromImage(image);
        }
    }

    /// <summary>Decoded texture count (for capture / HUD stats).</summary>
    public int TextureCount => _textures.Count;

    /// <summary>Per-kind triangle totals that fell back to a flat tint (no decoded texture).</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, int> UntexturedTriangleReport => _untexturedTris;

    public ImageTexture? TextureFor(string assetKind, int textureId) =>
        _textures.GetValueOrDefault((assetKind, textureId));

    public AlphaProfile? AlphaFor(string assetKind, int textureId) =>
        _alpha.TryGetValue((assetKind, textureId), out var p) ? p : null;

    /// <summary>
    /// Material for a welded static <see cref="RuntimeMesh"/>. Textured only when
    /// the mesh has UVs and a decoded texture exists. Includes the sky backdrop
    /// handling.
    /// </summary>
    public StandardMaterial3D StaticMesh(string assetKind, int textureId, bool hasUv, bool hasColour, bool isSky, bool renderWithoutTexture, int triangleCount)
    {
        var key = (assetKind, textureId);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        _textures.TryGetValue(key, out var tex);
        bool textured = hasUv && tex is not null;

        if (!textured && assetKind is "tfrag" or "tie" or "shrub" or "moby")
        {
            _untexturedTris[assetKind] = _untexturedTris.GetValueOrDefault(assetKind) + triangleCount;
        }

        var mat = NewBase(assetKind);
        mat.CullMode = MaterialModel.BackFaceCull(assetKind)
            ? BaseMaterial3D.CullModeEnum.Back
            : BaseMaterial3D.CullModeEnum.Disabled;

        if (textured)
        {
            mat.AlbedoTexture = tex;
            mat.AlbedoColor = Colors.White;
            ApplyAlpha(mat, key);
            ApplyEmission(mat, key);
        }

        if (MaterialModel.VertexColourAsAlbedo(assetKind, hasColour))
        {
            mat.VertexColorUseAsAlbedo = true;
        }

        if (isSky)
        {
            if (!textured && renderWithoutTexture && hasColour)
            {
                // Importer-provided vertex RGB is the material colour, so do not
                // multiply it by the generic sky fallback tint.
                mat.AlbedoColor = Colors.White;
            }

            // Camera-centred backdrop: drawn first, never writes depth, so it
            // can't occlude the level; still depth-tests so buildings in front of
            // the (huge, camera-parked) dome hide the clouds naturally. The
            // materialless gouraud shell draws behind the textured cloud layers.
            mat.RenderPriority = renderWithoutTexture ? -9 : -8;
            mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
            mat.VertexColorUseAsAlbedo = true;
            mat.Transparency = hasColour
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled;
        }

        _cache[key] = mat;
        return mat;
    }

    /// <summary>
    /// Material for a per-instance <see cref="RuntimeObjectMesh"/> /
    /// <see cref="RuntimeAnimatedMesh"/> — always 2-sided, textured whenever a
    /// decoded texture exists.
    /// </summary>
    public StandardMaterial3D Instanced(string assetKind, int textureId, bool hasColour)
    {
        var key = (assetKind, textureId);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        _textures.TryGetValue(key, out var tex);
        var mat = NewBase(assetKind);
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.AlbedoColor = tex is null ? KindTint.GetValueOrDefault(assetKind, Colors.White) : Colors.White;

        if (tex is not null)
        {
            mat.AlbedoTexture = tex;
            ApplyAlpha(mat, key);
            ApplyEmission(mat, key);
        }

        if (hasColour)
        {
            mat.VertexColorUseAsAlbedo = true;
        }

        _cache[key] = mat;
        return mat;
    }

    private static StandardMaterial3D NewBase(string assetKind) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        // Bilinear + mips (the PS2 filtered too) — nearest shimmered detailed
        // foliage / panel textures into moiré crosshatch.
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        AlbedoColor = KindTint.GetValueOrDefault(assetKind, Colors.White),
    };

    private void ApplyAlpha(StandardMaterial3D mat, (string, int) key)
    {
        // Any texel below the PS2 "half" alpha → alpha-scissor. (A histogram-
        // driven AlphaMode choice lands in the fidelity pass.)
        if (_alpha.TryGetValue(key, out var profile) && profile.AnyBelowHalf)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            mat.AlphaScissorThreshold = 0.5f;
        }
    }

    private void ApplyEmission(StandardMaterial3D mat, (string, int) key)
    {
        _ = mat;
        _ = key;
        // Emissive presentation lands in the fidelity pass.
    }
}

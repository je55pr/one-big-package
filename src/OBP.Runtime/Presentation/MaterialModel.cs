namespace OBP.Runtime.Presentation;

/// <summary>How a textured surface resolves its alpha channel.</summary>
public enum AlphaMode
{
    /// <summary>Fully opaque — no transparency handling.</summary>
    Opaque,

    /// <summary>Hard cut-out (foliage, grates) — alpha-scissor at a threshold.</summary>
    Scissor,

    /// <summary>Genuine translucency (glass, decals) — alpha blend.</summary>
    Blend,
}

/// <summary>
/// A cheap summary of a decoded texture's alpha channel: the fraction of texels
/// that are effectively opaque, hard cut-out (fully transparent), or partially
/// translucent. Lets the material model pick <see cref="AlphaMode"/> from what
/// the texture actually contains instead of "any texel below half alpha".
/// </summary>
public readonly record struct AlphaProfile(
    double OpaqueFraction,
    double CutoutFraction,
    double TranslucentFraction,
    bool AnyBelowHalf)
{
    public static readonly AlphaProfile FullyOpaque = new(1, 0, 0, false);

    public bool HasTransparency => CutoutFraction > 0 || TranslucentFraction > 0;

    /// <summary>Analyse a flat RGBA byte buffer (4 bytes/texel).</summary>
    public static AlphaProfile Analyse(byte[] rgba)
    {
        if (rgba.Length < 4)
        {
            return FullyOpaque;
        }

        long opaque = 0, cutout = 0, translucent = 0, n = 0;
        bool anyBelowHalf = false;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            byte a = rgba[i];
            n++;
            if (a < 128)
            {
                anyBelowHalf = true;
            }

            if (a >= 250)
            {
                opaque++;
            }
            else if (a <= 8)
            {
                cutout++;
            }
            else
            {
                translucent++;
            }
        }

        return n == 0
            ? FullyOpaque
            : new AlphaProfile((double)opaque / n, (double)cutout / n, (double)translucent / n, anyBelowHalf);
    }
}

/// <summary>
/// Engine-independent material decisions for the unshaded PS2 world render. All
/// pure so they can be unit tested and reported by the debug overlay / inspector
/// without a Godot material. Baked PS2 vertex colour stays authoritative — none
/// of this relights geometry.
/// </summary>
public static class MaterialModel
{
    /// <summary>
    /// Pick the alpha handling for a textured surface. A handful of stray
    /// sub-threshold texels in an otherwise solid wall no longer forces
    /// alpha-scissor (which speckled solid geometry under the old "any texel
    /// &lt; 128" test).
    /// </summary>
    public static (AlphaMode Mode, float ScissorThreshold) ResolveAlpha(AlphaProfile profile)
    {
        const double noiseFloor = 0.002;   // < 0.2% of texels — treat as decode noise
        const double blendFloor = 0.02;    // >= 2% partial alpha — a real translucent material

        if (profile.TranslucentFraction >= blendFloor)
        {
            return (AlphaMode.Blend, 0.5f);
        }

        if (profile.CutoutFraction > noiseFloor)
        {
            return (AlphaMode.Scissor, 0.5f);
        }

        return (AlphaMode.Opaque, 0.5f);
    }

    /// <summary>
    /// Whether a kind's geometry may be back-face culled. Currently <c>false</c>
    /// for everything: the decoded winding is not reliably outward-facing across
    /// terrain and built structure (whole regions — e.g. the Tabora desert
    /// floor — end up visible only from below), so all world geometry renders
    /// two-sided. That trades a little see-through at building interiors for no
    /// missing surfaces. Re-enable per kind here if an importer guarantees
    /// consistent outward winding.
    /// </summary>
    public static bool BackFaceCull(string assetKind) => false;

    /// <summary>tfrag / tie / sky texture atlases tile; keep repeat on for them (and for M1's sky UV scroll).</summary>
    public static bool TileTexture(string assetKind) => assetKind is "tfrag" or "tie" or "sky";

    /// <summary>
    /// Which kinds use their per-vertex colour as albedo: mobies (a decoded
    /// normal shade, no baked colour), tfrags (baked static lighting) and sky
    /// (cloud-edge alpha). Others ignore any stray colour array.
    /// </summary>
    public static bool VertexColourAsAlbedo(string assetKind, bool hasColour) =>
        hasColour && assetKind is "moby" or "tfrag" or "sky";

    /// <summary>
    /// tfrag baked-colour lift: gamma lift, then map to (0.6 .. 1.4) so shadowed
    /// terrain never crushes to black and lit terrain still lifts. Applied per
    /// channel to a 0..1 value. (Was the inline <c>Bake</c> local in
    /// <c>RuntimeWorldScene</c>.)
    /// </summary>
    public static float BakeCurve(float c) =>
        System.Math.Min(1f, 0.6f + (0.8f * (float)System.Math.Pow(System.Math.Clamp(c, 0f, 1f), 0.62)));

    /// <summary>Whether a kind's baked vertex colour gets the tfrag lift curve.</summary>
    public static bool UsesBakeCurve(string assetKind) => assetKind == "tfrag";

    /// <summary>
    /// A subtle emission energy for textures that decode very bright (signs,
    /// lamps, screens) so they read as light sources under the glow-free
    /// pipeline — never a relight, just a lift on that one material. 0 = none.
    /// </summary>
    public static double EmissionEnergy(double meanLuminance)
    {
        const double threshold = 0.82;
        return meanLuminance > threshold
            ? System.Math.Clamp((meanLuminance - threshold) * 1.6, 0.0, 0.35)
            : 0.0;
    }

    /// <summary>Mean Rec.709 luminance of a flat RGBA buffer, 0..1.</summary>
    public static double MeanLuminance(byte[] rgba)
    {
        if (rgba.Length < 4)
        {
            return 0;
        }

        double sum = 0;
        long n = 0;
        for (int i = 0; i + 2 < rgba.Length; i += 4)
        {
            sum += ((0.2126 * rgba[i]) + (0.7152 * rgba[i + 1]) + (0.0722 * rgba[i + 2])) / 255.0;
            n++;
        }

        return n == 0 ? 0 : sum / n;
    }
}

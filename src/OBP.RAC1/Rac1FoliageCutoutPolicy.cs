using OBP.PS2.Geometry;
using OBP.PS2.Presentation;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.RAC1;

/// <summary>
/// Conservative source-geometry test for large, interleaved cutout foliage layers.
/// Unlike a level/class allowlist this survives texture reindexing and repeated
/// source assets on other planets. It only admits the implicated pair of textures;
/// unrelated surfaces on the same shrub class retain native alpha blending.
/// </summary>
internal static class Rac1FoliageCutoutPolicy
{
    // Retail cases: Veldin 466 (474/456 faces, 53% shared vertices),
    // Veldin Return 466 & 510 (53% / 72%), Quartu 399 (73%),
    // Novalis 116 (64%). Smaller decorative overlaps stay in alpha blending.
    internal const int MinimumLayerFaces = 128;
    internal const double MinimumSharedVertexFraction = 0.50;
    internal const double MinimumCutoutFraction = 0.20;
    internal const double MaximumTranslucentFraction = 0.20;
    internal const double MaximumCutoutProfileDifference = 0.08;

    private readonly record struct SourceVertex(long X, long Y, long Z);

    /// <summary>
    /// Select level-local texture IDs only when large shrub leaf-card layers
    /// overlap significantly and their native materials allow cutout rendering.
    /// </summary>
    internal static HashSet<int> FindLayeredCutoutTextures(
        Rac1StaticClasses.ShrubClass shrub,
        IReadOnlyDictionary<int, AlphaProfile> alpha)
    {
        var (verticesByTexture, facesByTexture) = CollectEligibleLayers(shrub, alpha);
        var candidates = facesByTexture.Keys
            .Where(texture => facesByTexture[texture] >= MinimumLayerFaces)
            .OrderBy(texture => texture).ToArray();
        var admitted = new HashSet<int>();

        for (int a = 0; a < candidates.Length; a++)
        {
            for (int b = a + 1; b < candidates.Length; b++)
            {
                int left = candidates[a], right = candidates[b];
                if (!AreInterleavedLayers(left, right, facesByTexture, verticesByTexture, alpha))
                    continue;

                admitted.Add(left);
                admitted.Add(right);
            }
        }

        return admitted;
    }

    /// <summary>
    /// Collect the quantized native vertices for cutout-capable faces, keeping
    /// independent counts for each native texture ID within the shrub class.
    /// </summary>
    private static (Dictionary<int, HashSet<SourceVertex>> Vertices, Dictionary<int, int> Faces)
        CollectEligibleLayers(Rac1StaticClasses.ShrubClass shrub, IReadOnlyDictionary<int, AlphaProfile> alpha)
    {
        var mesh = shrub.Mesh;
        var verticesByTexture = new Dictionary<int, HashSet<SourceVertex>>();
        var facesByTexture = new Dictionary<int, int>();

        for (int face = 0; face < shrub.TriangleTextureIds.Length; face++)
        {
            int textureId = shrub.TriangleTextureIds[face];
            if (!IsEligibleFace(mesh, face, textureId, alpha))
                continue;

            if (!verticesByTexture.TryGetValue(textureId, out var vertices))
                verticesByTexture[textureId] = vertices = [];
            facesByTexture[textureId] = facesByTexture.GetValueOrDefault(textureId) + 1;

            for (int corner = 0; corner < 3; corner++)
            {
                int index = mesh.Indices[face * 3 + corner] * 3;
                // Source coordinates are quantized; tolerate sub-source-unit
                // decode noise without using world/placement-dependent distances.
                vertices.Add(new SourceVertex(
                    (long)Math.Round(mesh.Positions[index] * 100000d),
                    (long)Math.Round(mesh.Positions[index + 1] * 100000d),
                    (long)Math.Round(mesh.Positions[index + 2] * 100000d)));
            }
        }

        return (verticesByTexture, facesByTexture);
    }

    /// <summary>Exclude translucent, additive and fixed-alpha source faces.</summary>
    private static bool IsEligibleFace(RcShrub.Mesh mesh, int face, int textureId,
        IReadOnlyDictionary<int, AlphaProfile> alpha)
    {
        if (!alpha.TryGetValue(textureId, out var profile) ||
            profile.CutoutFraction < MinimumCutoutFraction ||
            profile.TranslucentFraction > MaximumTranslucentFraction ||
            mesh.TriangleAlphaBlendEnabled[face] is not true)
            return false;

        int stateIndex = mesh.TriangleMaterialStateIndices[face];
        if ((uint)stateIndex >= (uint)mesh.Materials.Count)
            return false;

        var blend = Ps2MaterialPresentation.From(mesh.Materials[stateIndex], true).BlendEquation;
        return blend is not (RuntimeBlendEquation.FixedAlpha or
            RuntimeBlendEquation.AdditiveSourceAlpha or RuntimeBlendEquation.AdditiveFixedAlpha);
    }

    /// <summary>
    /// Admit similarly sized and similarly cutout layers only if most vertices
    /// in the smaller distinct-vertex set also occur in the other layer.
    /// </summary>
    private static bool AreInterleavedLayers(int left, int right,
        IReadOnlyDictionary<int, int> facesByTexture,
        IReadOnlyDictionary<int, HashSet<SourceVertex>> verticesByTexture,
        IReadOnlyDictionary<int, AlphaProfile> alpha)
    {
        if (Math.Min(facesByTexture[left], facesByTexture[right]) <
            Math.Max(facesByTexture[left], facesByTexture[right]) * 0.75 ||
            Math.Abs(alpha[left].CutoutFraction - alpha[right].CutoutFraction) >
                MaximumCutoutProfileDifference)
            return false;

        var smaller = verticesByTexture[left].Count <= verticesByTexture[right].Count
            ? verticesByTexture[left] : verticesByTexture[right];
        var larger = ReferenceEquals(smaller, verticesByTexture[left])
            ? verticesByTexture[right] : verticesByTexture[left];

        int shared = smaller.Count(point => larger.Contains(point));
        return shared >= smaller.Count * MinimumSharedVertexFraction;
    }
}

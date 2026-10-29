using OBP.PS2.Graphics;
using OBP.Runtime.Presentation;

namespace OBP.PS2.Presentation;

/// <summary>
/// Shared PS2 shrub geometry test for intersecting, predominantly cutout leaf
/// layers. The caller owns each source class and its level-local texture table;
/// a coincident texture ID on another class never inherits the decision.
/// </summary>
public static class RcFoliageCutoutPolicy
{
    // Confirmed R&C1 examples include Veldin class 466, Novalis 116,
    // Quartu 399 and Veldin Return 466/510. The thresholds deliberately
    // reject small decor, partially translucent leaves and fixed-alpha blends.
    private const int MinimumLayerFaces = 128;
    private const double MinimumSharedVertexFraction = 0.50;
    private const double MinimumCutoutFraction = 0.20;
    private const double MaximumTranslucentFraction = 0.20;
    private const double MaximumCutoutProfileDifference = 0.08;

    /// <summary>
    /// Opt only the proven (source class, texture) pair into depth-writing
    /// cutout. Other shrub surfaces keep their source blend and mesh grouping.
    /// </summary>
    public static RuntimeMaterialPresentation? WithFoliageCutout(
        int sourceClassId,
        int textureId,
        RuntimeMaterialPresentation? presentation,
        IReadOnlyDictionary<int, AlphaProfile>? alphaProfiles,
        IReadOnlyDictionary<int, HashSet<int>>? cutoutSurfaces)
    {
        if (presentation is not { AlphaBlendEnabled: true })
            return presentation;

        if (alphaProfiles is null || !alphaProfiles.TryGetValue(textureId, out var profile) ||
            cutoutSurfaces is null || !cutoutSurfaces.TryGetValue(sourceClassId, out var selected) ||
            !selected.Contains(textureId) ||
            profile.CutoutFraction < 0.05 ||
            presentation.BlendEquation is RuntimeBlendEquation.FixedAlpha or
                RuntimeBlendEquation.AdditiveSourceAlpha or RuntimeBlendEquation.AdditiveFixedAlpha)
            return presentation;

        return presentation with
        {
            PreferPerPlacementFoliageDepth = true,
            PreferFoliageCutout = true,
        };
    }

    // Small, sparse textured card clumps (grass, weeds and wheat) do not
    // meet the interleaved-layer test above. In retail data their mostly
    // transparent card textures can still sort behind soil when blended.
    // Select by the source's class geometry, native blend equation and
    // decoded alpha, never by game, planet, object ID or texture index.
    private const int MaximumSparseCardFaces = 32;
    private const double MinimumSparseCardCutout = 0.65;
    // Retail Marcadia wheat includes sparse opaque tip texels (as few as 0.2%).
    // Requiring at least some prevents entirely soft/low-alpha textures from
    // becoming invisible when a 0.5 alpha-scissor discards every visible pixel.
    private const double MinimumSparseCardOpaque = 0.001;

    public static HashSet<int> FindFoliageCutoutTextures(
        double[] positions,
        int[] indices,
        int[] triangleTextureIds,
        int[] triangleMaterialStateIndices,
        bool?[] triangleAlphaBlendEnabled,
        IReadOnlyList<RcMaterialState> materials,
        IReadOnlyDictionary<int, AlphaProfile> alpha)
    {
        var selected = FindLayeredCutoutTextures(
            positions, indices, triangleTextureIds, triangleMaterialStateIndices,
            triangleAlphaBlendEnabled, materials, alpha);

        int faceCount = triangleTextureIds.Length;

        // Sparse-card eligibility is per texture group. A class may contain
        // many unrelated faces without making one small foliage texture dense.
        // All faces sharing a texture must use a supported native blend;
        // never turn just one face of a fixed-alpha/additive surface into a
        // different compositing mode through texture-wide grouping.
        foreach (var textureFaces in Enumerable.Range(0, faceCount)
                     .GroupBy(face => triangleTextureIds[face]))
        {
            if (textureFaces.Count() > MaximumSparseCardFaces ||
                !alpha.TryGetValue(textureFaces.Key, out var profile) ||
                profile.CutoutFraction < MinimumSparseCardCutout ||
                profile.OpaqueFraction < MinimumSparseCardOpaque ||
                profile.TranslucentFraction > MaximumTranslucentFraction)
                continue;

            bool supported = textureFaces.All(face =>
            {
                if (triangleAlphaBlendEnabled[face] is not true)
                    return false;

                int index = triangleMaterialStateIndices[face];
                if ((uint)index >= (uint)materials.Count)
                    return false;

                var equation = Ps2MaterialPresentation.From(materials[index], true).BlendEquation;
                return equation is not (RuntimeBlendEquation.FixedAlpha or
                    RuntimeBlendEquation.AdditiveSourceAlpha or RuntimeBlendEquation.AdditiveFixedAlpha);
            });
            if (supported)
                selected.Add(textureFaces.Key);
        }
        return selected;
    }

    private readonly record struct SourceVertex(long X, long Y, long Z);

    public static HashSet<int> FindLayeredCutoutTextures(
        double[] positions,
        int[] indices,
        int[] triangleTextureIds,
        int[] triangleMaterialStateIndices,
        bool?[] triangleAlphaBlendEnabled,
        IReadOnlyList<RcMaterialState> materials,
        IReadOnlyDictionary<int, AlphaProfile> alpha)
    {
        if (indices.Length != triangleTextureIds.Length * 3 ||
            triangleMaterialStateIndices.Length != triangleTextureIds.Length ||
            triangleAlphaBlendEnabled.Length != triangleTextureIds.Length)
        {
            throw new InvalidDataException("PS2 shrub triangle/material arrays disagree.");
        }

        var verticesByTexture = new Dictionary<int, HashSet<SourceVertex>>();
        var facesByTexture = new Dictionary<int, int>();
        for (int face = 0; face < triangleTextureIds.Length; face++)
        {
            int textureId = triangleTextureIds[face];
            if (!alpha.TryGetValue(textureId, out var profile) ||
                profile.CutoutFraction < MinimumCutoutFraction ||
                profile.TranslucentFraction > MaximumTranslucentFraction ||
                triangleAlphaBlendEnabled[face] is not true)
                continue;

            int stateIndex = triangleMaterialStateIndices[face];
            if ((uint)stateIndex >= (uint)materials.Count ||
                Ps2MaterialPresentation.From(materials[stateIndex], true).BlendEquation is
                    RuntimeBlendEquation.FixedAlpha or RuntimeBlendEquation.AdditiveSourceAlpha or
                    RuntimeBlendEquation.AdditiveFixedAlpha)
                continue;

            if (!verticesByTexture.TryGetValue(textureId, out var vertices))
                verticesByTexture[textureId] = vertices = [];
            facesByTexture[textureId] = facesByTexture.GetValueOrDefault(textureId) + 1;

            for (int corner = 0; corner < 3; corner++)
            {
                int index = indices[face * 3 + corner] * 3;
                vertices.Add(new SourceVertex(
                    (long)Math.Round(positions[index] * 100000d),
                    (long)Math.Round(positions[index + 1] * 100000d),
                    (long)Math.Round(positions[index + 2] * 100000d)));
            }
        }

        var candidates = facesByTexture.Keys
            .Where(texture => facesByTexture[texture] >= MinimumLayerFaces)
            .OrderBy(texture => texture).ToArray();
        var admitted = new HashSet<int>();
        for (int a = 0; a < candidates.Length; a++)
        {
            for (int b = a + 1; b < candidates.Length; b++)
            {
                int left = candidates[a], right = candidates[b];
                if (Math.Min(facesByTexture[left], facesByTexture[right]) <
                        Math.Max(facesByTexture[left], facesByTexture[right]) * 0.75 ||
                    Math.Abs(alpha[left].CutoutFraction - alpha[right].CutoutFraction) >
                        MaximumCutoutProfileDifference)
                    continue;

                var smaller = verticesByTexture[left].Count <= verticesByTexture[right].Count
                    ? verticesByTexture[left] : verticesByTexture[right];
                var larger = ReferenceEquals(smaller, verticesByTexture[left])
                    ? verticesByTexture[right] : verticesByTexture[left];
                int shared = smaller.Count(point => larger.Contains(point));
                if (shared < smaller.Count * MinimumSharedVertexFraction)
                    continue;

                admitted.Add(left);
                admitted.Add(right);
            }
        }

        return admitted;
    }
}

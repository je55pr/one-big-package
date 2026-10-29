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

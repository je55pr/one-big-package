using OBP.PS2.Graphics;
using OBP.PS2.Textures;
using OBP.Runtime.Presentation;

namespace OBP.PS2.Presentation;

/// <summary>
/// Conservative host-side appearance correction for low-profile, opaque
/// shrub-derived trim with a strongly top-lit vertical texture gradient.
/// Does not apply to foliage cards, tall geometry, or general shrub textures.
/// This is a presentation heuristic, not a recovered PS2 UV-flip register.
/// </summary>
public static class RcShrubTrimUvPolicy
{
    public static HashSet<int> FindInvertedTrimTextures(
        double[] positions, float[] uvs, int[] indices,
        int[] faceTextures, int[] faceMaterialStates,
        bool?[] faceAlphaBlendEnabled,
        IReadOnlyList<RcMaterialState> materials,
        IReadOnlyDictionary<int, RcLevelTextureTable.Texture> textures)
    {
        var result = new HashSet<int>();
        if (positions.Length < 9 || uvs.Length != positions.Length / 3 * 2 ||
            indices.Length != faceTextures.Length * 3 ||
            faceMaterialStates.Length != faceTextures.Length ||
            faceAlphaBlendEnabled.Length != faceTextures.Length)
            return result;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        for (int v = 0; v < positions.Length; v += 3)
        {
            minX = Math.Min(minX, positions[v]);
            maxX = Math.Max(maxX, positions[v]);
            minY = Math.Min(minY, positions[v + 1]);
            maxY = Math.Max(maxY, positions[v + 1]);
            minZ = Math.Min(minZ, positions[v + 2]);
            maxZ = Math.Max(maxZ, positions[v + 2]);
        }

        // Native shrub class positions are Z-up. The trim is an almost-flat
        // horizontal outline, unlike tree trunks, rocks and vertical cards.
        double height = maxZ - minZ;
        if (maxX - minX < 3 || maxY - minY < 3 || height is < 0.15 or > 0.8)
            return result;

        foreach (var faces in Enumerable.Range(0, faceTextures.Length)
                     .GroupBy(f => faceTextures[f]))
        {
            if (faces.Count() < 32 ||
                !textures.TryGetValue(faces.Key, out var texture) ||
                !IsTopLitOpaqueBand(texture))
                continue;

            int directionalEdges = 0, reversedEdges = 0;
            bool repeat = true;
            foreach (int f in faces)
            {
                int state = faceMaterialStates[f];
                if ((uint)state >= (uint)materials.Count ||
                    materials[state].WrapT != RcTextureWrapHint.Repeat ||
                    Ps2MaterialPresentation.From(materials[state], faceAlphaBlendEnabled[f])
                        .BlendEquation is RuntimeBlendEquation.FixedAlpha or
                            RuntimeBlendEquation.AdditiveSourceAlpha or
                            RuntimeBlendEquation.AdditiveFixedAlpha)
                {
                    repeat = false;
                    break;
                }

                for (int a = 0; a < 3; a++)
                    for (int b = a + 1; b < 3; b++)
                    {
                        int va = indices[3 * f + a], vb = indices[3 * f + b];
                        if ((uint)va >= (uint)(positions.Length / 3) ||
                            (uint)vb >= (uint)(positions.Length / 3))
                        {
                            repeat = false;
                            break;
                        }

                        double dz = positions[3 * vb + 2] - positions[3 * va + 2];
                        double dt = uvs[2 * vb + 1] - uvs[2 * va + 1];
                        if (Math.Abs(dz) <= 0.04 || Math.Abs(dt) <= 0.04)
                            continue;
                        directionalEdges++;
                        if (dz * dt < 0)
                            reversedEdges++;
                    }
                if (!repeat)
                    break;
            }

            // The brighter top rows of the texture currently map down toward
            // the bottom of the bevel. Correct only a strong, consistent case.
            if (repeat && directionalEdges >= 20 &&
                reversedEdges >= directionalEdges * 0.95)
                result.Add(faces.Key);
        }
        return result;
    }

    private static bool IsTopLitOpaqueBand(RcLevelTextureTable.Texture texture)
    {
        int width = texture.Width, height = texture.Height;
        if (width < 8 || height < 8 || texture.Rgba.Length != width * height * 4)
            return false;

        var rows = new double[height];
        double within = 0;
        for (int y = 0; y < height; y++)
        {
            double mean = 0;
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (texture.Rgba[offset + 3] != 255)
                    return false;
                mean += Luma(texture.Rgba, offset);
            }
            rows[y] = mean / width;
            for (int x = 0; x < width; x++)
            {
                double diff = Luma(texture.Rgba, (y * width + x) * 4) - rows[y];
                within += diff * diff;
            }
        }
        int margin = Math.Max(1, height / 8);
        double top = rows.Take(margin).Average();
        double bottom = rows.Skip(height - margin).Average();
        double average = rows.Average();
        double rowSpread = Math.Sqrt(rows.Sum(v => (v - average) * (v - average)) / height);
        double withinSpread = Math.Sqrt(within / (width * height));

        return top - bottom >= 0.20 && rowSpread >= 8 * (withinSpread + 0.001);
    }

    private static double Luma(byte[] bytes, int offset) =>
        (bytes[offset] * 0.2126 + bytes[offset + 1] * 0.7152 +
         bytes[offset + 2] * 0.0722) / 255.0;
}

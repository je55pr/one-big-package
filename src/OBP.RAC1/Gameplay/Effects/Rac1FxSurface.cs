using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>GS alpha register presets the recovered effects use: 0x44 = (Cs-Cd)*As+Cd, 0x48 = Cs*As+Cd.</summary>
public enum Rac1FxBlend : byte { Alpha = 0x44, Additive = 0x48 }

/// <summary>
/// Already interpreted triangle geometry in native Z-up space, with one Fx-bank texture and one blend.
/// Colours are RGBA bytes packed R | G&lt;&lt;8 | B&lt;&lt;16 | A&lt;&lt;24 in the PS2 range (0x80 = 1.0).
/// Hosts draw it; no source rules live in the renderer.
/// </summary>
public sealed record Rac1FxSurface(int Texture, Rac1FxBlend Blend, Vector3[] Positions, Vector2[] Uvs, uint[] Colors, int[] Indices)
{
    /// <summary>A GS quad drawn as a strip: triangles (0,1,2) and (1,2,3).</summary>
    public static Rac1FxSurface Quad(int texture, Rac1FxBlend blend, Vector3[] positions, Vector2[] uvs, uint[] colors) =>
        new(texture, blend, positions, uvs, colors, [0, 1, 2, 1, 2, 3]);

    /// <summary>A GS triangle strip over <paramref name="count"/> vertices starting at <paramref name="start"/> of the tables.</summary>
    public static Rac1FxSurface Strip(int texture, Rac1FxBlend blend, Vector3[] positions, Vector2[] uvs, uint[] colors)
    {
        var indices = new List<int>();
        for (int i = 0; i + 2 < positions.Length; i++)
            // Strip winding alternates; the renderer is double sided so only the vertex set matters.
            indices.AddRange([i, i + 1, i + 2]);
        return new(texture, blend, positions, uvs, colors, [.. indices]);
    }
}

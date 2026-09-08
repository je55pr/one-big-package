using OBP.PS2.Geometry;
using OBP.RAC2.Level;

namespace OBP.RAC2.Geometry;

/// <summary>
/// Going Commando compatibility facade over the retail-validated shared
/// <see cref="RcSky"/> inner sky codec. GC's level-core container remains
/// game-specific; this facade preserves the existing RAC2 API and tests.
/// </summary>
public static class GcSky
{
    public const int HeaderSize = RcSky.HeaderSize;
    public const int ClusterHeaderSize = RcSky.ClusterHeaderSize;
    public const int TextureEntrySize = RcSky.TextureEntrySize;

    public sealed record Shell(
        bool Textured,
        double[] Positions,
        float[] Uvs,
        float[] Alpha,
        int[] Indices,
        int[] TriangleTextureIds);

    public sealed record SkyTexture(int Index, int Width, int Height, byte[] Rgba);

    public sealed record Sky(
        (double R, double G, double B, double A) Colour,
        bool ClearScreen,
        int MaximumSpriteCount,
        IReadOnlyList<Shell> Shells,
        IReadOnlyList<SkyTexture> Textures);

    public static Sky Read(byte[] bytes)
    {
        var sky = RcSky.Read(bytes);
        return new Sky(
            sky.Colour,
            sky.ClearScreen,
            sky.MaximumSpriteCount,
            sky.Shells.Select(s => new Shell(
                s.Textured,
                s.Positions,
                s.Uvs,
                s.Alpha,
                s.Indices,
                s.TriangleTextureIds)).ToList(),
            sky.Textures.Select(t => new SkyTexture(
                t.Index,
                t.Width,
                t.Height,
                t.Rgba)).ToList());
    }

    /// <summary>Read the sky for a GC level core, or null when absent.</summary>
    public static Sky? ReadLevelSky(GcLevelCore.Core core)
    {
        if (core.Header.Sky <= 0)
        {
            return null;
        }
        var range = GcLevelCore.SectionRange(core, core.Header.Sky);
        if (range is not { } r)
        {
            return null;
        }

        return Read(core.Assets.AsSpan(r.Offset, r.Size).ToArray());
    }
}

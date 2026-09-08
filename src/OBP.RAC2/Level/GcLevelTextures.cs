using OBP.PS2.Textures;

namespace OBP.RAC2.Level;

/// <summary>
/// Going Commando projection of the shared RC level texture-entry codec. The GC
/// core header still owns which ArrayRange represents tfrags, Mobies, TIEs,
/// shrubs, parts or effects; only the independently validated 16-byte entry and
/// IDTEX8/GS-RAM decode live in <see cref="RcLevelTextureTable"/>.
/// </summary>
public static class GcLevelTextures
{
    public const int TextureEntrySize = RcLevelTextureTable.TextureEntrySize;

    public enum Table { Tfrag, Moby, Tie, Shrub, Part, Fx }

    public sealed record Texture(int Index, int Width, int Height, int Type, int PaletteSlot, int DataOffset, byte[] Rgba);

    public static List<Texture> Read(GcLevelCore.Core core, Table table = Table.Tfrag, int maxDimension = 1024)
    {
        var range = table switch
        {
            Table.Tfrag => core.Header.TfragTextures,
            Table.Moby => core.Header.MobyTextures,
            Table.Tie => core.Header.TieTextures,
            Table.Shrub => core.Header.ShrubTextures,
            Table.Part => core.Header.PartTextures,
            Table.Fx => core.Header.FxTextures,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        var decoded = RcLevelTextureTable.Read(
            core.Index,
            core.Assets,
            core.GsRam,
            core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(range.Count, range.Offset),
            new RcLevelTextureTable.Options(maxDimension, Strict: false));

        return decoded.Select(t => new Texture(
            t.Index, t.Width, t.Height, t.Type, t.PaletteSlot, t.DataOffset, t.Rgba)).ToList();
    }
}

using System.Buffers.Binary;
using OBP.PS2.Textures;
using OBP.Runtime;

namespace OBP.RAC3.Level;

/// <summary>
/// Native level Fx bank: loader 0x00428FE8 / 0x004225E8, lookup 0x004020D8.
/// Palette/pixels are offsets into the independent bank at core-index +0x68.
/// </summary>
public static class Rac3FxTextures
{
    public const string AssetKind = "rac3-fx";

    public static IReadOnlyDictionary<int, RuntimeTexture> Read(Rac3LevelCore.Core core, IReadOnlySet<int> ids)
    {
        var table = core.Header.FxTextures;
        if (core.Index.Length < 0x6c || table.Count < 0 || table.Offset < Rac3LevelCore.CoreHeaderSize ||
            (long)table.Offset + table.Count * 16L > core.Index.Length)
            throw new InvalidDataException("UYA Fx directory is outside the core index.");
        int bank = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x68));
        var output = new Dictionary<int, RuntimeTexture>();
        foreach (int id in ids.Order())
        {
            if ((uint)id >= table.Count) throw new InvalidDataException("UYA Fx texture is outside its table.");
            int at = table.Offset + id * 16;
            int palette = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int pixels = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            int width = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 8));
            int height = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 12));
            // Negative width selects PSMT4 in the original loader. This slice
            // only admits the original Cannon's observed PSMT8 entries.
            if (width < 0) throw new NotSupportedException("UYA PSMT4 Fx texture is outside this slice.");
            long pixelAt = (long)bank + pixels, paletteAt = (long)bank + palette;
            if (bank < 0 || palette < 0 || pixels < 0 || width is < 1 or > 1024 || height is < 1 or > 1024 ||
                pixelAt + (long)width * height > core.Assets.Length || paletteAt + 1024 > core.Assets.Length)
                throw new InvalidDataException("UYA Fx pixels/palette exceed the independent bank.");
            uint[] colors = Enumerable.Range(0, 256).Select(i =>
                BinaryPrimitives.ReadUInt32LittleEndian(core.Assets.AsSpan((int)paletteAt + i * 4))).ToArray();
            var texture = Ps2Texture.DecodePaletted8(new(width, height,
                core.Assets.AsMemory((int)pixelAt, width * height), colors));
            output.Add(id, new(AssetKind, id, width, height, texture.Rgba));
        }
        return output;
    }
}

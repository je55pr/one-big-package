using System.Buffers.Binary;
using OBP.PS2.Textures;
using OBP.Runtime;

namespace OBP.RAC1.Level;

/// <summary>Native per-level Fx bank used by both bitmap fonts and world effects.</summary>
public static class Rac1FxTextures
{
    // Loader 0x23f768: palette offset, pixel offset, width, height; both offsets
    // refer to the independent bank at core+68. This differs from Part records.
    public static IReadOnlyDictionary<int, RuntimeTexture> Read(Rac1LevelCore.Core core, IReadOnlySet<int> ids)
    {
        int bank = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x68));
        var result = new Dictionary<int, RuntimeTexture>();
        foreach (int id in ids.Order())
        {
            if ((uint)id >= core.Header.FxTextures.Count) throw new InvalidDataException("R&C1 Fx texture index is outside its table.");
            var row = core.Index.AsSpan(core.Header.FxTextures.Offset + id * 16, 16);
            int palette = BinaryPrimitives.ReadInt32LittleEndian(row);
            int pixels = BinaryPrimitives.ReadInt32LittleEndian(row[4..]);
            int width = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
            long pixelAt = (long)bank + pixels, paletteAt = (long)bank + palette;
            if (bank < 0 || pixels < 0 || palette < 0 || width is < 1 or > 1024 || height is < 1 or > 1024 ||
                pixelAt < 0 || paletteAt < 0 || pixelAt + (long)width * height > core.Assets.Length || paletteAt + 1024 > core.Assets.Length)
                throw new InvalidDataException("R&C1 Fx texture/palette exceeds its independent asset bank.");
            uint[] colors = Enumerable.Range(0, 256).Select(index =>
                BinaryPrimitives.ReadUInt32LittleEndian(core.Assets.AsSpan((int)paletteAt + index * 4, 4))).ToArray();
            var decoded = Ps2Texture.DecodePaletted8(new(width, height,
                core.Assets.AsMemory((int)pixelAt, width * height), colors));
            result.Add(id, new("fx", id, width, height, decoded.Rgba));
        }
        return result;
    }
}

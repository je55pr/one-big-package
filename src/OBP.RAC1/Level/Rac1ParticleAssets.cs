using System.Buffers.Binary;
using OBP.PS2.Textures;
using OBP.Runtime;

namespace OBP.RAC1.Level;

/// <summary>
/// Core+6c particle texture lookup, relocated by 0x23f630. Pool constructors
/// read the first byte of their type's entry as the PartTextures index. The
/// remaining entry bytes are not assigned unproven animation semantics here.
/// Part pixels and palettes use core+64, independently of Moby texture data.
/// </summary>
public static class Rac1ParticleAssets
{
    public static int InitialTexture(Rac1LevelCore.Core core, int poolType)
    {
        int at = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x6c));
        if (at < Rac1LevelCore.HeaderSize || at > core.Index.Length - 16)
            throw new InvalidDataException("R&C1 particle lookup exceeds the core index.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
        int data = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 8));
        int size = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 12));
        if (count is < 0 or > 256 || (uint)poolType >= count ||
            (long)at + 16 + count * 4L > core.Index.Length || data < 16 + count * 4 ||
            size < 1 || (long)at + data + size > core.Index.Length)
            throw new InvalidDataException("R&C1 particle lookup has invalid bounds.");
        int offset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 16 + poolType * 4));
        if (offset == 0) return 0; // Native loader binds absent entries to its zero-filled fallback.
        if (offset < data || offset >= (long)data + size)
            throw new InvalidDataException("Particle type points outside its texture lookup data.");
        int texture = core.Index[at + offset];
        if (texture >= core.Header.PartTextures.Count)
            throw new InvalidDataException("Particle type references an absent texture.");
        return texture;
    }

    public static IReadOnlyList<RuntimeTexture> ReadTextures(Rac1LevelCore.Core core, IReadOnlySet<int> ids)
    {
        int pixelBase = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x64));
        var result = new List<RuntimeTexture>();
        // 23f6d0..23f728 reads palette offset, GS flags, pixel offset and square
        // dimension. 1feff8 derives TEX0's log2 dimension with PLZCW. This is
        // NOT the ordinary Moby TextureEntry grammar; palettes are in assets.
        foreach (int id in ids.Order())
        {
            if ((uint)id >= core.Header.PartTextures.Count) throw new InvalidDataException("Particle texture index is outside its table.");
            var row = core.Index.AsSpan(core.Header.PartTextures.Offset + id * 16, 16);
            int palette = BinaryPrimitives.ReadInt32LittleEndian(row);
            int pixels = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            int dimension = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
            long pixelStart = (long)pixelBase + pixels, paletteStart = (long)pixelBase + palette;
            if (pixelBase < 0 || pixels < 0 || palette < 0 || dimension is < 1 or > 1024 ||
                (dimension & (dimension - 1)) != 0 || pixelStart < 0 || paletteStart < 0 ||
                pixelStart + (long)dimension * dimension > core.Assets.Length || paletteStart + 1024 > core.Assets.Length)
                throw new InvalidDataException("R&C1 particle texture/palette exceeds core assets.");
            uint[] colors = Enumerable.Range(0, 256).Select(index =>
                BinaryPrimitives.ReadUInt32LittleEndian(core.Assets.AsSpan((int)paletteStart + index * 4, 4))).ToArray();
            var decoded = Ps2Texture.DecodePaletted8(new(dimension, dimension,
                core.Assets.AsMemory((int)pixelStart, dimension * dimension), colors));
            result.Add(new("part", id, dimension, dimension, decoded.Rgba));
        }
        return result;
    }
}

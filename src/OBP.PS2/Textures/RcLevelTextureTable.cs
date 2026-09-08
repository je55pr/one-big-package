using System.Buffers.Binary;

namespace OBP.PS2.Textures;

/// <summary>
/// Shared 16-byte level <c>TextureEntry</c> table used by the PS2 Ratchet &amp;
/// Clank line where retail archaeology has independently established the same
/// indexed-pixel + GS-RAM palette representation. Enclosing table locations and
/// texture-family meanings remain game-specific.
/// </summary>
public static class RcLevelTextureTable
{
    public const int TextureEntrySize = 16;

    public readonly record struct Range(int Count, int Offset);

    public sealed record Texture(
        int Index,
        int Width,
        int Height,
        int Type,
        int PaletteSlot,
        int DataOffset,
        byte[] Rgba);

    public sealed record Options(int MaxDimension = 1024, bool Strict = true);

    public static List<Texture> Read(
        byte[] index,
        byte[] assets,
        byte[] gsRam,
        int texturesBaseOffset,
        Range range,
        Options? options = null)
    {
        options ??= new Options();
        if (range.Count < 0 || range.Offset < 0 ||
            (long)range.Offset + (long)range.Count * TextureEntrySize > index.Length)
        {
            throw new InvalidDataException(
                $"RC texture table {range.Offset}+{range.Count}x{TextureEntrySize} lies outside the {index.Length}-byte core index.");
        }
        if (texturesBaseOffset < 0 || texturesBaseOffset > assets.Length)
        {
            throw new InvalidDataException($"RC texture pixel base {texturesBaseOffset} lies outside the {assets.Length}-byte asset blob.");
        }

        var outp = new List<Texture>(range.Count);
        for (int i = 0; i < range.Count; i++)
        {
            int at = range.Offset + i * TextureEntrySize;
            int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
            int width = BinaryPrimitives.ReadInt16LittleEndian(index.AsSpan(at + 4));
            int height = BinaryPrimitives.ReadInt16LittleEndian(index.AsSpan(at + 6));
            int type = BinaryPrimitives.ReadInt16LittleEndian(index.AsSpan(at + 8));
            int paletteSlot = BinaryPrimitives.ReadInt16LittleEndian(index.AsSpan(at + 10));

            bool validDimensions = width > 0 && height > 0 && width <= options.MaxDimension && height <= options.MaxDimension;
            long pixelStartLong = (long)texturesBaseOffset + dataOffset;
            long pixelBytes = (long)width * height;
            bool validPixels = validDimensions && dataOffset >= 0 && pixelStartLong >= 0 &&
                               pixelStartLong <= assets.Length && pixelBytes <= assets.Length - pixelStartLong;
            long paletteStartLong = (long)paletteSlot * 0x100;
            bool validPalette = paletteSlot >= 0 && paletteStartLong >= 0 &&
                                paletteStartLong <= gsRam.Length && 256L * 4 <= gsRam.Length - paletteStartLong;

            if (!validDimensions || !validPixels || !validPalette)
            {
                if (options.Strict)
                {
                    throw new InvalidDataException(
                        $"RC texture entry {i} is invalid: {width}x{height}, dataOffset={dataOffset}, paletteSlot={paletteSlot}.");
                }
                continue;
            }

            int pixelStart = checked((int)pixelStartLong);
            int paletteStart = checked((int)paletteStartLong);
            var palette = new uint[256];
            for (int k = 0; k < 256; k++)
            {
                palette[k] = BinaryPrimitives.ReadUInt32LittleEndian(gsRam.AsSpan(paletteStart + k * 4));
            }

            try
            {
                var decoded = Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(
                    width, height, assets.AsMemory(pixelStart, checked(width * height)), palette));
                outp.Add(new Texture(i, width, height, type, paletteSlot, dataOffset, decoded.Rgba));
            }
            catch (Exception ex) when (!options.Strict && ex is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
            {
                // Preserve the historical permissive GC caller behaviour while
                // allowing evidence-backed callers such as R&C1 to fail loudly.
            }
        }

        if (options.Strict && outp.Count != range.Count)
        {
            throw new InvalidDataException($"RC texture table declares {range.Count} entries but decoded {outp.Count}.");
        }
        return outp;
    }
}

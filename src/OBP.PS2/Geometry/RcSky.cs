using System.Buffers.Binary;
using OBP.PS2.Textures;


namespace OBP.PS2.Geometry;

/// <summary>
/// Shared PS2 Ratchet &amp; Clank sky-shell codec. R&amp;C1 and Going Commando
/// retail archaeology independently validate this same inner binary grammar;
/// their outer level/core containers remain game-specific. Shell vertices are
/// s16 / 1024, ST is s16 / 4096, native alpha is preserved, and each triangle
/// carries its native texture id. The runtime redraws shells camera-centred.
/// See research/RAC1_SKY.md and research/GC_SKY.md.
/// </summary>
public static class RcSky
{
    public const int HeaderSize = 0x40;
    public const int ClusterHeaderSize = 0x20;
    public const int TextureEntrySize = 0x10;

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

    private static List<SkyTexture> ReadTextures(byte[] bytes, int defsOffset, int dataOffset, int count)
    {
        var outp = new List<SkyTexture>();
        for (int i = 0; i < count; i++)
        {
            int at = defsOffset + i * TextureEntrySize;
            if (at < 0 || at + TextureEntrySize > bytes.Length)
            {
                break;
            }

            int paletteOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 0x00));
            int textureOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 0x04));
            int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 0x08));
            int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 0x0c));
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
            {
                continue;
            }

            int pixelStart = dataOffset + textureOffset;
            int paletteStart = dataOffset + paletteOffset;
            if (pixelStart < 0 || paletteStart < 0 ||
                pixelStart + width * height > bytes.Length || paletteStart + 256 * 4 > bytes.Length)
            {
                continue;
            }

            var palette = new uint[256];
            for (int p = 0; p < 256; p++)
            {
                palette[p] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(paletteStart + p * 4));
            }

            Ps2Texture.Decoded decoded;
            try
            {
                decoded = Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(
                    width, height, bytes.AsMemory(pixelStart, width * height), palette));
            }
            catch
            {
                continue;
            }

            outp.Add(new SkyTexture(i, width, height, decoded.Rgba));
        }

        return outp;
    }

    private static Shell? ReadShell(byte[] bytes, int shellOffset)
    {
        if (shellOffset <= 0 || shellOffset + 0x10 > bytes.Length)
        {
            return null;
        }

        int clusterCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(shellOffset + 0x00));
        int flags = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(shellOffset + 0x04));
        if (clusterCount is < 0 or > 4096)
        {
            return null;
        }

        bool textured = (flags & 1) == 0;

        var positions = new List<double>();
        var uvs = new List<float>();
        var alpha = new List<float>();
        var indices = new List<int>();
        var triangleTextureIds = new List<int>();

        for (int c = 0; c < clusterCount; c++)
        {
            int ch = shellOffset + 0x10 + c * ClusterHeaderSize;
            if (ch + ClusterHeaderSize > bytes.Length)
            {
                break;
            }

            int data = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(ch + 0x10));
            int vertexCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(ch + 0x14));
            int triCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(ch + 0x16));
            int vertexOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ch + 0x18));
            int stOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ch + 0x1a));
            int triOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ch + 0x1c));
            if (vertexCount <= 0 || triCount < 0 || data <= 0)
            {
                continue;
            }

            int vbase = data + vertexOffset;
            int sbase = data + stOffset;
            int fbase = data + triOffset;
            if (vbase + vertexCount * 8 > bytes.Length ||
                sbase + vertexCount * 4 > bytes.Length ||
                fbase + triCount * 4 > bytes.Length)
            {
                continue;
            }

            int baseV = positions.Count / 3;
            for (int v = 0; v < vertexCount; v++)
            {
                positions.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(vbase + v * 8 + 0)) / 1024.0);
                positions.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(vbase + v * 8 + 2)) / 1024.0);
                positions.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(vbase + v * 8 + 4)) / 1024.0);
                int a = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(vbase + v * 8 + 6));
                alpha.Add(a == 0x80 ? 1f : System.Math.Clamp(a * 2f / 255f, 0f, 1f));
                uvs.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(sbase + v * 4 + 0)) / 4096f);
                uvs.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(sbase + v * 4 + 2)) / 4096f);
            }

            for (int f = 0; f < triCount; f++)
            {
                int i0 = bytes[fbase + f * 4 + 0];
                int i1 = bytes[fbase + f * 4 + 1];
                int i2 = bytes[fbase + f * 4 + 2];
                int tex = bytes[fbase + f * 4 + 3];
                if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
                {
                    continue;
                }

                // Reverse the winding order (matches the retail render).
                indices.Add(baseV + i2);
                indices.Add(baseV + i1);
                indices.Add(baseV + i0);
                triangleTextureIds.Add(tex == 0xff ? -1 : tex);
            }
        }

        return new Shell(
            textured,
            positions.ToArray(),
            uvs.ToArray(),
            alpha.ToArray(),
            indices.ToArray(),
            triangleTextureIds.ToArray());
    }

    public static Sky Read(byte[] bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            throw new InvalidDataException($"RC sky: buffer is {bytes.Length} bytes, shorter than the 0x40 header.");
        }

        byte r = bytes[0], g = bytes[1], b = bytes[2], a = bytes[3];
        var colour = (r / 255.0, g / 255.0, b / 255.0, a == 0x80 ? 1.0 : a / 128.0);
        bool clearScreen = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x04)) != 0;
        int shellCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x06));
        int maximumSpriteCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x0a));
        int textureCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x0c));
        int textureDefs = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10));
        int textureData = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x14));
        if (shellCount is < 0 or > 8)
        {
            throw new InvalidDataException($"RC sky: implausible shell count {shellCount}.");
        }

        var textures = textureCount is > 0 and < 1024 && textureDefs > 0 && textureData > 0
            ? ReadTextures(bytes, textureDefs, textureData, textureCount)
            : [];

        var shells = new List<Shell>();
        for (int i = 0; i < shellCount; i++)
        {
            int shellOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x20 + i * 4));
            var shell = ReadShell(bytes, shellOffset);
            if (shell is not null && shell.Indices.Length > 0)
            {
                shells.Add(shell);
            }
        }

        return new Sky(colour, clearScreen, maximumSpriteCount, shells, textures);
    }

}

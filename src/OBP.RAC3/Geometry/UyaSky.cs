using System.Buffers.Binary;
using OBP.PS2.Textures;

namespace OBP.RAC3.Geometry;

/// <summary>Strict retail-corroborated UYA/DL-family sky-shell decoder.</summary>
public static class UyaSky
{
    public const int HeaderSize = 0x40, ShellHeaderSize = 0x10, ClusterHeaderSize = 0x20, TextureEntrySize = 0x10;
    public sealed record Texture(int Index, int Width, int Height, byte[] Rgba);
    public sealed record Shell(bool Textured, bool Bloom, short[] RotationRaw, short[] AngularVelocityRaw,
        double[] Positions, float[] Uvs, float[] Alpha, int[] Indices, int[] TriangleTextureIds, int ClusterCount);
    public sealed record Sky((double R, double G, double B, double A) Colour, bool ClearScreen, int SpriteCount,
        int MaximumSpriteCount, int FxCount, IReadOnlyList<Shell> Shells, IReadOnlyList<Texture> Textures);

    public static Sky Read(byte[] bytes)
    {
        Require(bytes, 0, HeaderSize, "header");
        int shellCount = I16(bytes, 0x06), spriteCount = I16(bytes, 0x08), maxSprite = I16(bytes, 0x0a), textureCount = I16(bytes, 0x0c), fxCount = I16(bytes, 0x0e);
        int defs = I32(bytes, 0x10), data = I32(bytes, 0x14);
        if (shellCount < 0 || shellCount > 8 || textureCount < 0 || textureCount > 1024 || fxCount < 0 || fxCount > textureCount || spriteCount < 0 || maxSprite < 0)
            throw new InvalidDataException("UYA sky header contains implausible counts.");
        var textures = ReadTextures(bytes, defs, data, textureCount);
        var shells = new List<Shell>(shellCount);
        for (int i = 0; i < shellCount; i++)
        {
            int offset = I32(bytes, 0x20 + i * 4);
            if (offset <= 0) throw new InvalidDataException($"UYA sky shell {i} has invalid offset {offset}.");
            shells.Add(ReadShell(bytes, offset, textureCount));
        }
        byte r = bytes[0], g = bytes[1], b = bytes[2], a = bytes[3];
        return new Sky((r / 255.0, g / 255.0, b / 255.0, a == 0x80 ? 1 : a / 128.0), I16(bytes, 0x04) != 0, spriteCount, maxSprite, fxCount, shells, textures);
    }

    private static List<Texture> ReadTextures(byte[] bytes, int defs, int data, int count)
    {
        if (count == 0) return [];
        Require(bytes, defs, checked(count * TextureEntrySize), "texture definitions");
        var result = new List<Texture>(count);
        for (int i = 0; i < count; i++)
        {
            int at = defs + i * TextureEntrySize, paletteOffset = I32(bytes, at), textureOffset = I32(bytes, at + 4), w = I32(bytes, at + 8), h = I32(bytes, at + 12);
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024) throw new InvalidDataException($"UYA sky texture {i} has invalid dimensions {w}x{h}.");
            int pixels = checked(data + textureOffset), paletteAt = checked(data + paletteOffset);
            Require(bytes, pixels, checked(w * h), $"texture {i} pixels"); Require(bytes, paletteAt, 1024, $"texture {i} palette");
            var palette = new uint[256];
            for (int p = 0; p < 256; p++) palette[p] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(paletteAt + p * 4));
            var decoded = Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(w, h, bytes.AsMemory(pixels, w * h), palette));
            result.Add(new Texture(i, w, h, decoded.Rgba));
        }
        return result;
    }

    private static Shell ReadShell(byte[] bytes, int shellAt, int textureCount)
    {
        Require(bytes, shellAt, ShellHeaderSize, "shell header");
        int clusters = I16(bytes, shellAt), flags = I16(bytes, shellAt + 2);
        if (clusters < 0 || clusters > 4096) throw new InvalidDataException($"UYA sky shell has implausible cluster count {clusters}.");
        Require(bytes, shellAt + 0x10, checked(clusters * ClusterHeaderSize), "cluster table");
        short[] rotation = [I16s(bytes, shellAt + 4), I16s(bytes, shellAt + 6), I16s(bytes, shellAt + 8)];
        short[] velocity = [I16s(bytes, shellAt + 0xa), I16s(bytes, shellAt + 0xc), I16s(bytes, shellAt + 0xe)];
        var pos = new List<double>(); var uv = new List<float>(); var alpha = new List<float>(); var indices = new List<int>(); var triTex = new List<int>();
        for (int c = 0; c < clusters; c++)
        {
            int ch = shellAt + 0x10 + c * ClusterHeaderSize, d = I32(bytes, ch + 0x10), vc = I16(bytes, ch + 0x14), tc = I16(bytes, ch + 0x16);
            int vo = I16(bytes, ch + 0x18), so = I16(bytes, ch + 0x1a), to = I16(bytes, ch + 0x1c);
            if (vc < 0 || tc < 0 || vo < 0 || so < 0 || to < 0) throw new InvalidDataException($"UYA sky cluster {c} has invalid counts/offsets.");
            Require(bytes, checked(d + vo), checked(vc * 8), $"cluster {c} vertices"); Require(bytes, checked(d + so), checked(vc * 4), $"cluster {c} UVs"); Require(bytes, checked(d + to), checked(tc * 4), $"cluster {c} faces");
            int baseV = pos.Count / 3;
            for (int v = 0; v < vc; v++)
            {
                int va = d + vo + v * 8, ta = d + so + v * 4;
                pos.Add(I16s(bytes, va) / 1024.0); pos.Add(I16s(bytes, va + 2) / 1024.0); pos.Add(I16s(bytes, va + 4) / 1024.0);
                int a = I16s(bytes, va + 6); alpha.Add(a == 0x80 ? 1f : Math.Clamp(a * 2f / 255f, 0f, 1f));
                uv.Add(I16s(bytes, ta) / 4096f); uv.Add(I16s(bytes, ta + 2) / 4096f);
            }
            for (int f = 0; f < tc; f++)
            {
                int fa = d + to + f * 4, i0 = bytes[fa], i1 = bytes[fa + 1], i2 = bytes[fa + 2], tex = bytes[fa + 3];
                if (i0 >= vc || i1 >= vc || i2 >= vc) throw new InvalidDataException($"UYA sky cluster {c} face {f} references an invalid vertex.");
                if (tex != 0xff && tex >= textureCount) throw new InvalidDataException($"UYA sky cluster {c} face {f} references texture {tex} outside {textureCount}.");
                indices.Add(baseV + i2); indices.Add(baseV + i1); indices.Add(baseV + i0); triTex.Add(tex == 0xff ? -1 : tex);
            }
        }
        return new Shell((flags & 1) == 0, (flags & 2) != 0, rotation, velocity, pos.ToArray(), uv.ToArray(), alpha.ToArray(), indices.ToArray(), triTex.ToArray(), clusters);
    }

    private static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    private static int I16(byte[] b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at));
    private static short I16s(byte[] b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at));
    private static void Require(byte[] bytes, int offset, int size, string label)
    {
        if (offset < 0 || size < 0 || offset > bytes.Length || size > bytes.Length - offset) throw new InvalidDataException($"UYA sky {label} range {offset}+{size} lies outside {bytes.Length} bytes.");
    }
}

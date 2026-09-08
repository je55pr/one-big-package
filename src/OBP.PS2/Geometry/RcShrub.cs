using System.Buffers.Binary;
using OBP.PS2.Vif;

namespace OBP.PS2.Geometry;

/// <summary>
/// Going Commando shrub class geometry — the small instanced foliage. A shrub
/// class is a header then <c>packetCount</c> VIF command lists (header UNPACK
/// with GIF-tag + AD-GIF tables, then two parallel per-vertex arrays). A
/// GS-packet walk emits triangle-list / -strip primitives. See the game-specific
/// research notes for the retail compatibility evidence.
/// </summary>
public static class RcShrub
{
    public const int ClassHeaderSize = 0x40;
    public const int ClassEntrySize = 0x30;

    public sealed record Mesh(double[] Positions, float[] Uvs, int[] Indices, int[] TriangleMaterialSlots, float Scale);


    private readonly record struct SVtx(int X, int Y, int Z, int Ofs, int S, int T);

    private sealed class SPrim
    {
        public int Material;
        public bool Strip;
        public readonly List<SVtx> Verts = [];
    }

    public static Mesh ReadClass(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < ClassHeaderSize)
        {
            throw new InvalidDataException("Shrub class buffer shorter than the 0x40 header.");
        }

        float scale = BinaryPrimitives.ReadSingleLittleEndian(buf[0x20..]);
        int packetCount = BinaryPrimitives.ReadInt16LittleEndian(buf[0x28..]);
        var primitives = new List<SPrim>();

        for (int p = 0; p < packetCount; p++)
        {
            int entryAt = ClassHeaderSize + p * 8;
            if (entryAt + 8 > buf.Length)
            {
                break;
            }

            int offset = BinaryPrimitives.ReadInt32LittleEndian(buf[entryAt..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(buf[(entryAt + 4)..]);
            if (offset <= 0 || size <= 0 || offset + size > buf.Length)
            {
                continue;
            }

            var unpacks = Vif.FilterUnpacks(Vif.ReadCommandList(new ArraySegment<byte>(buf.Slice(offset, size).ToArray())));
            if (unpacks.Count < 3)
            {
                continue;
            }

            var hd = unpacks[0].Data;
            int textureCount = BinaryPrimitives.ReadInt32LittleEndian(hd.AsSpan(0));
            int gifTagCount = BinaryPrimitives.ReadInt32LittleEndian(hd.AsSpan(4));
            int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(hd.AsSpan(8));
            if (vertexCount is < 0 or > 50_000 || gifTagCount is < 0 or > 4096 || textureCount is < 0 or > 256)
            {
                continue;
            }

            var gifTags = new List<(bool PrimStrip, int Ofs)>();
            for (int g = 0; g < gifTagCount; g++)
            {
                int at = 0x10 + g * 0x10;
                if (at + 0x10 > hd.Count)
                {
                    break;
                }

                int primType = (int)((BinaryPrimitives.ReadUInt32LittleEndian(hd.AsSpan(at + 4)) >> 15) & 0b111);
                gifTags.Add((primType == 0b100, BinaryPrimitives.ReadInt32LittleEndian(hd.AsSpan(at + 0x0c))));
            }

            var adGifs = new List<(int Ofs, int TexId)>();
            int adGifBase = 0x10 + gifTagCount * 0x10;
            for (int a = 0; a < textureCount; a++)
            {
                int at = adGifBase + a * 0x40;
                if (at + 0x34 > hd.Count)
                {
                    break;
                }

                adGifs.Add((BinaryPrimitives.ReadInt32LittleEndian(hd.AsSpan(at + 0x0c)), BinaryPrimitives.ReadInt32LittleEndian(hd.AsSpan(at + 0x30))));
            }

            var d1 = unpacks[1].Data;
            var d2 = unpacks[2].Data;
            var verts = new List<SVtx>();
            for (int v = 0; v < vertexCount; v++)
            {
                if ((v + 1) * 8 > d1.Count || (v + 1) * 8 > d2.Count)
                {
                    break;
                }

                verts.Add(new SVtx(
                    BinaryPrimitives.ReadInt16LittleEndian(d1.AsSpan(v * 8)),
                    BinaryPrimitives.ReadInt16LittleEndian(d1.AsSpan(v * 8 + 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(d1.AsSpan(v * 8 + 4)),
                    BinaryPrimitives.ReadInt16LittleEndian(d1.AsSpan(v * 8 + 6)),
                    BinaryPrimitives.ReadInt16LittleEndian(d2.AsSpan(v * 8)),
                    BinaryPrimitives.ReadInt16LittleEndian(d2.AsSpan(v * 8 + 2))));
            }

            int nextGifTag = 0, nextAdGif = 0, nextVertex = 0, nextOffset = 0, material = 0, guard = 0;
            bool strip = true;
            SPrim? prim = null;
            while ((nextGifTag < gifTags.Count || nextAdGif < adGifs.Count || nextVertex < verts.Count) && guard++ < 200_000)
            {
                if (nextGifTag < gifTags.Count && gifTags[nextGifTag].Ofs == nextOffset)
                {
                    strip = gifTags[nextGifTag].PrimStrip;
                    prim = null;
                    nextGifTag++;
                    nextOffset += 1;
                }
                else if (nextAdGif < adGifs.Count && adGifs[nextAdGif].Ofs == nextOffset)
                {
                    material = adGifs[nextAdGif].TexId;
                    prim = null;
                    nextAdGif++;
                    nextOffset += 5;
                }
                else if (nextVertex < verts.Count && verts[nextVertex].Ofs == nextOffset)
                {
                    if (prim is null)
                    {
                        prim = new SPrim { Material = material, Strip = strip };
                        primitives.Add(prim);
                    }

                    prim.Verts.Add(verts[nextVertex]);
                    nextVertex++;
                    nextOffset += 3;
                }
                else
                {
                    break;
                }
            }
        }

        var positions = new List<double>();
        var uvs = new List<float>();
        var indices = new List<int>();
        var slots = new List<int>();
        double k = scale / 1024.0;

        foreach (var prim in primitives)
        {
            if (prim.Verts.Count < 3)
            {
                continue;
            }

            int baseV = positions.Count / 3;
            foreach (var v in prim.Verts)
            {
                positions.Add(v.X * k);
                positions.Add(v.Y * k);
                positions.Add(v.Z * k);
                uvs.Add(v.S / 4096f);
                uvs.Add(v.T / 4096f);
            }

            if (prim.Strip)
            {
                for (int i = 0; i < prim.Verts.Count - 2; i++)
                {
                    indices.Add(baseV + i);
                    indices.Add(baseV + i + 1);
                    indices.Add(baseV + i + 2);
                    slots.Add(prim.Material);
                }
            }
            else
            {
                for (int i = 0; i + 3 <= prim.Verts.Count; i += 3)
                {
                    indices.Add(baseV + i);
                    indices.Add(baseV + i + 1);
                    indices.Add(baseV + i + 2);
                    slots.Add(prim.Material);
                }
            }
        }

        return new Mesh(positions.ToArray(), uvs.ToArray(), indices.ToArray(), slots.ToArray(), scale);
    }

}

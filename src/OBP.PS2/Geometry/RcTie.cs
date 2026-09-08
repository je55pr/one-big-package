using System.Buffers.Binary;

namespace OBP.PS2.Geometry;

/// <summary>
/// Going Commando tie class geometry — the instanced environment set-pieces
/// (buildings, rocks, walls, platforms). Recovers LOD 0: positions
/// (<c>s16 * scale / 1024</c>), UVs (<c>u16 / 4096</c>) and a per-triangle
/// class-local material slot. R&C1 and GC share the packet grammar but select
/// different class-header layouts. See the game-specific research notes.
/// </summary>
public static class RcTie
{
    public const int Rac1ClassHeaderSize = 0x70;
    public const int GcClassHeaderSize = 0x80;
    public const int ClassEntrySize = 0x20;

    public sealed record Mesh(double[] Positions, float[] Uvs, int[] Indices, int[] TriangleMaterialSlots, float Scale);


    private readonly record struct Vtx(int X, int Y, int Z, int Ofs, int S, int T);

    private sealed class Primitive
    {
        public int Material;
        public int Winding;
        public readonly List<Vtx> Verts = [];
    }

    public sealed record Layout(string Name, int HeaderSize, int PacketCountOffset, int ScaleOffset);

    public static readonly Layout Rac1Layout = new("rac1", Rac1ClassHeaderSize, 0x20, 0x40);
    public static readonly Layout GcLayout = new("gc-uya-dl", GcClassHeaderSize, 0x0c, 0x40);

    public static Mesh ReadClass(ReadOnlySpan<byte> buf, Layout layout)
    {
        if (buf.Length < layout.HeaderSize)
        {
            throw new InvalidDataException($"RC tie class ({layout.Name}) buffer shorter than the 0x{layout.HeaderSize:x} header.");
        }

        float scale = BinaryPrimitives.ReadSingleLittleEndian(buf[layout.ScaleOffset..]);
        var primitives = new List<Primitive>();
        int tableBase = BinaryPrimitives.ReadInt32LittleEndian(buf);
        int packetCount = buf[layout.PacketCountOffset];

        if (tableBase > 0 && tableBase < buf.Length)
        {
            for (int p = 0; p < packetCount; p++)
            {
                int phOff = tableBase + p * 0x10;
                if (phOff + 0x10 > buf.Length)
                {
                    break;
                }

                int packetData = tableBase + BinaryPrimitives.ReadInt32LittleEndian(buf[phOff..]);
                int vertOfs = buf[phOff + 0x08] * 0x10;
                int vertSize = buf[phOff + 0x09] * 0x10;
                if (packetData <= 0 || packetData + 0x2c > buf.Length)
                {
                    continue;
                }

                var adGifDest = new int[4];
                var adGifSrc = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    adGifDest[i] = BinaryPrimitives.ReadInt32LittleEndian(buf[(packetData + i * 4)..]);
                    adGifSrc[i] = BinaryPrimitives.ReadInt32LittleEndian(buf[(packetData + 0x10 + i * 4)..]);
                }

                int stripCount = buf[packetData + 0x23];
                int dinkyCount = System.Math.Max(0, (buf[packetData + 0x28] - 4) >> 1);

                var strips = new List<(int GifTagOffset, int Winding)>();
                for (int s = 0; s < stripCount; s++)
                {
                    int so = packetData + 0x2c + s * 4;
                    if (so + 4 > buf.Length)
                    {
                        break;
                    }

                    strips.Add((buf[so + 2], buf[so + 3]));
                }

                int vbase = packetData + vertOfs;
                var raw = new List<Vtx>();
                void Push(int x, int y, int z, int ofs, int s, int t, int ofs2)
                {
                    raw.Add(new Vtx(x, y, z, ofs, s, t));
                    if (ofs2 != 0 && ofs2 != ofs)
                    {
                        raw.Add(new Vtx(x, y, z, ofs2, s, t));
                    }
                }

                for (int v = 0; v < dinkyCount; v++)
                {
                    int o = vbase + v * 0x10;
                    if (o + 0x10 > buf.Length)
                    {
                        break;
                    }

                    Push(
                        BinaryPrimitives.ReadInt16LittleEndian(buf[o..]),
                        BinaryPrimitives.ReadInt16LittleEndian(buf[(o + 2)..]),
                        BinaryPrimitives.ReadInt16LittleEndian(buf[(o + 4)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(o + 6)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(o + 8)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(o + 10)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(o + 14)..]));
                }

                for (int fo = vbase + dinkyCount * 0x10; fo + 0x18 <= vbase + vertSize && fo + 0x18 <= buf.Length; fo += 0x18)
                {
                    Push(
                        BinaryPrimitives.ReadInt16LittleEndian(buf[(fo + 8)..]),
                        BinaryPrimitives.ReadInt16LittleEndian(buf[(fo + 10)..]),
                        BinaryPrimitives.ReadInt16LittleEndian(buf[(fo + 12)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(fo + 6)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(fo + 16)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(fo + 18)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(buf[(fo + 22)..]));
                }

                raw.Sort((a, b) => a.Ofs - b.Ofs);
                var verts = new List<Vtx>();
                if (raw.Count > 0)
                {
                    verts.Add(raw[0]);
                }

                for (int i = 1; i < raw.Count; i++)
                {
                    if (raw[i].Ofs != raw[i - 1].Ofs)
                    {
                        verts.Add(raw[i]);
                    }
                }

                int nextStrip = 0, nextVertex = 0, nextAdGif = 1, nextOffset = 6, guard = 0;
                int material = System.Math.Max(0, adGifSrc[0] / 0x50);
                Primitive? prim = null;
                while ((nextStrip < strips.Count || nextVertex < verts.Count) && guard++ < 200_000)
                {
                    if (nextStrip < strips.Count && strips[nextStrip].GifTagOffset == nextOffset)
                    {
                        prim = new Primitive { Material = material, Winding = strips[nextStrip].Winding != 0 ? 1 : 0 };
                        primitives.Add(prim);
                        nextStrip++;
                        nextOffset += 1;
                    }
                    else if (nextVertex < verts.Count && verts[nextVertex].Ofs == nextOffset)
                    {
                        prim?.Verts.Add(verts[nextVertex]);
                        nextVertex++;
                        nextOffset += 3;
                    }
                    else if (nextAdGif < adGifSrc.Length && adGifDest[nextAdGif - 1] == nextOffset)
                    {
                        material = System.Math.Max(0, adGifSrc[nextAdGif] / 0x50);
                        nextAdGif++;
                        nextOffset += 6;
                    }
                    else
                    {
                        break;
                    }
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

            for (int i = 2; i < prim.Verts.Count; i++)
            {
                int a = baseV + i - 2, b = baseV + i - 1, c = baseV + i;
                if (i % 2 == prim.Winding)
                {
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(c);
                }
                else
                {
                    indices.Add(b);
                    indices.Add(a);
                    indices.Add(c);
                }

                slots.Add(prim.Material);
            }
        }

        return new Mesh(positions.ToArray(), uvs.ToArray(), indices.ToArray(), slots.ToArray(), scale);
    }

}

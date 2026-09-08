using System.Buffers.Binary;

namespace OBP.PS2.Vif;

/// <summary>
/// Minimal PS2 VIF1 command-list reader. The R&amp;C geometry formats store
/// vertex data as VIF DMA packet streams; asset recovery only needs to walk the
/// stream and pull out the UNPACK packets and STROW writes. Translated from
/// <c>reference-ts/packages/ps2-vif</c>.
/// </summary>
public static class Vif
{
    public const int CmdNop = 0x00;
    public const int CmdStcycl = 0x01;
    public const int CmdStmod = 0x05;
    public const int CmdStmask = 0x20;
    public const int CmdStrow = 0x30;
    public const int CmdStcol = 0x31;
    public const int CmdMpg = 0x4a;
    public const int CmdDirect = 0x50;
    public const int CmdDirectHl = 0x51;

    // VN/VL codes (low nibble of an UNPACK cmd).
    public const int UnpackS32 = 0x0, UnpackS16 = 0x1, UnpackS8 = 0x2;
    public const int UnpackV2_32 = 0x4, UnpackV2_16 = 0x5, UnpackV2_8 = 0x6;
    public const int UnpackV3_32 = 0x8, UnpackV3_16 = 0x9, UnpackV3_8 = 0xa;
    public const int UnpackV4_32 = 0xc, UnpackV4_16 = 0xd, UnpackV4_8 = 0xe, UnpackV4_5 = 0xf;

    public readonly record struct VifCode(uint Raw, bool Interrupt, int Cmd, int Num, bool IsUnpack, int Vnvl, bool Unsigned, int Addr);

    /// <summary>A VIF packet: its byte offset in the list, its code, and its inline data (a view into the source array).</summary>
    public readonly record struct VifPacket(int Offset, VifCode Code, ArraySegment<byte> Data);

    public static int UnpackElementSize(int vnvl)
    {
        int vn = (vnvl & 0b1100) >> 2;
        int vl = vnvl & 0b11;
        if (vl == 3)
        {
            return vn == 3 ? 2 : (int)System.Math.Ceiling(((32 >> vl) * (vn + 1)) / 8.0);
        }

        return (32 >> vl) * (vn + 1) / 8;
    }

    public static VifCode DecodeCode(uint raw)
    {
        int cmd = (int)((raw >> 24) & 0x7f);
        int num = (int)((raw >> 16) & 0xff);
        if (num == 0)
        {
            num = 256;
        }

        bool isUnpack = (cmd & 0x60) == 0x60;
        return new VifCode(
            raw,
            (raw >> 31) != 0,
            cmd,
            num,
            isUnpack,
            isUnpack ? cmd & 0x0f : 0,
            isUnpack && ((raw >> 14) & 1) != 0,
            isUnpack ? (int)(raw & 0x3ff) : 0);
    }

    public static int PacketSize(VifCode code)
    {
        switch (code.Cmd)
        {
            case CmdStmask:
                return 2 * 4;
            case CmdStrow:
            case CmdStcol:
                return 5 * 4;
            case CmdMpg:
                return (1 + code.Num * 2) * 4;
            case CmdDirect:
            case CmdDirectHl:
                int size = (int)(code.Raw & 0xffff);
                if (size == 0)
                {
                    size = 0x10000;
                }

                return (1 + size * 4) * 4;
            default:
                if (code.IsUnpack)
                {
                    int s = code.Num * UnpackElementSize(code.Vnvl);
                    if (s % 4 != 0)
                    {
                        s += 4 - (s % 4);
                    }

                    return (1 + s / 4) * 4;
                }

                return 4;
        }
    }

    public static List<VifPacket> ReadCommandList(ArraySegment<byte> bytes, int maxPackets = 100_000)
    {
        var packets = new List<VifPacket>();
        int offset = 0;
        while (offset + 4 <= bytes.Count && packets.Count < maxPackets)
        {
            var code = DecodeCode(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
            int size = PacketSize(code);
            if (size <= 0 || size > 0x10000 || offset + size > bytes.Count)
            {
                break;
            }

            packets.Add(new VifPacket(offset, code, bytes.Slice(offset + 4, size - 4)));
            offset += size;
        }

        return packets;
    }

    public static List<VifPacket> FilterUnpacks(IEnumerable<VifPacket> packets) =>
        packets.Where(p => p.Code.IsUnpack).ToList();
}

using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Graphics;

namespace OBP.RAC2.Presentation;

/// <summary>SCUS-97268 v1.01's resident global HUD header and item-icon bindings.</summary>
public static class Rac2WeaponMenuAssets
{
    public static Ps2HudSpriteBank Read(IRandomAccessReader disc)
    {
        if (disc.Length != Rac2Authority.PrimaryIsoSizeBytes || Ps2Boot.OpenDisc(disc).Boot.Serial != Rac2Authority.Primary.Serial)
            throw new InvalidDataException("RAC2 weapon icons require the supported NTSC-U v1.01 disc.");
        byte[] header = disc.Read(1001L * 2048 + 0x368, 0x1870);
        if (U32(header, 0) != 0x1870) throw new InvalidDataException("RAC2 global HUD header differs from the supported build.");
        long baseOffset = (long)U32(header, 4) * 2048;
        var ranges = Enumerable.Range(0, 6).Select(i =>
            (Offset: baseOffset + (long)U32(header, 8 + i * 8) * 2048,
             Length: checked((int)((long)U32(header, 12 + i * 8) * 2048)))).ToArray();
        return Ps2HudSpriteChunks.Read(disc, ranges);
    }

    // Descriptor +0x3C, corroborated by the native HUD animation directory.
    public static ushort? Icon(int item) => item switch
    {
        9 or 73 => 0xea99,
        12 or 115 => 0xea79,
        14 or 117 => 0xea7d,
        16 or 72 or 109 or 110 => 0xea77,
        17 or 118 => 0xea7b,
        18 or 114 => 0xea7a,
        22 or 65 or 83 or 84 => 0xea66,
        23 or 66 or 87 or 88 => 0xea68,
        24 or 67 or 85 or 86 => 0xea6b,
        25 or 70 or 103 or 104 => 0xea72,
        26 or 68 or 91 or 92 => 0xea67,
        27 or 69 or 99 or 100 => 0xea70,
        28 or 62 or 101 or 102 => 0xea71,
        29 or 63 or 95 or 96 => 0xea69,
        30 or 60 or 79 or 80 => 0xea64,
        31 or 61 or 93 or 94 => 0xea6d,
        32 or 78 or 105 or 106 => 0xea6e,
        37 or 76 or 97 or 98 => 0xea73,
        41 or 64 or 89 or 90 => 0xea6a,
        42 or 71 or 81 or 82 => 0xea65,
        43 => 0xea76,
        44 => 0xea6c,
        45 or 77 or 107 or 108 => 0xea74,
        53 or 116 => 0xea7c,
        _ => null,
    };

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}

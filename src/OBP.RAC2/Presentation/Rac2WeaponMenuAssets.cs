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
    private static readonly IReadOnlyDictionary<int, ushort> Icons =
        new (ushort Animation, int[] Items)[]
    {
        (0xea99, [9, 73]),
        (0xea79, [12, 115]),
        (0xea7d, [14, 117]),
        (0xea77, [16, 72, 109, 110]),
        (0xea7b, [17, 118]),
        (0xea7a, [18, 114]),
        (0xea66, [22, 65, 83, 84]),
        (0xea68, [23, 66, 87, 88]),
        (0xea6b, [24, 67, 85, 86]),
        (0xea72, [25, 70, 103, 104]),
        (0xea67, [26, 68, 91, 92]),
        (0xea70, [27, 69, 99, 100]),
        (0xea71, [28, 62, 101, 102]),
        (0xea69, [29, 63, 95, 96]),
        (0xea64, [30, 60, 79, 80]),
        (0xea6d, [31, 61, 93, 94]),
        (0xea6e, [32, 78, 105, 106]),
        (0xea73, [37, 76, 97, 98]),
        (0xea6a, [41, 64, 89, 90]),
        (0xea65, [42, 71, 81, 82]),
        (0xea76, [43]),
        (0xea6c, [44]),
        (0xea74, [45, 77, 107, 108]),
        (0xea7c, [53, 116]),
    }.SelectMany(binding => binding.Items.Select(item => (Item: item, binding.Animation)))
        .ToDictionary(binding => binding.Item, binding => binding.Animation);

    public static ushort? Icon(int item) => Icons.TryGetValue(item, out ushort icon) ? icon : null;

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}

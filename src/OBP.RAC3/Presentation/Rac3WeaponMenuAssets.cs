using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Graphics;

namespace OBP.RAC3.Presentation;

/// <summary>SCUS-97353's boot HUD and item descriptor +0x38 icon identities.</summary>
public static class Rac3WeaponMenuAssets
{
    public static Ps2HudSpriteBank Read(IRandomAccessReader disc)
    {
        if (disc.Length != Rac3Authority.PrimaryIsoSizeBytes || Ps2Boot.OpenDisc(disc).Boot.Serial != Rac3Authority.Primary.Serial)
            throw new InvalidDataException("RAC3 weapon icons require the supported NTSC-U original disc.");
        byte[] misc = disc.Read(1001L * 2048 + 0x648, 0x48);
        if (U32(misc, 0) != 0x48) throw new InvalidDataException("RAC3 resident misc header differs from the supported build.");
        long baseOffset = ((long)U32(misc, 4) + U32(misc, 0x40)) * 2048;
        long length = (long)U32(misc, 0x44) * 2048;
        var boot = new SubRangeReader(disc, baseOffset, length, "rac3-boot-hud");
        byte[] header = boot.Read(0, 0x80);
        var ranges = Enumerable.Range(0, 6).Select(i =>
            (Offset: (long)U32(header, 0x28 + i * 8), Length: checked((int)U32(header, 0x2c + i * 8)))).ToArray();
        return Ps2HudSpriteChunks.Read(boot, ranges);
    }

    private static readonly ushort[] WeaponIcons =
    [0xeaaf, 0xeaac, 0xeaab, 0xeab1, 0xeaba, 0xeab2, 0xeab0, 0xeaae, 0xeabc, 0xeab6, 0xeab4, 0xeaad, 0xeabb, 0xeab7, 0xea6c];

    public static ushort? Icon(int definition) => definition switch
    {
        16 => 0xea71,
        17 => 0xea69,
        19 => 0xea73,
        21 => 0xea6a,
        22 => 0xea74,
        >= 39 and <= 155 => WeaponIcons[(definition - 39) / 8],
        _ => null,
    };

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}

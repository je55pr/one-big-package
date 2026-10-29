using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Level;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Aridia's class-854 ocean callback consumes seven authored surface records,
/// rather than the collision class-3 mud footprint. Native records live in the
/// L2 executable overlay and are independent of the terrain / gameplay WAD.
/// A record is a 7-by-7-cell grid source, with a native per-cell draw packet at its pointer.
/// This extracts evidence only: it does not invent a host-side liquid shader.
/// </summary>
public static class Rac1AridiaOceanSource
{
    public const int LevelId = 2;
    public const int SourceClassId = 854;
    public const int AnimatedFxStart = 40;
    public const int AnimatedFxFrameCount = 64;

    private static readonly uint[] Addresses =
    [
        0x001f3c00, 0x001f3c60, 0x001f3cd0, 0x001f3d30,
        0x001f3d90, 0x001f3dd0, 0x001f3e10,
    ];

    public sealed record Surface(
        uint SourceAddress, float OriginX, float OriginY, float Height,
        float StepX, float StepY, int GridColumns, int GridRows,
        uint AnimatedFramePacket, uint CellPacketAddress, uint CellMaskAddress,
        byte[] CellMask)
    {
        public int CellColumns => GridColumns / 7;
        public int CellRows => GridRows / 7;
    }

    /// <summary>Native VIF V4-8 packet for one 7-by-7 cell: 124 packed 4-byte values.
    /// These are source bytes, not decoded Godot vertex attributes.</summary>
    public sealed record CellPacket(int CellIndex, byte[] PackedValues);

    public static IReadOnlyList<Surface> Read(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.LevelId != LevelId)
            throw new ArgumentException("Aridia ocean records require the native LEVEL2 overlay.", nameof(level));
        return Decode(Rac1LevelOverlay.Open(disc, level));
    }

    public static IReadOnlyList<Surface> Decode(Rac1LevelOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var result = new List<Surface>(Addresses.Length);
        foreach (uint address in Addresses)
        {
            var bytes = overlay.Read(address, 0x40).Span;
            static float F32(ReadOnlySpan<byte> data, int offset) => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));
            var centerX = F32(bytes, 0);
            var centerY = F32(bytes, 4);
            var height = F32(bytes, 8);
            var stepX = F32(bytes, 0x10);
            var stepY = F32(bytes, 0x14);
            var columns = BinaryPrimitives.ReadUInt16LittleEndian(bytes[0x18..]);
            var rows = BinaryPrimitives.ReadUInt16LittleEndian(bytes[0x1a..]);
            var framePacket = BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x1c..]);
            var cellPacket = BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x34..]);
            var cellMaskAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes[0x38..]);

            if (!float.IsFinite(centerX) || !float.IsFinite(centerY) ||
                !float.IsFinite(height) || !float.IsFinite(stepX) || !float.IsFinite(stepY) ||
                stepX <= 0 || stepY <= 0 ||
                columns == 0 || rows == 0 || columns % 7 != 0 || rows % 7 != 0 ||
                framePacket != 0x001f3e80)
                throw new InvalidDataException($"Aridia ocean surface record 0x{address:x8} is invalid.");

            // The native VU draw references a 0x200-byte per-cell VIF packet.
            // Preserve its address: decoding its VIF payload and waveform
            // is a separate step from recognizing authored grid metadata.
            _ = overlay.Read(cellPacket, 0x200);
            var flags = overlay.Read(cellMaskAddress, (columns / 7) * (rows / 7)).ToArray();
            if (flags.Any(flag => flag is not (0 or 1)))
                throw new InvalidDataException($"Aridia ocean cell flags at 0x{cellMaskAddress:x8} have unsupported values.");
            result.Add(new Surface(address, centerX, centerY, height,
                stepX, stepY, columns, rows, framePacket, cellPacket, cellMaskAddress, flags));
        }
        return result;
    }

    /// <summary>
    /// Each cell has one 0x200-byte VIF packet with a 0x10-byte command prefix,
    /// followed by 0x7c packed 4-byte vertex values. Cells are stored with a
    /// 0x200-byte stride; records can share the same source cell-packet base.
    /// </summary>
    public static IReadOnlyList<CellPacket> ReadCellPackets(Rac1LevelOverlay overlay, Surface surface)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(surface);
        var cells = new List<CellPacket>(surface.CellColumns * surface.CellRows);
        for (int i = 0; i < surface.CellColumns * surface.CellRows; i++)
        {
            uint address = checked(surface.CellPacketAddress + (uint)(i * 0x200));
            var packet = overlay.Read(address, 0x200).Span;
            if (BinaryPrimitives.ReadUInt32LittleEndian(packet[0x0c..]) != 0x6e7cc002)
                throw new InvalidDataException($"Aridia cell packet 0x{address:x8} has no VIF V4-8/124-vertex command.");
            cells.Add(new CellPacket(i, packet[0x10..0x200].ToArray()));
        }
        return cells;
    }
}
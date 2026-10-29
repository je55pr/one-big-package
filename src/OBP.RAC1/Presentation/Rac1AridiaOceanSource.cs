using System.Buffers.Binary;
using System.Numerics;
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
    public const int NativeStripVertexCount = 124;
    public const uint NativeStripIndexAddress = 0x001cb400;
    public const uint NativeStripTextureAddress = 0x001cb510;
    public const float NativeStripTextureScale = 1f / 6f;

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

    /// <summary>A source-ordered VU1 strip input: index into the cell's
    /// 8x8 vertex lattice, with its native packed RGB sample. The packet's
    /// fourth byte is zero in retail data and is not an inferred alpha.</summary>
    public readonly record struct StripVertex(byte LatticeIndex, byte Red, byte Green, byte Blue);

    /// <summary>One native camera clip cuboid and the source records drawn
    /// whenever the camera is within that cuboid. The first, middle and last
    /// ocean regions are independent, and their volumes may overlap.</summary>
    public sealed record CameraDrawGroup(Rac1ClipVolume ClipVolume, int[] SurfaceIndices);

    public static IReadOnlyList<CameraDrawGroup> ReadCameraDrawGroups(byte[] gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        var actor = Rac1Instances.Parse(gameplay).MobyInstances
            .Single(m => m.OClass == SourceClassId);
        if (actor.PVar is not { Length: >= 12 } pvar)
            throw new InvalidDataException("Aridia class-854 ocean actor lacks three camera clip-volume selectors.");
        var volumes = Rac1ClipVolumes.Read(gameplay);
        int[][] surfaceGroups = [[0], [2, 3, 4, 5, 6], [1]];
        var result = new CameraDrawGroup[3];
        for (int i = 0; i < result.Length; i++)
        {
            int volumeId = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(i * 4));
            if (volumeId < 0 || volumeId >= volumes.Count)
                throw new InvalidDataException($"Aridia class-854 camera clip-volume {volumeId} is invalid.");
            result[i] = new CameraDrawGroup(volumes[volumeId], surfaceGroups[i]);
        }
        return result;
    }

    public static int[] VisibleSurfaceIndices(IReadOnlyList<CameraDrawGroup> groups, Vector3 cameraNative)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (!float.IsFinite(cameraNative.X) || !float.IsFinite(cameraNative.Y) || !float.IsFinite(cameraNative.Z))
            throw new ArgumentOutOfRangeException(nameof(cameraNative));
        return groups.Where(g => g.ClipVolume.Contains(cameraNative))
            .SelectMany(g => g.SurfaceIndices).Distinct().ToArray();
    }

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

    /// <summary>Unpack the native 0x1cb400 strip index table. Entries are byte
    /// offsets into an 8-byte-stride 8x8 vertex lattice (0..63); consecutive
    /// rows are joined with degenerate strip vertices.</summary>
    public static byte[] ReadStripLatticeIndices(Rac1LevelOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var bytes = overlay.Read(NativeStripIndexAddress, NativeStripVertexCount * 2).Span;
        var indices = new byte[NativeStripVertexCount];
        for (int i = 0; i < indices.Length; i++)
        {
            ushort offset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(i * 2, 2));
            if (offset > 63 * 8 || offset % 8 != 0)
                throw new InvalidDataException($"Aridia strip index {i} is not an 8x8 lattice offset.");
            indices[i] = checked((byte)(offset / 8));
        }
        return indices;
    }

    /// <summary>The game copies the overlay's 124 pairs at 0x1cb510 into
    /// the VU1 input, multiplying each by 1/6 at ocean actor startup.
    /// The DMA chain sends these as strip texture ST coordinates after the
    /// separately built world XYZ and per-cell packed colours.</summary>
    public static Vector2[] ReadStripTextureCoordinates(Rac1LevelOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var data = overlay.Read(NativeStripTextureAddress, NativeStripVertexCount * 2 * 4).Span;
        var result = new Vector2[NativeStripVertexCount];
        for (int i = 0; i < result.Length; i++)
        {
            float x = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(i * 8, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(i * 8 + 4, 4));
            if (!float.IsFinite(x) || !float.IsFinite(y) || MathF.Abs(x) > 3 || MathF.Abs(y) > 3)
                throw new InvalidDataException($"Aridia local strip coordinate {i} is invalid.");
            result[i] = new Vector2(x * NativeStripTextureScale,
                y * NativeStripTextureScale);
        }
        return result;
    }

    /// <summary>Pair original VIF colour slots with original strip positions.
    /// No UV, transparency, lighting or wave height is inferred.</summary>
    public static StripVertex[] DecodeCellStrip(CellPacket packet, ReadOnlySpan<byte> latticeIndices)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (latticeIndices.Length != NativeStripVertexCount ||
            packet.PackedValues.Length != NativeStripVertexCount * 4)
            throw new InvalidDataException("Aridia cell strip size does not match its native VIF payload.");
        var vertices = new StripVertex[NativeStripVertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            int offset = i * 4;
            vertices[i] = new StripVertex(latticeIndices[i],
                packet.PackedValues[offset], packet.PackedValues[offset + 1], packet.PackedValues[offset + 2]);
        }
        return vertices;
    }

    /// <summary>Each cell has one 0x200-byte VIF packet with a 0x10-byte
    /// command prefix followed by 0x7c packed four-byte values. Cells have
    /// a 0x200-byte stride; records may share packet ranges.</summary>
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
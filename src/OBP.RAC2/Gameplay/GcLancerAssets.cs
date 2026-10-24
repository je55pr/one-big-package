using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.PS2.Compression;
using OBP.PS2.Geometry;
using OBP.RAC2.Audio;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Local-disc extraction for the base Lancer's global held assets. The shot
/// model is separately class 3070 in the level core. No host or R&amp;C1 ids enter
/// this reader. See research/GC_LANCER.md for texture and attachment boundaries.
/// </summary>
public static class GcLancerAssets
{
    public const string GadgetIsoPath = "/G/GADGET.WAD";
    private const int SectorBytes = 2048;
    private const int EntryBytes = 20;
    private const int MaxAssetBytes = 4 * 1024 * 1024;

    /// <summary>
    /// TexturePif is the native PIF2 texture, including its header, CLUT and
    /// pixels. Material/GS presentation is deliberately left to the consumer.
    /// Keep the source reader open while decoding the lazy sound bank.
    /// </summary>
    public sealed record HeldAssets(
        GcUyaMoby.MobyClass Model,
        byte[] TexturePif,
        GcSoundBank SoundBank)
    {
        public IReadOnlyList<Matrix4x4> InverseBindMatrices { get; init; } = [];
    }

    public static HeldAssets ReadHeld(IRandomAccessReader gadgetWad)
    {
        ArgumentNullException.ThrowIfNull(gadgetWad);
        var prefix = gadgetWad.Read(0, 8);
        int headerSize = I32(prefix, 0);
        if (headerSize < 8 || headerSize > 64 * 1024 || (headerSize - 8) % EntryBytes != 0)
            throw new InvalidDataException("Invalid GC gadget directory size.");

        byte[] directory = gadgetWad.Read(0, headerSize);
        int entry = -1;
        for (int at = 8; at < directory.Length; at += EntryBytes)
        {
            if (I32(directory, at) != GcLancerProfile.HeldMobyClass) continue;
            if (entry >= 0) throw new InvalidDataException("Duplicate base Lancer gadget entry.");
            entry = at;
        }

        if (entry < 0) throw new InvalidDataException("Base Lancer is absent from the GC gadget directory.");
        long dataOffset = (long)I32(directory, entry + 4) * SectorBytes;
        long dataSize = (long)I32(directory, entry + 8) * SectorBytes;
        long bankOffset = (long)I32(directory, entry + 12) * SectorBytes;
        long bankSize = (long)I32(directory, entry + 16) * SectorBytes;
        if (dataOffset < headerSize || bankOffset < dataOffset + dataSize)
            throw new InvalidDataException("Overlapping GC Lancer gadget ranges.");

        var data = Bounded(gadgetWad, dataOffset, dataSize, "Lancer data");
        var bank = Bounded(gadgetWad, bankOffset, bankSize, "Lancer sound bank");
        byte[] ranges = data.Read(0, 16);
        long modelOffset = I32(ranges, 0) - dataOffset;
        long modelSize = I32(ranges, 4);
        long textureOffset = I32(ranges, 8) - dataOffset;
        long textureSize = I32(ranges, 12);
        if (modelOffset < 16 || textureOffset < modelOffset + modelSize)
            throw new InvalidDataException("Overlapping GC Lancer model/texture ranges.");

        var modelSource = Bounded(data, modelOffset, modelSize, "Lancer model");
        var textureSource = Bounded(data, textureOffset, textureSize, "Lancer texture");
        byte[] model = WadLz.ReadBlock(modelSource, 0, MaxAssetBytes).Data;
        byte[] texture = WadLz.ReadBlock(textureSource, 0, MaxAssetBytes).Data;
        // This bounded slice admits the one-texture PIF2 table carried by the
        // base Lancer, rather than guessing at other gadgets' texture layouts.
        if (texture.Length < 0x30 || I32(texture, 0) != 1 || I32(texture, 4) != 16
            || I32(texture, 16) != 0x50494632)
            throw new InvalidDataException("Unsupported GC Lancer texture table.");
        int width = I32(texture, 24);
        int height = I32(texture, 28);
        if (width is <= 0 or > 1024 || height is <= 0 or > 1024
            || I32(texture, 32) != 19 || texture.Length < 0x30 + 256 * 4 + width * height)
            throw new InvalidDataException("Invalid GC Lancer PIF8 texture extent.");
        if (model.Length < 0x48) throw new InvalidDataException("Truncated GC Lancer model.");

        var mesh = GcUyaMoby.ReadClass(model);
        if (mesh.TriangleMaterialSlots.Any(slot => slot > 0))
            throw new InvalidDataException("GC Lancer material refers outside its one-texture table.");
        var joints = GcUyaMoby.ReadJoints(model, model[8]);
        var sequences = GcUyaMoby.ReadSequences(model, model[8]);
        var held = new GcUyaMoby.MobyClass(GcLancerProfile.HeldMobyClass,
            mesh, mesh.TriangleMaterialSlots.ToArray(), [0], joints, sequences);
        return new(held, texture[16..], GcSoundBank.Open(bank))
        { InverseBindMatrices = GcUyaGadgetPose.ReadInverseBindMatrices(model) };
    }

    private static SubRangeReader Bounded(IRandomAccessReader source, long offset, long size, string name)
    {
        if (offset < 0 || size <= 0 || size > MaxAssetBytes
            || offset > source.Length || size > source.Length - offset)
            throw new InvalidDataException($"GC {name} exceeds its source range.");
        return new(source, offset, size, name);
    }

    private static int I32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
}

using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.PS2.Textures;
using OBP.Runtime;

namespace OBP.RAC3.Level;

/// <summary>UYA's native shared definition-frame blob, relocated by TABLE1 0x4224c8.</summary>
public sealed class Rac3ParticleDefinitions
{
    private readonly int[] _offsets;
    private readonly byte[] _frames;
    public int DefinitionCount => _offsets.Length;
    public int TextureCount { get; }
    private Rac3ParticleDefinitions(int[] offsets, byte[] frames, int count) => (_offsets, _frames, TextureCount) = (offsets, frames, count);
    public static Rac3ParticleDefinitions Parse(ReadOnlySpan<byte> data, int textureCount)
    {
        if (data.Length < 16 || textureCount is < 1 or > 256) throw new InvalidDataException("Invalid UYA Part definition header.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data), textures = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        int start = BinaryPrimitives.ReadInt32LittleEndian(data[8..]), size = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        if (count is < 1 or > 256 || textures != textureCount || start < 16L + count * 4L || size < 1 || (long)start + size > data.Length)
            throw new InvalidDataException("UYA particle definitions exceed their shared native blob.");
        int[] offsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            int raw = BinaryPrimitives.ReadInt32LittleEndian(data[(16 + i * 4)..]);
            long offset = raw == 0 ? 0 : (long)raw - start;
            if (offset < 0 || offset >= size) throw new InvalidDataException("UYA definition points outside its frame blob.");
            offsets[i] = (int)offset;
        }
        byte[] frames = data.Slice(start, size).ToArray();
        if (frames.Any(f => f >= textureCount)) throw new InvalidDataException("UYA frame selects an absent Part texture.");
        return new(offsets, frames, textureCount);
    }
    public int Texture(int definition, int frame)
    {
        if ((uint)definition >= _offsets.Length || frame < 0 || (long)_offsets[definition] + frame >= _frames.Length)
            throw new InvalidDataException("UYA definition/frame exceeds its loaded blob.");
        return _frames[_offsets[definition] + frame];
    }
}

/// <summary>Source-qualified Part bank; the Cannon beam's existing Fx bank is a different resource.</summary>
public sealed record Rac3ParticleAssets(ObpSourceRef Source, Rac3ParticleDefinitions Definitions,
    IReadOnlyDictionary<int, RuntimeTexture> Textures)
{
    public const string AssetKind = "rac3-part";
    public IReadOnlyDictionary<int, Rac3PartTextureLayout> NativeLayouts { get; init; } = new Dictionary<int, Rac3PartTextureLayout>();
    public static Rac3ParticleAssets Read(Rac3LevelCore.Core core, ObpSourceRef source)
    {
        if (source.Game != ObpSourceGame.Rac3 || source.BuildId != Rac3Authority.Primary.BuildId || string.IsNullOrEmpty(source.LevelId))
            throw new ArgumentException("UYA Part assets require a qualified original source/build/level.", nameof(source));
        var table = core.Header.PartTextures;
        if (core.Index.Length < 0x70 || table.Count is < 1 or > 256 || table.Offset < Rac3LevelCore.CoreHeaderSize ||
            (long)table.Offset + table.Count * 16L > core.Index.Length)
            throw new InvalidDataException("UYA Part directory exceeds its core index.");
        int bank = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x64)), definition = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x6c));
        if (bank < 0 || bank > core.Assets.Length || definition < Rac3LevelCore.CoreHeaderSize || definition > core.Index.Length - 16)
            throw new InvalidDataException("UYA Part bank/definitions exceed the core.");
        var definitions = Rac3ParticleDefinitions.Parse(core.Index.AsSpan(definition), table.Count);
        var images = new Dictionary<int, RuntimeTexture>(); var layouts = new Dictionary<int, Rac3PartTextureLayout>();
        for (int id = 0; id < table.Count; id++)
        {
            var row = core.Index.AsSpan(table.Offset + id * 16, 16);
            int palette = BinaryPrimitives.ReadInt32LittleEndian(row), pixels = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]); int dimension = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
            if (flags != 0) throw new NotSupportedException("UYA Part encodings beyond the measured PSMT8 slice require their own decoder.");
            if (palette < 0 || pixels < 0 || dimension is < 1 or > 1024 || (dimension & (dimension - 1)) != 0 ||
                (long)bank + palette + 1024 > core.Assets.Length || (long)bank + pixels + (long)dimension * dimension > core.Assets.Length)
                throw new InvalidDataException("UYA Part pixels/palette exceed the independent bank.");
            uint[] colors = Enumerable.Range(0, 256).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(core.Assets.AsSpan(bank + palette + 4 * i))).ToArray();
            var decoded = Ps2Texture.DecodePaletted8(new(dimension, dimension, core.Assets.AsMemory(bank + pixels, dimension * dimension), colors));
            images.Add(id, new(AssetKind, id, dimension, dimension, decoded.Rgba));
            layouts.Add(id, new((uint)palette, (uint)pixels, dimension));
        }
        return new(source, definitions, images) { NativeLayouts = layouts };
    }
}

public readonly record struct Rac3PartTextureLayout(uint PaletteOffset, uint PixelOffset, int Dimension)
{
    public int DimensionLog2 => Dimension is >= 1 and <= 1024 && BitOperations.IsPow2((uint)Dimension)
        ? BitOperations.Log2((uint)Dimension) : throw new InvalidDataException("Unsupported UYA Part dimensions.");
    public (uint Palette, uint Pixels) Bind(uint gsBase) =>
        (unchecked((gsBase + PaletteOffset) << 4), unchecked(((gsBase + PixelOffset) << 4) + (uint)DimensionLog2));
}

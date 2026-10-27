using System.Buffers.Binary;
using OBP.Core;
using OBP.PS2.Textures;
using OBP.Runtime;

namespace OBP.RAC2.Level;

/// <summary>The GC definition header and shared frame-byte blob relocated by Museum 0x3042a8.</summary>
public sealed class Rac2ParticleDefinitions
{
    private readonly int[] _offsets;
    private readonly byte[] _frames;
    public int DefinitionCount => _offsets.Length;
    public int TextureCount { get; }
    private Rac2ParticleDefinitions(int[] offsets, byte[] frames, int textureCount) => (_offsets, _frames, TextureCount) = (offsets, frames, textureCount);

    public static Rac2ParticleDefinitions Parse(ReadOnlySpan<byte> data, int textureCount)
    {
        if (data.Length < 16 || textureCount is < 1 or > 256) throw new InvalidDataException("Invalid GC particle definition header.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data), textures = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        int start = BinaryPrimitives.ReadInt32LittleEndian(data[8..]), size = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        if (count is < 1 or > 256 || textures != textureCount || start < 16L + count * 4L || size < 1 || (long)start + size > data.Length)
            throw new InvalidDataException("GC particle definitions exceed their bounded native blob.");
        var offsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            int raw = BinaryPrimitives.ReadInt32LittleEndian(data[(16 + i * 4)..]);
            long offset = raw == 0 ? 0 : (long)raw - start; // Native zero entries point at the blob's first byte.
            if (offset < 0 || offset >= size) throw new InvalidDataException("GC particle definition points outside its frame blob.");
            offsets[i] = (int)offset;
        }
        byte[] frames = data.Slice(start, size).ToArray();
        if (frames.Any(frame => frame >= textureCount)) throw new InvalidDataException("GC particle frame selects an absent Part texture.");
        return new(offsets, frames, textureCount);
    }

    /// <summary>The native editor indexes the shared blob directly, including across adjacent definition entries.</summary>
    public int Texture(int definition, int frame)
    {
        if ((uint)definition >= _offsets.Length || frame < 0 || (long)_offsets[definition] + frame >= _frames.Length)
            throw new InvalidDataException("GC particle definition/frame pair exceeds the loaded frame blob.");
        return _frames[_offsets[definition] + frame];
    }
}

/// <summary>GC Part bank only; Fx and ordinary Moby/tfrag texture grammars remain separate.</summary>
public sealed record Rac2ParticleAssets(ObpSourceRef Source, Rac2ParticleDefinitions Definitions,
    IReadOnlyDictionary<int, RuntimeTexture> Textures)
{
    public const string AssetKind = "rac2-part";
    public IReadOnlyDictionary<int, Rac2PartTextureLayout> NativeLayouts { get; init; } = new Dictionary<int, Rac2PartTextureLayout>();

    public static Rac2ParticleAssets Read(Rac2LevelCore.Core core, ObpSourceRef source)
    {
        if (source.Game != ObpSourceGame.Rac2 || source.BuildId != Rac2Authority.Primary.BuildId || source.LevelId is null)
            throw new ArgumentException("GC particles require a qualified original v1.01 level source.", nameof(source));
        var table = core.Header.PartTextures;
        if (core.Index.Length < 0x70 || table.Count is < 1 or > 256 || table.Offset < Rac2LevelCore.CoreHeaderSize ||
            (long)table.Offset + table.Count * 16L > core.Index.Length)
            throw new InvalidDataException("GC Part directory exceeds the level core.");
        int definition = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x6c));
        int bank = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x64));
        if (definition < Rac2LevelCore.CoreHeaderSize || definition > core.Index.Length - 16 || bank < 0 || bank > core.Assets.Length)
            throw new InvalidDataException("GC Part definitions/bank exceed the level core.");
        var definitions = Rac2ParticleDefinitions.Parse(core.Index.AsSpan(definition), table.Count);
        var textures = new Dictionary<int, RuntimeTexture>();
        var layouts = new Dictionary<int, Rac2PartTextureLayout>();
        for (int id = 0; id < table.Count; id++)
        {
            var row = core.Index.AsSpan(table.Offset + id * 16, 16);
            int palette = BinaryPrimitives.ReadInt32LittleEndian(row), pixels = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
            int dimension = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
            if (flags != 0) throw new NotSupportedException("This GC Part texture encoding is outside the measured PSMT8 slice.");
            if (palette < 0 || pixels < 0 || dimension is < 1 or > 1024 || (dimension & (dimension - 1)) != 0 ||
                (long)bank + palette + 1024 > core.Assets.Length || (long)bank + pixels + (long)dimension * dimension > core.Assets.Length)
                throw new InvalidDataException("GC Part pixels/palette exceed the source bank.");
            uint[] colors = Enumerable.Range(0, 256).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(core.Assets.AsSpan(bank + palette + i * 4))).ToArray();
            var image = Ps2Texture.DecodePaletted8(new(dimension, dimension, core.Assets.AsMemory(bank + pixels, dimension * dimension), colors));
            textures.Add(id, new(AssetKind, id, dimension, dimension, image.Rgba));
            layouts.Add(id, new((uint)palette, (uint)pixels, dimension));
        }
        return new(source, definitions, textures) { NativeLayouts = layouts };
    }
}

/// <summary>PSMT8 Part layout, before native 0x3042a8 binds it to a caller-supplied GS bank base.</summary>
public readonly record struct Rac2PartTextureLayout(uint PaletteOffset, uint PixelOffset, int Dimension)
{
    public (uint Palette, uint Pixels) Bind(uint gsBankBase) =>
        (unchecked((gsBankBase + PaletteOffset) << 4), unchecked(((gsBankBase + PixelOffset) << 4) + (uint)System.Numerics.BitOperations.Log2((uint)Dimension)));
}

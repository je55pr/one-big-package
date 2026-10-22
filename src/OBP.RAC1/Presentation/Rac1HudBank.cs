using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;
using OBP.PS2.Textures;
using OBP.Runtime;

namespace OBP.RAC1.Presentation;

/// <summary>Shared native HUD animation/frame/page tables, independently of any host image format.</summary>
public sealed class Rac1HudBank
{
    public sealed record Animation(ushort Id, int FrameCount, int FirstFrame, byte Flags);
    private readonly record struct Page(int Offset, int Width, int Height);
    private readonly record struct Frame(int Palette, int Image);

    private readonly IReadOnlyList<byte[]> _chunks;
    private readonly Frame[] _frames;
    private readonly Page[] _palettes, _images;
    private readonly int[] _paletteEnds, _imageEnds;
    public IReadOnlyDictionary<ushort, Animation> Animations { get; }
    public int FrameCount => _frames.Length;
    public int PalettePageCount => _palettes.Length;
    public int ImagePageCount => _images.Length;

    private Rac1HudBank(IReadOnlyList<byte[]> chunks, Dictionary<ushort, Animation> animations,
        Frame[] frames, Page[] palettes, Page[] images, int[] paletteEnds, int[] imageEnds)
    {
        _chunks = chunks; Animations = animations; _frames = frames;
        _palettes = palettes; _images = images; _paletteEnds = paletteEnds; _imageEnds = imageEnds;
    }

    public static Rac1HudBank Read(IRandomAccessReader disc, byte[] discIndex)
    {
        var chunks = new byte[6][];
        for (int i = 0; i < chunks.Length; i++)
        {
            byte[] raw = Rac1UiAssets.ReadArchive(disc, discIndex, 0x14f8 + i * 8, 8 * 1024 * 1024);
            chunks[i] = i == 0 ? raw : WadLz.Decompress(raw, 32 * 1024 * 1024).Data;
        }
        return Decode(chunks);
    }

    public static Rac1HudBank Decode(IReadOnlyList<byte[]> chunks)
    {
        if (chunks.Count != 6 || chunks[0].Length is < 0x100 or > 256 * 1024)
            throw new InvalidDataException("R&C1 HUD bank has an invalid header/chunk count.");
        var header = chunks[0];
        int a = Offset(header, 4), f = Offset(header, 8), p = Offset(header, 12), im = Offset(header, 16);
        if (a < 0xb0 || a >= f || f >= p || p >= im || im > header.Length || (f - a) % 8 != 0 || (p - f) % 4 != 0)
            throw new InvalidDataException("R&C1 HUD tables are out of order or misaligned.");
        int[] paletteEnds = BankEnds(header, 0x14), imageEnds = BankEnds(header, 0x34);
        int paletteCount = paletteEnds.Max(), imageCount = imageEnds.Max();
        if ((long)p + paletteCount * 8L > im || (long)im + imageCount * 8L > header.Length)
            throw new InvalidDataException("R&C1 HUD pages exceed the header.");

        var frames = new Frame[(p - f) / 4];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new(U16(header, f + i * 4), U16(header, f + i * 4 + 2));
        var palettes = Enumerable.Range(0, paletteCount).Select(i => ReadPage(header, p + i * 8)).ToArray();
        var images = Enumerable.Range(0, imageCount).Select(i => ReadPage(header, im + i * 8)).ToArray();
        var animations = new Dictionary<ushort, Animation>();
        bool sentinel = false;
        for (int at = a; at < f; at += 8)
        {
            ushort id = U16(header, at);
            if (id == 0xffff) { sentinel = true; break; }
            int count = U16(header, at + 2), start = U16(header, at + 4);
            if (count == 0 || start + count > frames.Length || !animations.TryAdd(id, new(id, count, start, header[at + 6])))
                throw new InvalidDataException("R&C1 HUD animation has invalid or duplicate frame references.");
            for (int i = start; i < start + count; i++)
            {
                var frame = frames[i];
                if (frame.Palette >= paletteCount || frame.Image >= imageCount)
                    throw new InvalidDataException("R&C1 HUD frame references an absent page.");
                ValidatePage(chunks, paletteEnds, frame.Palette, palettes[frame.Palette], 1024);
                var image = images[frame.Image];
                ValidatePage(chunks, imageEnds, frame.Image, image, image.Width * image.Height);
            }
        }
        if (!sentinel) throw new InvalidDataException("R&C1 HUD animation sentinel is missing.");
        return new(chunks, animations, frames, palettes, images, paletteEnds, imageEnds);
    }

    /// <summary>Frame identities are animation-local; an absent animation/variant is not a substitute icon.</summary>
    public RuntimeTexture? DecodeFrame(ushort animationId, int localFrame = 0)
    {
        if (!Animations.TryGetValue(animationId, out var animation) || (uint)localFrame >= animation.FrameCount) return null;
        int frameIndex = animation.FirstFrame + localFrame;
        var frame = _frames[frameIndex];
        var palette = _palettes[frame.Palette]; var image = _images[frame.Image];
        byte[] paletteData = _chunks[ChunkFor(_paletteEnds, frame.Palette)];
        uint[] colors = Enumerable.Range(0, 256).Select(i =>
            BinaryPrimitives.ReadUInt32LittleEndian(paletteData.AsSpan(palette.Offset + i * 4, 4))).ToArray();
        var pixels = _chunks[ChunkFor(_imageEnds, frame.Image)].AsMemory(image.Offset, image.Width * image.Height);
        var decoded = Ps2Texture.DecodePaletted8(new(image.Width, image.Height, pixels, colors));
        return new("hud", frameIndex, image.Width, image.Height, decoded.Rgba);
    }

    private static void ValidatePage(IReadOnlyList<byte[]> chunks, int[] ends, int index, Page page, int length)
    {
        byte[] data = chunks[ChunkFor(ends, index)];
        if (page.Offset > data.Length || length > data.Length - page.Offset)
            throw new InvalidDataException("R&C1 HUD image/palette lies outside its texture bank.");
    }

    private static int ChunkFor(int[] ends, int index)
    {
        // Resident bank 0 and streamed banks 1/4 are backed by chunks 1/2/5.
        int bank = Array.FindIndex(ends, end => end != 0 && index < end);
        return bank switch { 0 => 1, 1 => 2, 4 => 5, _ => throw new InvalidDataException("Unsupported R&C1 HUD texture bank.") };
    }

    private static int[] BankEnds(byte[] data, int at)
    {
        int[] ends = Enumerable.Range(0, 8).Select(i => Offset(data, at + i * 4)).ToArray();
        int previous = 0;
        foreach (int end in ends)
        {
            if (end != 0 && (end < previous || end > 65536)) throw new InvalidDataException("R&C1 HUD bank ends are invalid.");
            if (end != 0) previous = end;
        }
        return ends;
    }

    private static Page ReadPage(byte[] data, int at)
    {
        uint address = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        int w = data[at + 6], h = data[at + 7];
        if (w > 11 || h > 11) throw new InvalidDataException("R&C1 HUD page dimensions are invalid.");
        return new((int)(address & 0x7fffffff), 1 << w, 1 << h);
    }

    private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
    private static int Offset(byte[] data, int at)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        return value <= int.MaxValue ? (int)value : throw new InvalidDataException("R&C1 HUD table offset exceeds its bounds.");
    }
}

using System.Buffers.Binary;
using OBP.PS2.Textures;

namespace OBP.PS2.Graphics;

/// <summary>Shared native HUD animation/frame/page tables, independently of any host image format.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "S3453", Justification = "Only Decode may publish a bank, after validating its source tables and page ranges.")]
public sealed class Ps2HudSpriteBank
{
    public sealed record Animation(ushort Id, int FrameCount, int FirstFrame, byte Flags);
    private readonly record struct Page(int DataOffset, int Width, int Height);
    private readonly record struct Frame(int Palette, int Image);

    private readonly IReadOnlyList<byte[]> _chunks;
    private readonly Frame[] _frames;
    private readonly Page[] _palettes, _images;
    private readonly int[] _paletteEnds, _imageEnds;
    private readonly int[] _bankChunks;
    private readonly Dictionary<ushort, Animation> _animations = [];
    public IReadOnlyDictionary<ushort, Animation> Animations => _animations;
    public int FrameCount => _frames.Length;
    public int PalettePageCount => _palettes.Length;
    public int ImagePageCount => _images.Length;

    private Ps2HudSpriteBank(IReadOnlyList<byte[]> chunks, Frame[] frames, Page[] palettes, Page[] images,
        int[] paletteEnds, int[] imageEnds, int[] bankChunks)
    {
        _chunks = chunks; _frames = frames; _bankChunks = bankChunks;
        _palettes = palettes; _images = images; _paletteEnds = paletteEnds; _imageEnds = imageEnds;
    }

    public static Ps2HudSpriteBank Decode(IReadOnlyList<byte[]> chunks) => Decode(chunks, [1, 2, 3, 4, 5, -1, -1, -1]);

    public static Ps2HudSpriteBank Decode(IReadOnlyList<byte[]> chunks, IReadOnlyList<int> bankChunks)
    {
        if (chunks.Count != 6 || chunks[0].Length is < 0x100 or > 256 * 1024)
            throw new InvalidDataException("PS2 HUD bank has an invalid header/chunk count.");
        if (bankChunks.Count != 8 || bankChunks.Any(chunk => chunk != -1 && (chunk < 1 || chunk >= chunks.Count)))
            throw new ArgumentException("HUD banks need eight validated source-specific chunk bindings.", nameof(bankChunks));
        var header = chunks[0];
        int a = Offset(header, 4), f = Offset(header, 8), p = Offset(header, 12), im = Offset(header, 16);
        if (a < 0xb0 || a >= f || f >= p || p >= im || im > header.Length || (f - a) % 8 != 0 || (p - f) % 4 != 0)
            throw new InvalidDataException("PS2 HUD tables are out of order or misaligned.");
        int[] paletteEnds = BankEnds(header, 0x14), imageEnds = BankEnds(header, 0x34);
        int paletteCount = paletteEnds.Max(), imageCount = imageEnds.Max();
        if ((long)p + paletteCount * 8L > im || (long)im + imageCount * 8L > header.Length)
            throw new InvalidDataException("PS2 HUD pages exceed the header.");

        var frames = new Frame[(p - f) / 4];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new(U16(header, f + i * 4), U16(header, f + i * 4 + 2));
        var palettes = Enumerable.Range(0, paletteCount).Select(i => ReadPage(header, p + i * 8)).ToArray();
        var images = Enumerable.Range(0, imageCount).Select(i => ReadPage(header, im + i * 8)).ToArray();
        var bank = new Ps2HudSpriteBank(chunks, frames, palettes, images, paletteEnds, imageEnds, bankChunks.ToArray());
        bank.ReadAnimations(header, a, f);
        return bank;
    }

    private void ReadAnimations(byte[] header, int startOffset, int endOffset)
    {
        bool sentinel = false;
        for (int at = startOffset; at < endOffset; at += 8)
        {
            ushort id = U16(header, at);
            if (id == 0xffff) { sentinel = true; break; }
            int count = U16(header, at + 2), start = U16(header, at + 4);
            if (count == 0 || start + count > _frames.Length || !_animations.TryAdd(id, new(id, count, start, header[at + 6])))
                throw new InvalidDataException("PS2 HUD animation has invalid or duplicate frame references.");
            for (int i = start; i < start + count; i++) ValidateFrame(_frames[i]);
        }
        if (!sentinel) throw new InvalidDataException("PS2 HUD animation sentinel is missing.");
    }

    private void ValidateFrame(Frame frame)
    {
        if (frame.Palette >= _palettes.Length || frame.Image >= _images.Length)
            throw new InvalidDataException("PS2 HUD frame references an absent page.");
        ValidatePage(_paletteEnds, frame.Palette, _palettes[frame.Palette], 1024);
        var image = _images[frame.Image];
        ValidatePage(_imageEnds, frame.Image, image, image.Width * image.Height);
    }

    /// <summary>Frame identities are animation-local; an absent animation/variant is not a substitute icon.</summary>
    public Ps2Texture.Decoded? DecodeFrame(ushort animationId) => DecodeFrame(animationId, 0);

    public Ps2Texture.Decoded? DecodeFrame(ushort animationId, int localFrame)
    {
        if (!Animations.TryGetValue(animationId, out var animation) || (uint)localFrame >= animation.FrameCount) return null;
        int frameIndex = animation.FirstFrame + localFrame;
        var frame = _frames[frameIndex];
        var palette = _palettes[frame.Palette]; var image = _images[frame.Image];
        byte[] paletteData = _chunks[ChunkFor(_paletteEnds, frame.Palette)];
        uint[] colors = Enumerable.Range(0, 256).Select(i =>
            BinaryPrimitives.ReadUInt32LittleEndian(paletteData.AsSpan(palette.DataOffset + i * 4, 4))).ToArray();
        var pixels = _chunks[ChunkFor(_imageEnds, frame.Image)].AsMemory(image.DataOffset, image.Width * image.Height);
        var decoded = Ps2Texture.DecodePaletted8(new(image.Width, image.Height, pixels, colors));
        return new(image.Width, image.Height, decoded.Rgba);
    }

    private void ValidatePage(int[] ends, int index, Page page, int length)
    {
        byte[] data = _chunks[ChunkFor(ends, index)];
        if (page.DataOffset > data.Length || length > data.Length - page.DataOffset)
            throw new InvalidDataException("PS2 HUD image/palette lies outside its texture bank.");
    }

    private int ChunkFor(int[] ends, int index)
    {
        int bank = Array.FindIndex(ends, end => end != 0 && index < end);
        return bank >= 0 && _bankChunks[bank] >= 1 ? _bankChunks[bank] : throw new InvalidDataException("Unsupported source HUD texture bank.");
    }

    private static int[] BankEnds(byte[] data, int at)
    {
        int[] ends = Enumerable.Range(0, 8).Select(i => Offset(data, at + i * 4)).ToArray();
        int previous = 0;
        foreach (int end in ends)
        {
            if (end != 0 && (end < previous || end > 65536)) throw new InvalidDataException("PS2 HUD bank ends are invalid.");
            if (end != 0) previous = end;
        }
        return ends;
    }

    private static Page ReadPage(byte[] data, int at)
    {
        uint address = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        int w = data[at + 6], h = data[at + 7];
        if (w > 11 || h > 11) throw new InvalidDataException("PS2 HUD page dimensions are invalid.");
        return new((int)(address & 0x7fffffff), 1 << w, 1 << h);
    }

    private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
    private static int Offset(byte[] data, int at)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        return value <= int.MaxValue ? (int)value : throw new InvalidDataException("PS2 HUD table offset exceeds its bounds.");
    }
}

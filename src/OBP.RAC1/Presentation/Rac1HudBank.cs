using OBP.IO;
using OBP.PS2.Compression;
using OBP.PS2.Graphics;
using OBP.Runtime;

namespace OBP.RAC1.Presentation;

/// <summary>RAC1 source-owned global HUD lookup over the shared PS2 bitmap codec.</summary>
public sealed class Rac1HudBank
{
    public sealed record Animation(ushort Id, int FrameCount, int FirstFrame, byte Flags);
    private readonly Ps2HudSpriteBank _bank;
    public IReadOnlyDictionary<ushort, Animation> Animations { get; }
    public int FrameCount => _bank.FrameCount;
    public int PalettePageCount => _bank.PalettePageCount;
    public int ImagePageCount => _bank.ImagePageCount;

    private Rac1HudBank(Ps2HudSpriteBank bank)
    {
        _bank = bank;
        Animations = bank.Animations.ToDictionary(pair => pair.Key, pair =>
            new Animation(pair.Value.Id, pair.Value.FrameCount, pair.Value.FirstFrame, pair.Value.Flags));
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

    public static Rac1HudBank Decode(IReadOnlyList<byte[]> chunks) => new(Ps2HudSpriteBank.Decode(chunks));

    public RuntimeTexture? DecodeFrame(ushort animationId, int localFrame = 0) =>
        _bank.DecodeFrame(animationId, localFrame) is { } texture
            ? new("hud", _bank.Animations[animationId].FirstFrame + localFrame, texture.Width, texture.Height, texture.Rgba) : null;
}

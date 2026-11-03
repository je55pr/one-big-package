using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Presentation;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1UiAssetsTests
{
    [Fact]
    public void HudFramesUseTheirOwnPaletteAndImageBanksAndTheSharedPs2Codec()
    {
        var bank = Rac1HudBank.Decode(SyntheticHud());
        Assert.Equal(2, bank.Animations.Count);
        Assert.Equal(2, bank.FrameCount);
        Assert.Equal(2, bank.PalettePageCount);
        Assert.Equal(2, bank.ImagePageCount);
        var first = bank.DecodeFrame(0x7544)!;
        Assert.Equal((4, 2), (first.Width, first.Height));
        Assert.Equal(new byte[] { 10, 20, 30, 128 }, first.Rgba[..4]);
        var streamed = bank.DecodeFrame(0x7580)!;
        Assert.Equal((8, 1), (streamed.Width, streamed.Height));
        Assert.Equal(new byte[] { 40, 50, 60, 255 }, streamed.Rgba[..4]);
        Assert.Null(bank.DecodeFrame(0x1234));
        Assert.Null(bank.DecodeFrame(0x7544, -1));
        Assert.Null(bank.DecodeFrame(0x7544, 1));
    }

    [Theory]
    [InlineData(8, 0xb4)] // wrong table order
    [InlineData(0x18, 0)] // palette bank 1 absent
    [InlineData(0x18, 65537)] // unbounded page count
    [InlineData(0xe4, 0x7fffffff)] // image offset beyond bank
    public void HudRejectsInvalidTablesAndBankRanges(int offset, uint value)
    {
        var chunks = SyntheticHud();
        U32(chunks[0], offset, value);
        Assert.Throws<InvalidDataException>(() => Rac1HudBank.Decode(chunks));
    }

    [Fact]
    public void HudRejectsMissingSentinelAbsentPagesAndTruncatedStreamedData()
    {
        var chunks = SyntheticHud();
        U16(chunks[0], 0xc4, 0x1234);
        Assert.Throws<InvalidDataException>(() => Rac1HudBank.Decode(chunks));
        chunks = SyntheticHud(); U16(chunks[0], 0xd2, 2);
        Assert.Throws<InvalidDataException>(() => Rac1HudBank.Decode(chunks));
        chunks = SyntheticHud(); chunks[5] = [];
        Assert.Throws<InvalidDataException>(() => Rac1HudBank.Decode(chunks));
        chunks = SyntheticHud(); chunks[0][0xea] = 12;
        Assert.Throws<InvalidDataException>(() => Rac1HudBank.Decode(chunks));
    }

    [Fact]
    public void FontsRetainNativeSignedOffsetsAndAdvancesAndRejectInvalidCells()
    {
        var atlas = new RuntimeTexture("fx", 1, 256, 128, new byte[256 * 128 * 4]);
        var metrics = new byte[Rac1BitmapFont.GlyphCount * 4];
        metrics[65 * 4] = 224; metrics[65 * 4 + 1] = 112;
        metrics[65 * 4 + 2] = 253; metrics[65 * 4 + 3] = 14;
        var font = Rac1BitmapFont.Decode("regular", atlas, metrics);
        Assert.Equal(232, font.Glyphs.Count);
        Assert.Equal(new Rac1BitmapFont.Glyph(65, 224, 112, -3, 14), font.Glyphs[65]);
        Assert.True(font.Glyphs[65].Printable);
        Assert.False(font.Glyphs[0].Printable);
        metrics[65 * 4] = 250;
        Assert.Throws<InvalidDataException>(() => Rac1BitmapFont.Decode("regular", atlas, metrics));
        Assert.Throws<InvalidDataException>(() => Rac1BitmapFont.Decode("regular", atlas, metrics[..^1]));
    }

    [Fact]
    public void LocalisationPreservesControlBytesAndEmptyLanguageSlots()
    {
        var tables = Rac1UiAssets.DecodeLanguages(SyntheticLanguages());
        Assert.Equal(6, tables.Count);
        Assert.Equal(new byte[] { (byte)'A', 8, 0xe1 }, tables[0][20196]);
        Assert.All(tables.Skip(1), table => Assert.Empty(table));
    }

    [Theory]
    [InlineData(0, 0)] // table in the offset header
    [InlineData(0, uint.MaxValue)]
    [InlineData(24, 5001)] // unbounded record count
    [InlineData(28, uint.MaxValue)] // table outside archive
    [InlineData(32, 8)] // text in the record table
    [InlineData(32, 28)] // text at end of language
    public void LocalisationRejectsInvalidPointersAndCounts(int offset, uint value)
    {
        byte[] archive = SyntheticLanguages();
        U32(archive, offset, value);
        Assert.Throws<InvalidDataException>(() => Rac1UiAssets.DecodeLanguages(archive));
    }

    [Fact]
    public void LocalisationDoesNotReadATerminatorFromTheNextLanguage()
    {
        byte[] archive = SyntheticLanguages(); archive[51] = 1;
        Assert.Throws<InvalidDataException>(() => Rac1UiAssets.DecodeLanguages(archive));
    }

    [SkippableFact]
    public void RetailUiDecodesAllFramesFontsAndLanguagesDirectlyFromIso()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var assets = Rac1UiAssets.Read(disc);
        Assert.Equal(56, assets.Hud.Animations.Count);
        Assert.Equal(326, assets.Hud.FrameCount);
        Assert.Equal(42, assets.Hud.PalettePageCount);
        Assert.Equal(236, assets.Hud.ImagePageCount);
        Assert.Equal(new[] { 1762, 0, 1739, 1736, 1739, 1736 }, assets.Languages.Select(l => l.Count));
        Assert.Equal("Quick Select", assets.Text(20196));
        Assert.Null(assets.Text(uint.MaxValue));
        Assert.Null(assets.Text(20196, -1));
        Assert.Equal(3, assets.Fonts.Count);
        var atlasHashes = new Dictionary<string, string>
        {
            ["regular"] = "ecfbe45c34d4b14090d8b4b2d07c8e867e474846ab9e1bb144fb58ba6ecf788c",
            ["small"] = "15f068899801a0f210b9b9d3be2c72a811eaf60b25a4b87e0167c9eaa99e758f",
            ["large"] = "bc36ce9b83b22ce175242a44604e7835caf9e9ba9233c0265fabf56b3c851832",
        };
        foreach (var font in assets.Fonts.Values)
        {
            Assert.Equal(232, font.Glyphs.Count);
            Assert.Equal((256, 128), (font.Atlas.Width, font.Atlas.Height));
            Assert.True(font.Glyphs['A'].Printable);
            Assert.Equal(atlasHashes[font.Style], Convert.ToHexString(SHA256.HashData(font.Atlas.Rgba)).ToLowerInvariant());
        }
        // Decoder-oracle hashes are generated independently by the research
        // extractor, retaining no image, text table or executable payload here.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var identity = new byte[8];
        foreach (var animation in assets.Hud.Animations.Values.OrderBy(a => a.Id))
            for (int frame = 0; frame < animation.FrameCount; frame++)
            {
                var texture = assets.Hud.DecodeFrame(animation.Id, frame);
                Assert.NotNull(texture);
                Assert.Equal(texture!.Width * texture.Height * 4, texture.Rgba.Length);
                U16(identity, 0, animation.Id); U16(identity, 2, (ushort)frame);
                U16(identity, 4, (ushort)texture.Width); U16(identity, 6, (ushort)texture.Height);
                hash.AppendData(identity); hash.AppendData(texture.Rgba);
            }
        Assert.Equal("1d5b0d7be410aed4ef0b27f03828db0eaf708ab108f4562c446651383ee3c346",
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        Assert.Equal(14, assets.Fonts["regular"].Glyphs['A'].Advance);
    }

    private static byte[][] SyntheticHud()
    {
        byte[][] chunks = [new byte[0x100], new byte[1032], new byte[1024], [], [], new byte[8]];
        var h = chunks[0];
        U32(h, 4, 0xb4); U32(h, 8, 0xcc); U32(h, 12, 0xd4); U32(h, 16, 0xe4);
        U32(h, 0x14, 1); U32(h, 0x18, 2);
        U32(h, 0x34, 1); U32(h, 0x44, 2);
        U16(h, 0xb4, 0x7544); U16(h, 0xb6, 1);
        U16(h, 0xbc, 0x7580); U16(h, 0xbe, 1); U16(h, 0xc0, 1);
        U16(h, 0xc4, 0xffff);
        U16(h, 0xd0, 1); U16(h, 0xd2, 1);
        U32(h, 0xd4, 0x80000000); U32(h, 0xdc, 0x80000000);
        U32(h, 0xe4, 0x80000400); h[0xea] = 2; h[0xeb] = 1;
        U32(h, 0xec, 0x80000000); h[0xf2] = 3;
        U32(chunks[1], 16 * 4, 0x401e140a); chunks[1].AsSpan(1024).Fill(8);
        U32(chunks[2], 16 * 4, 0x803c3228); chunks[5].AsSpan().Fill(8);
        return chunks;
    }

    private static byte[] SyntheticLanguages()
    {
        var archive = new byte[60];
        U32(archive, 0, 24);
        for (int i = 1; i < 6; i++) U32(archive, i * 4, 52);
        U32(archive, 24, 1); U32(archive, 28, 28);
        U32(archive, 32, 24); U32(archive, 36, 20196);
        archive[48] = (byte)'A'; archive[49] = 8; archive[50] = 0xe1;
        U32(archive, 56, 8);
        return archive;
    }

    private static void U32(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
    private static void U16(byte[] data, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at, 2), value);
}

using System.Text;
using OBP.IO;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1MenuRootTextLayoutTests
{
    private static IReadOnlyList<Rac1BitmapFont.Glyph> TestFont()
    {
        var font = Enumerable.Range(0, Rac1BitmapFont.GlyphCount)
            .Select(code => new Rac1BitmapFont.Glyph(code, 2, 4, 0, 0)).ToArray();
        font['W'] = new('W', 10, 20, 0, 16);
        font['e'] = new('e', 26, 20, 3, 12);
        font[' '] = new(' ', 0, 0, 0, 5);
        return font;
    }

    [Fact]
    public void ASourceGlyphCellIsPositionedFromNativePenAndYOffset()
    {
        var result = Rac1MenuRootTextLayout.Layout("We "u8, TestFont(), 240, 40);
        Assert.Equal(104, result.Left); // width 16+12+5=33, integer midpoint 120-16
        Assert.Equal(12, result.AnchorY); // 40/2 - 14/2 - 1
        Assert.Equal(33, result.MeasuredWidth);
        Assert.Equal([
            new Rac1MenuRootTextLayout.GlyphQuad((byte)'W', 104, 12, 10, 20),
            new Rac1MenuRootTextLayout.GlyphQuad((byte)'e', 120, 15, 26, 20),
        ], result.Quads);
    }

    [Fact]
    public void NativeRootLayoutUsesIntegerMidpointsAndValidatesSupportedSourceBytes()
    {
        var glyphs = TestFont();
        var odd = Rac1MenuRootTextLayout.Layout("W"u8, glyphs, 201, 39);
        Assert.Equal(92, odd.Left);
        Assert.Equal(11, odd.AnchorY);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1MenuRootTextLayout.Layout("W"u8, glyphs, 0, 40));
        Assert.Throws<InvalidDataException>(() =>
            Rac1MenuRootTextLayout.Layout(new byte[] { 0x0E }, glyphs, 200, 40));
        Assert.Throws<InvalidDataException>(() =>
            Rac1MenuRootTextLayout.Layout(new byte[] { 0x80 }, glyphs, 200, 40));
        var terminated = Rac1MenuRootTextLayout.Layout(new byte[] { (byte)'W', 0, 0x80 },
            glyphs, 200, 40);
        Assert.Single(terminated.Quads);
    }

    [Fact]
    public void RetailRootFontMetricsDriveTheActualGlyphQuads()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var assets = Rac1UiAssets.Read(disc);
        var font = assets.Fonts["large"].Glyphs;
        var source = assets.NativeMenuPages!.NativeRootEntries;
        int[] expectedWidths = [87, 84, 118, 52, 43, 77];
        for (int slot = 0; slot < 6; slot++)
        {
            string text = assets.Text((uint)source[slot].TextId)!;
            var quads = Rac1MenuRootTextLayout.Layout(
                Encoding.Latin1.GetBytes(text), font, 240, 40);
            Assert.Equal(expectedWidths[slot], quads.MeasuredWidth);
            Assert.Equal(120 - (expectedWidths[slot] >> 1), quads.Left);
            Assert.Equal(12, quads.AnchorY);
            Assert.Equal(text.Count(c => c != ' '), quads.Quads.Count);
            Assert.All(quads.Quads, q =>
            {
                Assert.Equal(font[q.Code].U, q.U);
                Assert.Equal(font[q.Code].V, q.V);
                Assert.InRange(q.Y, 12, 15);
            });
        }
    }
}

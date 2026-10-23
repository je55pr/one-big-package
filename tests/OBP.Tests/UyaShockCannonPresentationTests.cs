using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime.Presentation;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public sealed class UyaShockCannonPresentationTests
{
    [Fact]
    public void BeamUsesFourNativeTexturedLayersWithSourcePoleAndAlternatingRingStrips()
    {
        var session = new UyaShockCannonSession(); session.Equip();
        UyaShockCannonFrame frame = session.Advance(new(true));
        for (int i = 1; i < 79; i++) frame = session.Advance(new(true));
        var scroll = new UyaShockCannonBeam.Scroll().Advance();
        var layers = UyaShockCannonBeam.Build(frame, new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, new(2, 3, 4)), scroll);
        Assert.Equal(new[] { 23, 23, 23, 10 }, layers.Select(l => l.FxTextureId));
        Assert.Equal(new[] { 8, 8, 8, 6 }, layers.Select(l => l.RingCount));
        Assert.Equal(new uint[] { 0x3a804020, 0x3a801040, 0x3a804010, 0x1dd20a40 }, layers.Select(l => l.NativeRgba));
        Assert.Equal(600, layers.Sum(l => l.Mesh.TriangleCount));
        foreach (var layer in layers)
        {
            Assert.Equal(UyaFxTextures.AssetKind, layer.Mesh.AssetKind);
            Assert.Equal(RuntimeBlendEquation.AdditiveSourceAlpha, layer.Mesh.MaterialPresentation!.BlendEquation);
            Assert.Equal(RuntimeTextureWrap.Repeat, layer.Mesh.MaterialPresentation.WrapT);
            Assert.All(layer.Mesh.Positions, p => Assert.True(double.IsFinite(p)));
            Assert.Equal(layer.RingCount * 22 * 4, layer.Mesh.Colors!.Length);
        }
        var first = layers[0].Mesh;
        // Original EE/VU oracle: aim +X, muzzle (2,3,4), counter29, width1.1.
        Assert.InRange(first.Positions[0], 1.49999, 1.50001);
        Assert.Equal(4, first.Positions[1]); Assert.Equal(3, first.Positions[2]);
        Assert.InRange(first.Positions[3], 2.23414, 2.23417);
        Assert.InRange(first.Positions[4], 4.24035, 4.24039);
        Assert.InRange(first.Positions[5], 3.24035, 3.24039);
        Assert.Equal(7.15f, first.Uvs[1], 5);
        Assert.Equal(6.15f, first.Uvs[3], 5);
        Assert.Equal(58 / 128f, first.Colors![3]);
        Assert.Equal(28 / 128f, first.Colors[4]);
        Assert.Empty(UyaShockCannonBeam.Build(session.Reset(), new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, Vector3.Zero), scroll));
    }

    [Fact]
    public void PrimitiveKeepsTenPolarStepsAndAlternatesBuffersInsteadOfTwistingUvs()
    {
        var strips = UyaShockCannonBeam.EllipsoidStrips(Matrix4x4.Identity, 8, 0x3a804020, .15f);
        Assert.Equal(8, strips.Length);
        Assert.All(strips, s => Assert.Equal(22, s.Length));
        Assert.Equal(Vector3.UnitZ, strips[0][0].Position);
        Assert.InRange(strips[0][1].Position.X, .3090168f, .3090172f);
        Assert.InRange(strips[0][1].Position.Z, .9510564f, .9510567f);
        Assert.Equal(0x3a70381cu, strips[0][1].Rgba);
        Assert.Equal(strips[0][1], strips[1][1]);
        Assert.Equal(5.15f, strips[1][0].Uv.Y, 5);
        Assert.Equal(0x3a000000u, strips[7][0].Rgba);
        var noFade = UyaShockCannonBeam.EllipsoidStrips(Matrix4x4.Identity, -2, 0x3a804020, 0);
        Assert.Equal(0x3a804020u, noFade[1][0].Rgba);
        var alphaFade = UyaShockCannonBeam.EllipsoidStrips(Matrix4x4.Identity, 2, 0x40804020, 0, additive: false);
        Assert.Equal(0x00804020u, alphaFade[1][0].Rgba);
        Assert.Throws<ArgumentOutOfRangeException>(() => UyaShockCannonBeam.EllipsoidStrips(Matrix4x4.Identity, 0, 0, 0));
    }

    [Fact]
    public void SourceScrollWrapsAndDoesNotBecomeAnElapsedSecondsShaderGuess()
    {
        var scroll = new UyaShockCannonBeam.Scroll(6.9f, 6.95f, 6.99f).Advance();
        Assert.Equal(.05f, scroll.First, 5); Assert.Equal(.05f, scroll.Second, 5); Assert.Equal(.04f, scroll.Third, 5);
    }

    [Fact]
    public void GadgetPifRejectsTruncatedUnsupportedAndOutOfBoundsTextures()
    {
        var bytes = new byte[0x440];
        void I32(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        I32(0, 1); I32(4, 0x10); "2FIP"u8.CopyTo(bytes.AsSpan(0x10));
        I32(0x18, 4); I32(0x1c, 4); I32(0x20, 0x13); I32(0x2c, 1);
        Assert.Equal(4, Assert.Single(UyaGlobalGadget.ReadTextureBundle(bytes)).Width);
        Assert.Throws<InvalidDataException>(() => UyaGlobalGadget.ReadTextureBundle(bytes[..^1]));
        I32(0x20, 0x14);
        Assert.Throws<NotSupportedException>(() => UyaGlobalGadget.ReadTextureBundle(bytes));
        I32(0x20, 0x13); I32(0x18, int.MaxValue);
        Assert.Throws<InvalidDataException>(() => UyaGlobalGadget.ReadTextureBundle(bytes));
    }

    [Theory]
    [InlineData(4, int.MaxValue)] // Payload base escapes the disc.
    [InlineData(8, -1)] // Negative class identity.
    [InlineData(12, 1)] // Half-present model range.
    [InlineData(16, -1)] // Negative sector count.
    public void GadgetDirectoryKeepsRangesInsideTheSource(int word, int value)
    {
        byte[] bytes = new byte[4 * 1024 * 1024];
        int header = UyaDiscIndex.TocLba * UyaDiscIndex.SectorBytes + UyaGlobalGadget.TocHeaderOffset;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header), UyaGlobalGadget.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 4), 1536);
        Assert.Equal(48, UyaGlobalGadget.ReadDirectory(new InMemoryReader("synthetic-disc", bytes)).Count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + word), value);
        Assert.Throws<InvalidDataException>(() => UyaGlobalGadget.ReadDirectory(new InMemoryReader("bad-directory", bytes)));
    }

    [Fact]
    public void FxUsesItsIndependentBankAndRefusesEscapingPixelsOrUnrecoveredFormats()
    {
        var index = new byte[0xcc]; var assets = new byte[0x430];
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0x68), 0x20);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc0), 0x400);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc8), 4);
        var header = new UyaLevelCore.CoreHeader(default, 0, 0, 0, 0, default, default, default,
            default, default, default, default, default, new(1, 0xbc), 0, 0, 0, 0, 0, []);
        var core = new UyaLevelCore.Core(null!, null!, header, index, assets, [], []);
        var textures = UyaFxTextures.Read(core, new HashSet<int> { 0 });
        Assert.Equal((4, 4), (textures[0].Width, textures[0].Height));
        Assert.Throws<InvalidDataException>(() => UyaFxTextures.Read(core, new HashSet<int> { 1 }));
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc0), 0x420);
        Assert.Throws<InvalidDataException>(() => UyaFxTextures.Read(core, new HashSet<int> { 0 }));
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc4), -4);
        Assert.Throws<NotSupportedException>(() => UyaFxTextures.Read(core, new HashSet<int> { 0 }));
    }
}

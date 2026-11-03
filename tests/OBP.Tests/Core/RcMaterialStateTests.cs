using System.Buffers.Binary;
using OBP.PS2.Graphics;

namespace OBP.Tests;

public sealed class RcMaterialStateTests
{
    private static void Ad(byte[] bytes, int offset, int lo, int hi, byte address)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), lo);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), hi);
        bytes[offset + 8] = address;
    }

    [Fact]
    public void ShrubMaterial_DecodesProvenRuntimeFixupFields()
    {
        var bytes = new byte[0x40];
        Ad(bytes, 0x00, unchecked((int)0xffff_ff92), 4, 0x14);
        Ad(bytes, 0x10, 1, 0, 0x08);
        Ad(bytes, 0x20, 7, 0, 0x34);
        Ad(bytes, 0x30, 7, 0, 0x06);

        var material = RcMaterialState.ReadShrub(bytes);

        Assert.Equal(7, material.TextureId);
        Assert.Equal(RcTextureWrapHint.Clamp, material.WrapS);
        Assert.Equal(RcTextureWrapHint.Repeat, material.WrapT);
        Assert.Equal(RcTextureMinFilterHint.LinearMipmapNearest, material.MinFilter);
        Assert.Equal(unchecked((int)0xffff_ff92), material.LodKRaw);
        Assert.Equal(4, material.MinFilterRaw);
        Assert.Equal(1, material.ClampRawLo);
        Assert.Equal(0, material.ClampRawHi);
    }

    [Fact]
    public void TfragAndTie_PreserveUnresolvedSamplerWordsWithoutGuessing()
    {
        var tfrag = new byte[0x50];
        Ad(tfrag, 0x00, 3, 0, 0x06);
        Ad(tfrag, 0x10, 123, 4, 0x14);
        Ad(tfrag, 0x20, 1, 1, 0x08);
        Ad(tfrag, 0x30, 3, 0, 0x34);
        Ad(tfrag, 0x40, 0, 0, 0x36);

        var tf = RcMaterialState.ReadTfrag(tfrag);
        Assert.Equal(3, tf.TextureId);
        Assert.Equal(RcTextureWrapHint.Unknown, tf.WrapS);
        Assert.Equal(RcTextureMinFilterHint.Unknown, tf.MinFilter);
        Assert.Equal((1, 1), (tf.ClampRawLo, tf.ClampRawHi));
        Assert.Equal(4, tf.MinFilterRaw);
        var tie = new byte[0x50];
        Ad(tie, 0x00, 5, 0, 0x06);
        Ad(tie, 0x10, 321, 2, 0x14);
        Ad(tie, 0x20, 5, 0, 0x34);
        Ad(tie, 0x30, 0, 1, 0x08);
        Ad(tie, 0x40, 0, 0, 0x36);

        var ti = RcMaterialState.ReadTie(tie);
        Assert.Equal(5, ti.TextureId);
        Assert.Equal(RcTextureWrapHint.Unknown, ti.WrapT);
        Assert.Equal(RcTextureMinFilterHint.Unknown, ti.MinFilter);
        Assert.Equal((0, 1), (ti.ClampRawLo, ti.ClampRawHi));
        Assert.Equal(2, ti.MinFilterRaw);
    }

    [Theory]
    [InlineData(0, RcMobySurfaceEffect.RegularTexture)]
    [InlineData(15, RcMobySurfaceEffect.RegularTexture)]
    [InlineData(-1, RcMobySurfaceEffect.None)]
    [InlineData(-2, RcMobySurfaceEffect.Chrome)]
    [InlineData(-3, RcMobySurfaceEffect.Glass)]
    [InlineData(-4, RcMobySurfaceEffect.Unknown)]
    public void MobyTextureSelector_ClassifiesSpecialSurfaces(int textureId, RcMobySurfaceEffect expected)
    {
        Assert.Equal(expected, RcMobyMaterial.ClassifyTextureId(textureId));
    }
    [Fact]
    public void MobyMaterial_DecodesFilterAndPreservesUnresolvedClamp()
    {
        var bytes = new byte[0x40];
        Ad(bytes, 0x00, unchecked((int)0xffff_ff92), 4, 0x14);
        Ad(bytes, 0x10, 1, 0, 0x08);
        Ad(bytes, 0x20, -2, 0, 0x06);
        Ad(bytes, 0x30, 0, 0, 0x34);

        var material = RcMaterialState.ReadMoby(bytes);

        Assert.Equal(-2, material.TextureId);
        Assert.Equal(RcMobySurfaceEffect.Chrome, RcMobyMaterial.ClassifyTextureId(material.TextureId));
        Assert.Equal(RcTextureMinFilterHint.LinearMipmapNearest, material.MinFilter);
        Assert.Equal(RcTextureWrapHint.Unknown, material.WrapS);
        Assert.Equal((1, 0), (material.ClampRawLo, material.ClampRawHi));
    }

    [Fact]
    public void GifPrimitiveState_DecodesPreAndAlphaBlendEnable()
    {
        const int prim =
            0b100 |
            (1 << 3) |
            (1 << 4) |
            (1 << 5) |
            (1 << 6);
        ulong tagLow = (1UL << 46) | ((ulong)prim << 47);

        var state = RcGifPrimitiveState.Decode(tagLow);

        Assert.True(state.Pre);
        Assert.Equal(0b100, state.PrimitiveType);
        Assert.True(state.Gouraud);
        Assert.True(state.Textured);
        Assert.True(state.Fogged);
        Assert.True(state.AlphaBlendEnabled);
    }

    [Fact]
    public void MaterialDecoder_PreservesRuntimeAddressBytesWithoutRejecting()
    {
        var bytes = new byte[0x40];
        Ad(bytes, 0x00, 0, 4, 0x04);
        Ad(bytes, 0x10, 1, 0, 0x18);
        Ad(bytes, 0x20, -2, 0, 0x06);
        Ad(bytes, 0x30, 0, 0, 0x34);

        var material = RcMaterialState.ReadMoby(bytes);

        Assert.Equal(0x04, material.Tex1Address);
        Assert.Equal(0x18, material.ClampAddress);
        Assert.Equal(0x06, material.Tex0Address);
        Assert.Equal((byte?)0x34, material.Extra0Address);
    }

    [Theory]
    [InlineData(0, 1, 0, 1, 0, RcBlendModeHint.SourceAlpha)]
    [InlineData(0, 2, 0, 1, 0, RcBlendModeHint.AdditiveSourceAlpha)]
    [InlineData(0, 1, 2, 1, 64, RcBlendModeHint.FixedAlpha)]
    [InlineData(0, 2, 2, 1, 128, RcBlendModeHint.AdditiveFixedAlpha)]
    public void TieAlphaSlot_DecodesKnownGsEquation(
        int a, int b, int c, int e, int fix, RcBlendModeHint expected)
    {
        var bytes = new byte[0x50];
        Ad(bytes, 0x00, 2, 0, 0x06);
        Ad(bytes, 0x10, 0, 0, 0x14);
        Ad(bytes, 0x20, 0, 0, 0x34);
        Ad(bytes, 0x30, 0, 0, 0x08);
        int lo = a | (b << 2) | (c << 4) | (e << 6);
        Ad(bytes, 0x40, lo, fix, 0x42);

        var alpha = Assert.IsType<RcGsAlphaState>(RcMaterialState.ReadTie(bytes).AlphaBlend);
        Assert.Equal(expected, alpha.Hint);
        Assert.Equal(fix, alpha.Fix);
    }
}

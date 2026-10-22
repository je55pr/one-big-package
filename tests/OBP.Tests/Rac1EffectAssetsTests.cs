using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1EffectAssetsTests
{
    private static Rac1LevelCore.Core FxCore()
    {
        var index = new byte[0x100]; var assets = new byte[0x800];
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0x68), 0x100);
        // Fx records are palette/pixels/width/height, with an independent base.
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc4), 1024);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xc8), 16);
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(0xcc), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(assets.AsSpan(0x104), 0x80804020);
        assets.AsSpan(0x500, 128).Fill(1);
        return new(index, assets, [], new([], 0, 0, 0, 0, 0, 0, assets.Length, 0, 0,
            default, default, default, default, default, new(1, 0xc0)));
    }

    [Fact]
    public void FxUsesTheIndependentBankAndPreservesRectangularDimensions()
    {
        var texture = Rac1EffectAssets.ReadFxTextures(FxCore(), new HashSet<int> { 0 })[0];
        Assert.Equal("fx", texture.AssetKind); Assert.Equal(16, texture.Width); Assert.Equal(8, texture.Height);
        Assert.Equal(16 * 8 * 4, texture.Rgba.Length);
        Assert.Equal(new byte[] { 32, 64, 128, 255 }, texture.Rgba[..4]);
    }

    [Fact]
    public void FxRejectsAbsentIdsAndOutOfBankPixelsAndPalette()
    {
        var core = FxCore();
        Assert.Throws<InvalidDataException>(() => Rac1EffectAssets.ReadFxTextures(core, new HashSet<int> { 1 }));
        BinaryPrimitives.WriteInt32LittleEndian(core.Index.AsSpan(0xc4), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => Rac1EffectAssets.ReadFxTextures(core, new HashSet<int> { 0 }));
        core = FxCore(); BinaryPrimitives.WriteInt32LittleEndian(core.Index.AsSpan(0xc0), -1);
        Assert.Throws<InvalidDataException>(() => Rac1EffectAssets.ReadFxTextures(core, new HashSet<int> { 0 }));
    }
}

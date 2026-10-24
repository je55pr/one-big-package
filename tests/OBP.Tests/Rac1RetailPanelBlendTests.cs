using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1RetailPanelBlendTests
{
    [Theory]
    [InlineData(1, -2)]
    [InlineData(75, -2)]
    [InlineData(76, -1)]
    [InlineData(150, -1)]
    [InlineData(151, 0)]
    [InlineData(365, 0)]
    public void NativeSurfaceInsetFollowsBothThresholds(int projected, int expected) =>
        Assert.Equal(expected, Rac1RetailPanelBlend.InnerSizeAdjustment(projected));

    [Fact]
    public void FlashIsRareEventWithNativeTwoTickRecurrence()
    {
        var flash = new Rac1RetailPanelBlend.Flash(0, false);
        Assert.Equal(0f, flash.AdditiveGain);
        Assert.False(flash.Step(false).Active);
        flash = flash.Step(true);
        Assert.True(flash.Active);
        Assert.Equal(0, flash.Time);
        Assert.Equal(1f, flash.AdditiveGain);
        for (int i = 0; i < 64; i++) flash = flash.Step(false);
        Assert.Equal(128, flash.Time);
        Assert.Equal(1f, flash.AdditiveGain);
        for (int i = 0; i < 32; i++) flash = flash.Step(false);
        Assert.Equal(192, flash.Time);
        Assert.Equal(1f, flash.AdditiveGain);
        for (int i = 0; i < 16; i++) flash = flash.Step(false);
        Assert.Equal(224, flash.Time);
        Assert.Equal(.5f, flash.AdditiveGain);
        for (int i = 0; i < 16; i++) flash = flash.Step(false);
        Assert.False(flash.Active);
        Assert.Equal(256, flash.Time);
        Assert.Equal(0f, flash.AdditiveGain);
    }

    [Fact]
    public void SourceTintMultipliersAreMeasuredInPs2ColorSpace()
    {
        Assert.Equal(.75f, Rac1RetailPanelBlend.StreakRgbScale);
        Assert.Equal(.625f, Rac1RetailPanelBlend.StreakAlphaScale);
    }
}

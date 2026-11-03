using Godot;
using OBP.Godot;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class WorldMaterialFactoryTests
{
    [Fact]
    public void NullPresentationLeavesNativeOverridesUnset()
    {
        var plan = WorldMaterialFactory.ResolveNativePresentation(null);

        Assert.Null(plan.TextureFilter);
        Assert.Null(plan.TextureRepeat);
        Assert.Null(plan.Transparency);
        Assert.Equal(BaseMaterial3D.BlendModeEnum.Mix, plan.BlendMode);
        Assert.Null(plan.FixedAlphaFactor);
        Assert.False(plan.ScaleRgbByFixedAlpha);
    }

    [Theory]
    [InlineData(RuntimeTextureMinFilter.Nearest, BaseMaterial3D.TextureFilterEnum.Nearest)]
    [InlineData(RuntimeTextureMinFilter.Linear, BaseMaterial3D.TextureFilterEnum.Linear)]
    [InlineData(RuntimeTextureMinFilter.NearestMipmapNearest, BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps)]
    [InlineData(RuntimeTextureMinFilter.NearestMipmapLinear, BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps)]
    [InlineData(RuntimeTextureMinFilter.LinearMipmapNearest, BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps)]
    [InlineData(RuntimeTextureMinFilter.LinearMipmapLinear, BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps)]
    public void MapsRecoveredMinificationFilterToGodotSampler(
        RuntimeTextureMinFilter native,
        BaseMaterial3D.TextureFilterEnum expected)
    {
        var plan = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(MinFilter: native));

        Assert.Equal(expected, plan.TextureFilter);
    }

    [Fact]
    public void MapsUniformWrapButDoesNotInventMixedAxisApproximation()
    {
        var repeat = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                WrapS: RuntimeTextureWrap.Repeat,
                WrapT: RuntimeTextureWrap.Repeat));
        var clamp = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                WrapS: RuntimeTextureWrap.Clamp,
                WrapT: RuntimeTextureWrap.Clamp));
        var mixed = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                WrapS: RuntimeTextureWrap.Clamp,
                WrapT: RuntimeTextureWrap.Repeat));

        Assert.True(repeat.TextureRepeat);
        Assert.False(clamp.TextureRepeat);
        Assert.Null(mixed.TextureRepeat);
    }

    [Fact]
    public void AdditiveFixedAlphaScalesSourceRgbBeforeAddBlend()
    {
        var plan = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                AlphaBlendEnabled: true,
                BlendEquation: RuntimeBlendEquation.AdditiveFixedAlpha,
                FixedAlphaFactor: 0.5));

        Assert.Equal(BaseMaterial3D.TransparencyEnum.Alpha, plan.Transparency);
        Assert.Equal(BaseMaterial3D.BlendModeEnum.Add, plan.BlendMode);
        Assert.Equal(0.5f, plan.FixedAlphaFactor);
        Assert.True(plan.ScaleRgbByFixedAlpha);
    }

    [Fact]
    public void FixedAlphaMixKeepsCoefficientWithoutRgbScaling()
    {
        var plan = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                AlphaBlendEnabled: true,
                BlendEquation: RuntimeBlendEquation.FixedAlpha,
                FixedAlphaFactor: 0.75));

        Assert.Equal(BaseMaterial3D.BlendModeEnum.Mix, plan.BlendMode);
        Assert.Equal(0.75f, plan.FixedAlphaFactor);
        Assert.False(plan.ScaleRgbByFixedAlpha);
    }

    [Fact]
    public void SourceAlphaUsesGodotMixBlend()
    {
        var plan = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                AlphaBlendEnabled: true,
                BlendEquation: RuntimeBlendEquation.SourceAlpha));

        Assert.Equal(BaseMaterial3D.TransparencyEnum.Alpha, plan.Transparency);
        Assert.Equal(BaseMaterial3D.BlendModeEnum.Mix, plan.BlendMode);
        Assert.Null(plan.FixedAlphaFactor);
        Assert.False(plan.ScaleRgbByFixedAlpha);
    }

    [Fact]
    public void DisabledBlendDoesNotForceHostTransparency()
    {
        var plan = WorldMaterialFactory.ResolveNativePresentation(
            new RuntimeMaterialPresentation(
                AlphaBlendEnabled: false,
                BlendEquation: RuntimeBlendEquation.AdditiveSourceAlpha));

        Assert.Null(plan.Transparency);
        Assert.Equal(BaseMaterial3D.BlendModeEnum.Add, plan.BlendMode);
    }
}

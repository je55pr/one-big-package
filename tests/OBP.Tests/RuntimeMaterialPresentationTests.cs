using OBP.PS2.Graphics;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Portable native-to-runtime material mapping tests. These deliberately avoid
/// Godot types so the presentation contract remains usable by any host.
/// </summary>
public class RuntimeMaterialPresentationTests
{
    private static RcMaterialState State(
        int textureId = 7,
        RcTextureWrapHint wrapS = RcTextureWrapHint.Unknown,
        RcTextureWrapHint wrapT = RcTextureWrapHint.Unknown,
        RcTextureMinFilterHint minFilter = RcTextureMinFilterHint.Unknown) =>
        new(textureId, wrapS, wrapT, minFilter, 0, (int)minFilter, (int)wrapS, (int)wrapT, "test");

    [Fact]
    public void MapsRecoveredSamplerAndAlphaEnableIntoNeutralContract()
    {
        var native = State(
            wrapS: RcTextureWrapHint.Clamp,
            wrapT: RcTextureWrapHint.Repeat,
            minFilter: RcTextureMinFilterHint.LinearMipmapLinear);

        var runtime = OBP.RAC2.NativeMaterialPresentation.From(
            native, alphaBlendEnabled: true);

        Assert.Equal(RuntimeTextureWrap.Clamp, runtime.WrapS);
        Assert.Equal(RuntimeTextureWrap.Repeat, runtime.WrapT);
        Assert.Equal(RuntimeTextureMinFilter.LinearMipmapLinear, runtime.MinFilter);
        Assert.True(runtime.AlphaBlendEnabled);
        Assert.Equal(RuntimeBlendEquation.Unknown, runtime.BlendEquation);
        Assert.True(runtime.HasNativeEvidence);
    }

    [Fact]
    public void MapsBlendEquationAndFixedAlphaCoefficient()
    {
        var native = State() with
        {
            AlphaBlend = new RcGsAlphaState(0, 2, 2, 1, 64),
        };

        var runtime = OBP.RAC2.NativeMaterialPresentation.From(native);

        Assert.Equal(RuntimeBlendEquation.AdditiveFixedAlpha, runtime.BlendEquation);
        Assert.Equal(0.5, runtime.FixedAlphaFactor);
    }

    [Theory]
    [InlineData(-1, RuntimeSurfaceEffect.None)]
    [InlineData(-2, RuntimeSurfaceEffect.Chrome)]
    [InlineData(-3, RuntimeSurfaceEffect.Glass)]
    [InlineData(12, RuntimeSurfaceEffect.RegularTexture)]
    public void MapsRecoveredMobyTextureSelectors(int textureId, RuntimeSurfaceEffect expected)
    {
        var runtime = OBP.RAC2.NativeMaterialPresentation.From(
            State(textureId), classifyMobySurface: true);

        Assert.Equal(expected, runtime.SurfaceEffect);
    }

    [Fact]
    public void TrilogyAdaptersMapSharedNativeEvidenceIdentically()
    {
        var native = State(
            textureId: -2,
            wrapS: RcTextureWrapHint.Repeat,
            wrapT: RcTextureWrapHint.Clamp,
            minFilter: RcTextureMinFilterHint.NearestMipmapLinear) with
        {
            AlphaBlend = new RcGsAlphaState(0, 1, 0, 1, 0),
        };

        var rac1 = OBP.RAC1.NativeMaterialPresentation.From(
            native, alphaBlendEnabled: false, classifyMobySurface: true);
        var rac2 = OBP.RAC2.NativeMaterialPresentation.From(
            native, alphaBlendEnabled: false, classifyMobySurface: true);
        var rac3 = OBP.RAC3.NativeMaterialPresentation.From(
            native, alphaBlendEnabled: false, classifyMobySurface: true);

        Assert.Equal(rac1, rac2);
        Assert.Equal(rac2, rac3);

        Assert.Equal(RuntimeBlendEquation.SourceAlpha, rac1.BlendEquation);
        Assert.Null(rac1.FixedAlphaFactor);
        Assert.Equal(RuntimeSurfaceEffect.Chrome, rac1.SurfaceEffect);
    }

    [Fact]
    public void UnknownNativeStateStaysUnresolved()
    {
        var runtime = OBP.RAC2.NativeMaterialPresentation.From(State());

        Assert.Equal(RuntimeTextureWrap.Unknown, runtime.WrapS);
        Assert.Equal(RuntimeTextureWrap.Unknown, runtime.WrapT);
        Assert.Equal(RuntimeTextureMinFilter.Unknown, runtime.MinFilter);
        Assert.Null(runtime.AlphaBlendEnabled);
        Assert.Equal(RuntimeBlendEquation.Unknown, runtime.BlendEquation);
        Assert.Null(runtime.FixedAlphaFactor);
        Assert.Equal(RuntimeSurfaceEffect.Unknown, runtime.SurfaceEffect);
        Assert.False(runtime.HasNativeEvidence);
    }
}

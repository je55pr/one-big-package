using OBP.PS2.Graphics;
using OBP.Runtime.Presentation;

namespace OBP.RAC2;

/// <summary>
/// Source-game boundary adapter from recovered PS2 material evidence to the
/// engine-independent runtime presentation contract.
/// </summary>
public static class NativeMaterialPresentation
{
    public static RuntimeMaterialPresentation From(
        RcMaterialState state,
        bool? alphaBlendEnabled = null,
        bool classifyMobySurface = false) => new(
        MapWrap(state.WrapS),
        MapWrap(state.WrapT),
        MapFilter(state.MinFilter),
        alphaBlendEnabled,
        MapBlend(state.AlphaBlend?.Hint ?? RcBlendModeHint.Unknown),
        FixedAlphaFactor(state.AlphaBlend),
        classifyMobySurface
            ? MapSurface(RcMobyMaterial.ClassifyTextureId(state.TextureId))
            : RuntimeSurfaceEffect.Unknown);

    private static RuntimeTextureWrap MapWrap(RcTextureWrapHint value) => value switch
    {
        RcTextureWrapHint.Repeat => RuntimeTextureWrap.Repeat,
        RcTextureWrapHint.Clamp => RuntimeTextureWrap.Clamp,
        _ => RuntimeTextureWrap.Unknown,
    };

    private static RuntimeTextureMinFilter MapFilter(RcTextureMinFilterHint value) => value switch
    {
        RcTextureMinFilterHint.Nearest => RuntimeTextureMinFilter.Nearest,
        RcTextureMinFilterHint.Linear => RuntimeTextureMinFilter.Linear,
        RcTextureMinFilterHint.NearestMipmapNearest => RuntimeTextureMinFilter.NearestMipmapNearest,
        RcTextureMinFilterHint.NearestMipmapLinear => RuntimeTextureMinFilter.NearestMipmapLinear,
        RcTextureMinFilterHint.LinearMipmapNearest => RuntimeTextureMinFilter.LinearMipmapNearest,
        RcTextureMinFilterHint.LinearMipmapLinear => RuntimeTextureMinFilter.LinearMipmapLinear,
        _ => RuntimeTextureMinFilter.Unknown,
    };

    private static RuntimeBlendEquation MapBlend(RcBlendModeHint value) => value switch
    {
        RcBlendModeHint.SourceAlpha => RuntimeBlendEquation.SourceAlpha,
        RcBlendModeHint.AdditiveSourceAlpha => RuntimeBlendEquation.AdditiveSourceAlpha,
        RcBlendModeHint.FixedAlpha => RuntimeBlendEquation.FixedAlpha,
        RcBlendModeHint.AdditiveFixedAlpha => RuntimeBlendEquation.AdditiveFixedAlpha,
        _ => RuntimeBlendEquation.Unknown,
    };

    private static double? FixedAlphaFactor(RcGsAlphaState? alpha) => alpha?.Hint is
        RcBlendModeHint.FixedAlpha or RcBlendModeHint.AdditiveFixedAlpha
            ? alpha.Value.Fix / 128.0
            : null;

    private static RuntimeSurfaceEffect MapSurface(RcMobySurfaceEffect value) => value switch
    {
        RcMobySurfaceEffect.RegularTexture => RuntimeSurfaceEffect.RegularTexture,
        RcMobySurfaceEffect.None => RuntimeSurfaceEffect.None,
        RcMobySurfaceEffect.Chrome => RuntimeSurfaceEffect.Chrome,
        RcMobySurfaceEffect.Glass => RuntimeSurfaceEffect.Glass,
        _ => RuntimeSurfaceEffect.Unknown,
    };
}

namespace OBP.Runtime.Presentation;

public enum RuntimeTextureWrap
{
    Unknown,
    Repeat,
    Clamp,
}

public enum RuntimeTextureMinFilter
{
    Unknown,
    Nearest,
    Linear,
    NearestMipmapNearest,
    NearestMipmapLinear,
    LinearMipmapNearest,
    LinearMipmapLinear,
}

public enum RuntimeBlendEquation
{
    Unknown,
    SourceAlpha,
    AdditiveSourceAlpha,
    FixedAlpha,
    AdditiveFixedAlpha,
}

public enum RuntimeSurfaceEffect
{
    Unknown,
    RegularTexture,
    None,
    Chrome,
    Glass,
}

/// <summary>
/// Engine-independent presentation evidence attached to one render surface.
/// Unknown values are intentional: importers only populate semantics proven by
/// the native format, and hosts may keep their established fallback behaviour
/// for unresolved fields.
/// </summary>
public sealed record RuntimeMaterialPresentation(
    RuntimeTextureWrap WrapS = RuntimeTextureWrap.Unknown,
    RuntimeTextureWrap WrapT = RuntimeTextureWrap.Unknown,
    RuntimeTextureMinFilter MinFilter = RuntimeTextureMinFilter.Unknown,
    bool? AlphaBlendEnabled = null,
    RuntimeBlendEquation BlendEquation = RuntimeBlendEquation.Unknown,
    double? FixedAlphaFactor = null,
    RuntimeSurfaceEffect SurfaceEffect = RuntimeSurfaceEffect.Unknown)
{
    public bool HasNativeEvidence =>
        WrapS != RuntimeTextureWrap.Unknown
        || WrapT != RuntimeTextureWrap.Unknown
        || MinFilter != RuntimeTextureMinFilter.Unknown
        || AlphaBlendEnabled is not null
        || BlendEquation != RuntimeBlendEquation.Unknown
        || FixedAlphaFactor is not null
        || SurfaceEffect != RuntimeSurfaceEffect.Unknown;
}

using System.Buffers.Binary;

namespace OBP.PS2.Graphics;

/// <summary>
/// Render-state hints carried by the R&C PS2 engine's VU material payloads.
/// These records are inputs to game microcode, not a generic GS register dump.
/// </summary>
public enum RcTextureWrapHint
{
    Unknown = -1,
    Repeat = 0,
    Clamp = 1,
}

public enum RcTextureMinFilterHint
{
    Unknown = -1,
    Nearest = 0,
    Linear = 1,
    NearestMipmapNearest = 2,
    NearestMipmapLinear = 3,
    LinearMipmapNearest = 4,
    LinearMipmapLinear = 5,
}
public enum RcBlendModeHint
{
    Unknown,
    SourceAlpha,
    AdditiveSourceAlpha,
    FixedAlpha,
    AdditiveFixedAlpha,
}

public readonly record struct RcGsAlphaState(int A, int B, int C, int D, byte Fix)
{
    public static RcGsAlphaState Decode(ulong raw) => new(
        (int)(raw & 0x3),
        (int)((raw >> 2) & 0x3),
        (int)((raw >> 4) & 0x3),
        (int)((raw >> 6) & 0x3),
        (byte)((raw >> 32) & 0xff));

    public RcBlendModeHint Hint => (A, B, C, D) switch
    {
        (0, 1, 0, 1) => RcBlendModeHint.SourceAlpha,
        (0, 2, 0, 1) => RcBlendModeHint.AdditiveSourceAlpha,
        (0, 1, 2, 1) => RcBlendModeHint.FixedAlpha,
        (0, 2, 2, 1) => RcBlendModeHint.AdditiveFixedAlpha,
        _ => RcBlendModeHint.Unknown,
    };
}

public sealed record RcMaterialState(
    int TextureId,
    RcTextureWrapHint WrapS,
    RcTextureWrapHint WrapT,
    RcTextureMinFilterHint MinFilter,
    int LodKRaw,
    int MinFilterRaw,
    int ClampRawLo,
    int ClampRawHi,
    string Layout)
{
    public bool HasKnownWrap => WrapS != RcTextureWrapHint.Unknown && WrapT != RcTextureWrapHint.Unknown;

    public byte Tex0Address { get; init; }
    public byte Tex1Address { get; init; }
    public byte ClampAddress { get; init; }
    public byte? Extra0Address { get; init; }
    public byte? Extra1Address { get; init; }
    public RcGsAlphaState? AlphaBlend { get; init; }

    public static RcMaterialState ReadTfrag(ReadOnlySpan<byte> primitive)
    {
        Require(primitive, 0x50, "tfrag");
        return Decode("tfrag", primitive, 0x00, 0x10, 0x20, decodeWrap: false, decodeFilter: false) with
        {
            Tex0Address = Address(primitive, 0x00),
            Tex1Address = Address(primitive, 0x10),
            ClampAddress = Address(primitive, 0x20),
            Extra0Address = Address(primitive, 0x30),
            Extra1Address = Address(primitive, 0x40),
        };
    }

    public static RcMaterialState ReadTie(ReadOnlySpan<byte> primitive)
    {
        Require(primitive, 0x50, "tie");
        byte tailAddress = Address(primitive, 0x40);
        return Decode("tie", primitive, 0x00, 0x10, 0x30, decodeWrap: false, decodeFilter: false) with
        {
            Tex0Address = Address(primitive, 0x00),
            Tex1Address = Address(primitive, 0x10),
            ClampAddress = Address(primitive, 0x30),
            Extra0Address = Address(primitive, 0x20),
            Extra1Address = tailAddress,
            AlphaBlend = tailAddress == 0x42
                ? RcGsAlphaState.Decode(BinaryPrimitives.ReadUInt64LittleEndian(primitive[0x40..]))
                : null,
        };
    }

    public static RcMaterialState ReadShrub(ReadOnlySpan<byte> primitive)
    {
        Require(primitive, 0x40, "shrub");
        return Decode("shrub", primitive, 0x30, 0x00, 0x10, decodeWrap: true, decodeFilter: true) with
        {
            Tex0Address = Address(primitive, 0x30),
            Tex1Address = Address(primitive, 0x00),
            ClampAddress = Address(primitive, 0x10),
            Extra0Address = Address(primitive, 0x20),
        };
    }

    public static RcMaterialState ReadMoby(ReadOnlySpan<byte> primitive)
    {
        Require(primitive, 0x40, "moby");
        return Decode("moby", primitive, 0x20, 0x00, 0x10, decodeWrap: false, decodeFilter: true) with
        {
            Tex0Address = Address(primitive, 0x20),
            Tex1Address = Address(primitive, 0x00),
            ClampAddress = Address(primitive, 0x10),
            Extra0Address = Address(primitive, 0x30),
        };
    }

    private static RcMaterialState Decode(
        string layout,
        ReadOnlySpan<byte> primitive,
        int tex0Offset,
        int tex1Offset,
        int clampOffset,
        bool decodeWrap,
        bool decodeFilter)
    {
        int textureId = BinaryPrimitives.ReadInt32LittleEndian(primitive[tex0Offset..]);
        int lodK = BinaryPrimitives.ReadInt32LittleEndian(primitive[tex1Offset..]);
        int minFilter = BinaryPrimitives.ReadInt32LittleEndian(primitive[(tex1Offset + 4)..]);
        int clampLo = BinaryPrimitives.ReadInt32LittleEndian(primitive[clampOffset..]);
        int clampHi = BinaryPrimitives.ReadInt32LittleEndian(primitive[(clampOffset + 4)..]);
        return new RcMaterialState(
            textureId,
            decodeWrap ? DecodeWrap(clampLo) : RcTextureWrapHint.Unknown,
            decodeWrap ? DecodeWrap(clampHi) : RcTextureWrapHint.Unknown,
            decodeFilter ? DecodeMinFilter(minFilter) : RcTextureMinFilterHint.Unknown,
            lodK,
            minFilter,
            clampLo,
            clampHi,
            layout);
    }

    private static RcTextureWrapHint DecodeWrap(int raw) => raw switch
    {
        0 => RcTextureWrapHint.Repeat,
        1 => RcTextureWrapHint.Clamp,
        _ => RcTextureWrapHint.Unknown,
    };

    private static RcTextureMinFilterHint DecodeMinFilter(int raw) => raw switch
    {
        0 => RcTextureMinFilterHint.Nearest,
        1 => RcTextureMinFilterHint.Linear,
        2 => RcTextureMinFilterHint.NearestMipmapNearest,
        3 => RcTextureMinFilterHint.NearestMipmapLinear,
        4 => RcTextureMinFilterHint.LinearMipmapNearest,
        5 => RcTextureMinFilterHint.LinearMipmapLinear,
        _ => RcTextureMinFilterHint.Unknown,
    };

    private static void Require(ReadOnlySpan<byte> primitive, int length, string layout)
    {
        if (primitive.Length < length)
            throw new InvalidDataException($"{layout} material primitive is shorter than 0x{length:x} bytes.");
    }

    private static byte Address(ReadOnlySpan<byte> primitive, int offset) => primitive[offset + 8];
}

/// <summary>Special negative texture selectors used by the shared Moby packet grammar.</summary>
public enum RcMobySurfaceEffect
{
    Unknown,
    RegularTexture,
    None,
    Chrome,
    Glass,
}

public static class RcMobyMaterial
{
    public static RcMobySurfaceEffect ClassifyTextureId(int textureId) => textureId switch
    {
        >= 0 => RcMobySurfaceEffect.RegularTexture,
        -1 => RcMobySurfaceEffect.None,
        -2 => RcMobySurfaceEffect.Chrome,
        -3 => RcMobySurfaceEffect.Glass,
        _ => RcMobySurfaceEffect.Unknown,
    };
}

/// <summary>
/// Primitive flags encoded in a GIF tag when PRE=1. Only fields needed for
/// material recovery are exposed; this is deliberately not a GS emulator.
/// </summary>
public readonly record struct RcGifPrimitiveState(
    bool Pre,
    int PrimitiveType,
    bool Gouraud,
    bool Textured,
    bool Fogged,
    bool AlphaBlendEnabled)
{
    public static RcGifPrimitiveState Decode(ulong gifTagLow)
    {
        bool pre = ((gifTagLow >> 46) & 1) != 0;
        int prim = (int)((gifTagLow >> 47) & 0x7ff);
        return new RcGifPrimitiveState(
            pre,
            prim & 0x7,
            ((prim >> 3) & 1) != 0,
            ((prim >> 4) & 1) != 0,
            ((prim >> 5) & 1) != 0,
            ((prim >> 6) & 1) != 0);
    }
}

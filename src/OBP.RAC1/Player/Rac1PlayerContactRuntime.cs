using OBP.RAC1.Gameplay;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

public readonly record struct Rac1NativeVector3(double X, double Y, double Z)
{
    public static Rac1NativeVector3 Zero { get; } = new(0d, 0d, 0d);

    public static Rac1NativeVector3 operator +(
        Rac1NativeVector3 left,
        Rac1NativeVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public double Length =>
        Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
}

/// <summary>
/// Decoded R&C1 collision-face byte. The raw byte is always retained.
/// Signed/high-bit faces and low class 31 are deliberately excluded from
/// ordinary material classification rather than assigned lethal semantics.
/// </summary>
public readonly record struct Rac1CollisionFaceSemantics(
    byte RawFaceType,
    int? SurfaceClass,
    int RawEffectMode,
    int EffectMode,
    bool IsSpecialHighBitFace)
{
    public static Rac1CollisionFaceSemantics Decode(int rawFaceType)
    {
        if (rawFaceType is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(rawFaceType));

        byte raw = checked((byte)rawFaceType);
        bool special = (raw & 0x80) != 0;
        int lowClass = raw & 0x1f;
        int rawEffect = (raw & 0x60) >> 5;
        int? surfaceClass = !special && lowClass != 31
            ? lowClass
            : null;
        int effectMode = special || rawEffect == 3
            ? 0
            : rawEffect;

        return new Rac1CollisionFaceSemantics(
            raw,
            surfaceClass,
            rawEffect,
            effectMode,
            special);
    }
}

public enum Rac1SurfaceInteractionKind
{
    None,
    ShallowWaterWade,
    MudSink,
    IceSlide,
    MagnebootSupport,
}
public readonly record struct Rac1SupportAnchorState(
    uint NativeState,
    bool IsValid);

public readonly record struct Rac1SupportCarry(
    Rac1NativeVector3 TransformDelta)
{
    public static Rac1SupportCarry None { get; } =
        new(Rac1NativeVector3.Zero);
}

/// <summary>
/// Conveyor transfer is distinct from ordinary support carry. The pre-transform
/// bias is retained separately from the resolved tangential transfer.
/// </summary>
public readonly record struct Rac1ConveyorTransfer(
    Rac1NativeVector3 PreTransformBias,
    Rac1NativeVector3 TangentialTransfer)
{
    public const int RecoveredClass1250 = 1250;
    public const double Class1250NativeBiasPerTick = 1d / 24d;

    public static Rac1ConveyorTransfer None { get; } =
        new(Rac1NativeVector3.Zero, Rac1NativeVector3.Zero);

    public static Rac1ConveyorTransfer ForClass1250(
        Rac1NativeVector3 nativePreTransformDirection,
        Rac1NativeVector3 resolvedTangentialTransfer)
    {
        double length = nativePreTransformDirection.Length;
        if (!double.IsFinite(length) || length <= 1e-12d)
            throw new ArgumentOutOfRangeException(nameof(nativePreTransformDirection));

        double scale = Class1250NativeBiasPerTick / length;
        var bias = new Rac1NativeVector3(
            nativePreTransformDirection.X * scale,
            nativePreTransformDirection.Y * scale,
            nativePreTransformDirection.Z * scale);
        return new Rac1ConveyorTransfer(
            bias,
            resolvedTangentialTransfer);
    }
}

/// <summary>
/// Rich R&C1 contact/support result corresponding to the recovered player
/// contact fields. Dynamic contact and persistent support are intentionally
/// separate identities; support carry and conveyor transfer stay separate too.
/// </summary>
public sealed record Rac1PlayerContactResult(
    bool IsGrounded,
    bool HitCeiling,
    Rac1CollisionFaceSemantics? Face,
    Rac1MobyRuntimeKey? ContactedMoby,
    Rac1MobyRuntimeKey? CurrentDynamicContact,
    Rac1MobyRuntimeKey? PersistentSupportMoby,
    Rac1SupportAnchorState SupportAnchor,
    Rac1SupportCarry SupportCarry,
    Rac1ConveyorTransfer Conveyor)
{
    public const int MagnebootSupportMobyClass = 0xad;

    public PlayerContactFacts MovementFacts =>
        new(IsGrounded, HitCeiling);

    public Rac1SurfaceInteractionKind SurfaceInteraction =>
        ClassifySurface(Face, ContactedMoby);
    public Rac1NativeVector3 ApplySupportAndConveyor(
        Rac1NativeVector3 ordinaryPlayerDelta)
    {
        Rac1NativeVector3 supportDelta =
            SupportAnchor.IsValid && PersistentSupportMoby is not null
                ? SupportCarry.TransformDelta
                : Rac1NativeVector3.Zero;

        return ordinaryPlayerDelta +
            supportDelta +
            Conveyor.TangentialTransfer;
    }

    public static Rac1PlayerContactResult StaticWorld(
        bool isGrounded,
        bool hitCeiling = false,
        int? rawFaceType = null) =>
        new(
            isGrounded,
            hitCeiling,
            rawFaceType.HasValue
                ? Rac1CollisionFaceSemantics.Decode(rawFaceType.Value)
                : null,
            null,
            null,
            null,
            new Rac1SupportAnchorState(0u, false),
            Rac1SupportCarry.None,
            Rac1ConveyorTransfer.None);

    private static Rac1SurfaceInteractionKind ClassifySurface(
        Rac1CollisionFaceSemantics? face,
        Rac1MobyRuntimeKey? contactedMoby)
    {
        if (face?.SurfaceClass is not int surface)
            return Rac1SurfaceInteractionKind.None;
        return surface switch
        {
            0 => Rac1SurfaceInteractionKind.ShallowWaterWade,
            3 => Rac1SurfaceInteractionKind.MudSink,
            7 => Rac1SurfaceInteractionKind.IceSlide,
            2 when contactedMoby?.NativeClassId == MagnebootSupportMobyClass =>
                Rac1SurfaceInteractionKind.MagnebootSupport,
            _ => Rac1SurfaceInteractionKind.None,
        };
    }
}

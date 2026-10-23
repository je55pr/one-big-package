namespace OBP.RAC1.Gameplay;

/// <summary>
/// One narrow native Rilgar class-623 prefilter at 0x2846d8.
/// The caller at 0x3033c4 passes a Moby pointer, mask 0x00330000,
/// and a zero clear-policy argument. The routine checks the Moby's
/// +0xa4 marker against a 0x40-stride native registry at 0x178500:
/// +0x34 stores the owning Moby pointer and +0x24 the marker flags.
/// This only models whether the existing marker byte is cleared.
/// It is not a damage receiver, health calculation or hit admission.
/// </summary>
public static class Rac1Class623ImpactMarkerFilter
{
    public const byte EmptyMarker = 0xff;
    public const uint NativeCallMask = 0x00330000;
    public const int RegistryStride = 0x40;
    public const int NativeMarkerOffset = 0xa4;
    public const int RegistryFlagsOffset = 0x24;
    public const int RegistryOwnerOffset = 0x34;

    /// <summary>
    /// Owner equality is native pointer equality, not merely a class match.
    /// Unknown registry facts fail closed and preserve the existing byte.
    /// The original helper's third argument is zero in the class-623 call.
    /// </summary>
    public static Rac1Class623ImpactMarkerResult Evaluate(
        byte currentMarker,
        bool? ownerIsTarget,
        uint? registryFlags,
        bool allowClearing = true)
    {
        if (currentMarker == EmptyMarker ||
            ownerIsTarget != true ||
            registryFlags is null ||
            (registryFlags.Value & NativeCallMask) != 0 ||
            !allowClearing)
            return new(currentMarker, false);

        return new(EmptyMarker, true);
    }
}

public readonly record struct Rac1Class623ImpactMarkerResult(
    byte Marker, bool WasCleared);

using OBP.Runtime;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Retail-backed tag-choice fragment for TABLE1's per-frame target registry.
/// This does not claim complete registration admission. Each owning class has
/// additional live-state gates before it calls shared helper 0x004538E0.
/// </summary>
public static class UyaTargetRegistry
{
    public const int NativeTagThree = 3;
    public const int NativeTagFive = 5;

    public static bool TryGetAuthoredSelectorOffset(
        int nativeClassId,
        out int pvarOffset)
    {
        pvarOffset = nativeClassId switch
        {
            5821 => 0x5f,
            5860 => 0x5f,
            6306 => 0x5f,
            6317 => 0x5f,
            6476 => 0x2f,
            6577 => 0x5f,
            6836 => 0x5f,
            _ => -1,
        };
        return pvarOffset >= 0;
    }

    public static UyaTargetRegistryTagChoice DecodeAuthoredTagChoice(
        int nativeClassId,
        ReadOnlySpan<byte> pvar)
    {
        if (!TryGetAuthoredSelectorOffset(nativeClassId, out int offset))
            throw new NotSupportedException(
                $"UYA class {nativeClassId} has no recovered TABLE1 registry tag selector.");

        if ((uint)offset >= (uint)pvar.Length)
            throw new InvalidDataException(
                $"UYA class {nativeClassId} PVar is too short for registry selector +0x{offset:x}.");

        byte selector = pvar[offset];
        return new UyaTargetRegistryTagChoice(
            nativeClassId,
            offset,
            selector,
            selector == 0 ? NativeTagThree : NativeTagFive,
            RequiresAdditionalNativeAdmission: true);
    }

    public static UyaTargetRegistryTagChoice ReadAuthoredTagChoice(
        RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac3")
            throw new ArgumentException("Source is not a UYA Moby.", nameof(source));

        byte[] pvar = source.NativePayloads?
            .SingleOrDefault(payload =>
                payload.Format == UyaMobyRuntimeSession.PVarPayloadFormat)?.Data
            ?? throw new InvalidDataException(
                $"UYA class {source.NativeClassId} instance {source.InstanceIndex} has no PVar payload.");

        return DecodeAuthoredTagChoice(source.NativeClassId, pvar);
    }
}

public sealed record UyaTargetRegistryTagChoice(
    int NativeClassId,
    int PVarSelectorOffset,
    byte AuthoredSelector,
    int NativeTag,
    bool RequiresAdditionalNativeAdmission);

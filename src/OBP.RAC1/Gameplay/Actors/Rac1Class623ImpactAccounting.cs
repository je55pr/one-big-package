using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Rilgar class-623's narrow native health accounting at 0x3034f4–0x303530.
/// This is a consumer of original 0x284730 service outputs, not a source
/// collision solver or a replacement for the state-specific hit receiver.
/// </summary>
public static class Rac1Class623ImpactAccounting
{
    public const int HealthPVarOffset = 0x20;
    public const int SkipStatus = 1;
    public const int TerminalState = 14;

    /// <summary>The authored +0x20 floating-point value; no default HP invented.</summary>
    public static float ReadAuthoredHealth(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length != Rac1AmoeboidClass623Admission.PVarSize)
            throw new InvalidDataException("Class-623 health needs the original 0x2e0-byte PVar.");
        return BinaryPrimitives.ReadSingleLittleEndian(pvar[HealthPVarOffset..]);
    }

    /// <summary>
    /// Models only the caller's native subtraction after a service query:
    /// skip when outStatus==1 or the Moby is already state 14; otherwise
    /// PVar+0x20 -= native outScalar and nonpositive result takes the
    /// terminal response branch. Null means the service fact is unknown.
    /// Finite checks are host safety for absent/corrupt observations, not
    /// a recovered PS2 FPU predicate. No state or flag is changed here.
    /// </summary>
    public static Rac1Class623ImpactAccountingResult Evaluate(
        float currentHealth,
        int nativeState,
        int? nativeServiceStatus,
        float? nativeServiceScalar)
    {
        if (nativeState == TerminalState ||
            nativeServiceStatus is null ||
            nativeServiceStatus == SkipStatus ||
            nativeServiceScalar is null ||
            !float.IsFinite(currentHealth) ||
            !float.IsFinite(nativeServiceScalar.Value))
            return new(false, currentHealth, false);

        float remaining = currentHealth - nativeServiceScalar.Value;
        if (!float.IsFinite(remaining))
            return new(false, currentHealth, false);

        return new(true, remaining, remaining <= 0f);
    }
}

public readonly record struct Rac1Class623ImpactAccountingResult(
    bool Applied, float RemainingHealth, bool TakesTerminalPath);

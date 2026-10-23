using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Rilgar class-623's source state-0 target eligibility, distinct from its
/// unrecovered subsequent updater and damage receiver. Native L05 0x302080
/// first sets Moby.flags+0x34 bit 0x1000; the +0x2b4 and +0x2b8 branches
/// clear that bit when entering states 12 and 15 respectively.
/// </summary>
public static class Rac1AmoeboidClass623Admission
{
    public const int PVarSize = 0x2e0;
    public const int EligibleState = 1;
    public const int HiddenState = 12;
    public const int WaitingState = 15;
    public const ushort TargetableFlag = 0x1000;

    /// <summary>
    /// Native initial Moby state and 0x1000 flag after state 0. This is
    /// an authored startup snapshot, NOT later runtime target liveness.
    /// </summary>
    public static Rac1Class623InitialTargetState Read(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length != PVarSize)
            throw new InvalidDataException(
                $"Rilgar class-623 target requires 0x{PVarSize:x} source PVar bytes.");

        int hiddenGate = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x2b4..]);
        long linkedGate = BinaryPrimitives.ReadInt64LittleEndian(pvar[0x2b8..]);
        if (hiddenGate != 0)
            return new(HiddenState, false);
        if (linkedGate != -1)
            return new(WaitingState, false);
        return new(EligibleState, true);
    }
}

public readonly record struct Rac1Class623InitialTargetState(
    int NativeState, bool HasTargetableFlag);

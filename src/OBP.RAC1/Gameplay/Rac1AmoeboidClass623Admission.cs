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
        ValidatePVar(pvar);
        int hiddenGate = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x2b4..]);
        long linkedGate = BinaryPrimitives.ReadInt64LittleEndian(pvar[0x2b8..]);
        if (hiddenGate != 0)
            return new(HiddenState, false);
        if (linkedGate != -1)
            return new(WaitingState, false);
        return new(EligibleState, true);
    }

    /// <summary>
    /// State-15 reads two 32-bit indices from PVar+0x2b8/+0x2bc.
    /// The first indexes a runtime Moby (class 830 in all retail Rilgar
    /// cases), the second is an optional authored clip-volume index.
    /// Both use -1 as the absent sentinel.
    /// </summary>
    public static Rac1Class623ActivationLinks ReadActivationLinks(ReadOnlySpan<byte> pvar)
    {
        ValidatePVar(pvar);
        int linkedMoby = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x2b8..]);
        int triggerVolume = BinaryPrimitives.ReadInt32LittleEndian(pvar[0x2bc..]);
        if (linkedMoby < -1 || triggerVolume < -1)
            throw new InvalidDataException("Class-623 activation references contain invalid indices.");
        return new(linkedMoby, triggerVolume);
    }

    /// <summary>
    /// Recovered state-15 reactivation conditions at Rilgar 0x30282c.
    /// The supplied linked controller byte is the *native runtime*
    /// Moby+0xbc, NOT the actor's visibility/active presentation.
    /// Unknown host facts must be null and do not grant admission.
    /// The clip-volume predicate is a separate explicit host adapter.
    /// </summary>
    public static Rac1Class623InitialTargetState AdvanceWaiting(
        Rac1Class623InitialTargetState current, Rac1Class623ActivationLinks links,
        byte? linkedControllerCompletion, bool? playerInsideTriggerVolume)
    {
        if (current.NativeState != WaitingState || current.HasTargetableFlag)
            return current;

        bool controllerReady = links.LinkedMobyIndex >= 0 &&
            linkedControllerCompletion is > 0;
        bool inVolume = links.TriggerVolumeIndex >= 0 &&
            playerInsideTriggerVolume == true;

        return controllerReady || inVolume ? new(EligibleState, true) : current;
    }

    private static void ValidatePVar(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length != PVarSize)
            throw new InvalidDataException(
                $"Rilgar class-623 target requires 0x{PVarSize:x} source PVar bytes.");
    }
}

public readonly record struct Rac1Class623InitialTargetState(
    int NativeState, bool HasTargetableFlag);

public readonly record struct Rac1Class623ActivationLinks(
    int LinkedMobyIndex, int TriggerVolumeIndex);

using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original Rilgar class-830 PVar bytes. +0x08 is checked for zero
/// in state 0; the meanings of +0x00 and +0x04 are not yet fully
/// decoded. +0x0c is overwritten by the native updater on entry.
/// </summary>
public readonly record struct Rac1Class830NativePVar(
    int Field0, float Field4, int StateZeroLookupSelector, int FieldC)
{
    public const int Size = 0x10;

    public static Rac1Class830NativePVar Read(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length != Size)
            throw new InvalidDataException("Rilgar class-830 requires exactly 0x10 PVar bytes.");
        return new(
            BinaryPrimitives.ReadInt32LittleEndian(pvar),
            BinaryPrimitives.ReadSingleLittleEndian(pvar[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(pvar[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(pvar[12..]));
    }
}

/// <summary>
/// Explicitly supplied results of original source-owned runtime lookups.
/// No boolean here may be inferred from host visibility or generic activity.
/// Unknown lookup results (null) fail closed.
/// </summary>
/// <param name="CellMarked">Original state-0 cell byte at table 0x1cb6b0 + 0x454.</param>
/// <param name="CellBitSet">Original state-0 mask query at table 0x15c190.</param>
/// <param name="AlternateLookupByte">Original mode-nonzero table byte at 0x15c050.</param>
/// <param name="OwnsControlSlot">State-1 control owner equals this native Moby.</param>
/// <param name="ControlSlotClear">State-1 control field at +0x30e equals zero.</param>
public readonly record struct Rac1Class830NativeInputs(
    bool? CellMarked = null,
    bool? CellBitSet = null,
    byte? AlternateLookupByte = null,
    bool? OwnsControlSlot = null,
    bool? ControlSlotClear = null);

public readonly record struct Rac1Class830ControllerState(
    int NativeState, byte? CompletionByte);

/// <summary>
/// Engine-neutral, per-level class-830 completion producer. Rilgar updater
/// 0x30c5b0: state 0 checks two independent native bit tables and an
/// optional third lookup before setting state 2/+0xbc=2, or state 1.
/// State 1 checks the original control owner and the clear control slot
/// before setting state 2/+0xbc=1. Unobserved conditions stay unknown.
/// State 2 has no further native transition in this updater.
/// </summary>
public sealed class Rac1Class830ControllerSession
{
    private sealed class Controller(Rac1Class830NativePVar pvar)
    {
        public Rac1Class830NativePVar PVar { get; } = pvar;
        public int State { get; set; }
        public byte? CompletionByte { get; set; }
    }

    private readonly Dictionary<int, Controller> _controllers = [];
    public int RegisteredCount => _controllers.Count;

    public void Register(int instanceIndex, int nativeClassId, ReadOnlySpan<byte> pvar)
    {
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (nativeClassId != 830) throw new ArgumentOutOfRangeException(nameof(nativeClassId));
        if (!_controllers.TryAdd(instanceIndex, new(Rac1Class830NativePVar.Read(pvar))))
            throw new InvalidOperationException($"Duplicate class-830 controller i{instanceIndex}.");
    }

    public Rac1Class830ControllerState Get(int instanceIndex)
    {
        var controller = _controllers[instanceIndex];
        return new(controller.State, controller.CompletionByte);
    }

    /// <summary>
    /// Returns null until a real native completion write occurred.
    /// The identity and class are never derived from a presented node.
    /// </summary>
    public Rac1Class830CompletionSnapshot? Snapshot(int instanceIndex)
    {
        var controller = _controllers[instanceIndex];
        return controller.State == 2 && controller.CompletionByte is > 0
            ? new(instanceIndex, 830, controller.CompletionByte.Value)
            : null;
    }

    public Rac1Class830ControllerState Step(int instanceIndex, Rac1Class830NativeInputs source)
    {
        var controller = _controllers[instanceIndex];
        if (controller.State == 0)
        {
            if (source.CellMarked is null) return Get(instanceIndex);
            if (source.CellMarked == true)
            {
                Complete(controller, 2);
                return Get(instanceIndex);
            }
            if (source.CellBitSet is null) return Get(instanceIndex);
            if (source.CellBitSet == true)
            {
                Complete(controller, 2);
                return Get(instanceIndex);
            }
            if (controller.PVar.StateZeroLookupSelector != 0)
            {
                if (source.AlternateLookupByte is null) return Get(instanceIndex);
                if (source.AlternateLookupByte == 0xff)
                {
                    Complete(controller, 2);
                    return Get(instanceIndex);
                }
            }
            controller.State = 1;
        }
        else if (controller.State == 1 &&
                 source.OwnsControlSlot == true &&
                 source.ControlSlotClear == true)
        {
            Complete(controller, 1);
        }

        return Get(instanceIndex);
    }

    private static void Complete(Controller controller, byte signal)
    {
        controller.State = 2;
        controller.CompletionByte = signal;
    }

    public void Clear() => _controllers.Clear();
}

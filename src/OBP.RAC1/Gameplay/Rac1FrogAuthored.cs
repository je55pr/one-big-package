using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>Original class-827 selectors. All four mode slots are signed 16-bit
/// fields, not two merged integers. LEVEL6/10, FUN_L06_002e8678.</summary>
public sealed record Rac1FrogAuthored(
    int PathIndex, short HasCarrier, short StartsHidden, int TargetInstance,
    short RespawnLives, short SwitchSelector, float Health, float NativeSpeed)
{
    public const int ClassId = 827;
    public const int PVarSize = 0x290;

    public static Rac1FrogAuthored Read(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != ClassId)
            throw new ArgumentException("Expected native class-827 Frog.", nameof(source));
        var payloads = source.NativePayloads?.Where(p => p.Format == Rac1BoltCrate.PVarPayloadFormat).ToArray();
        if (payloads is not [{ Data.Length: PVarSize }])
            throw new InvalidDataException("Frog requires one 0x290-byte authored PVar.");
        var p = payloads[0].Data.AsSpan();
        var result = new Rac1FrogAuthored(
            BinaryPrimitives.ReadInt32LittleEndian(p[0x1f8..]),
            BinaryPrimitives.ReadInt16LittleEndian(p[0x200..]),
            BinaryPrimitives.ReadInt16LittleEndian(p[0x202..]),
            BinaryPrimitives.ReadInt32LittleEndian(p[0x204..]),
            BinaryPrimitives.ReadInt16LittleEndian(p[0x240..]),
            BinaryPrimitives.ReadInt16LittleEndian(p[0x242..]),
            BinaryPrimitives.ReadSingleLittleEndian(p[0x20..]),
            BinaryPrimitives.ReadSingleLittleEndian(p[0x1e4..]));
        if (result.PathIndex < -1 || result.TargetInstance < -1 ||
            result.RespawnLives < 0 || !float.IsFinite(result.Health) || result.Health <= 0 ||
            !float.IsFinite(result.NativeSpeed) || result.NativeSpeed <= 0)
            throw new InvalidDataException("Frog PVar has invalid native path, target, health or speed.");
        return result;
    }

    /// <summary>Native init branch order: carried 1, hidden 15, path 3, roam 8.
    /// +0x242 is an independent *update* gate, not an initialization mode.</summary>
    public int InitialState => HasCarrier != 0 ? 1 : StartsHidden != 0 ? 15 : PathIndex >= 0 ? 3 : 8;

    /// <summary>When the original global +0x15fdf8 switch is unknown the host
    /// deliberately uses its observed initial zero policy; selector-one frogs
    /// must not be awoken by generic nearby-enemy registration.</summary>
    public bool UpdateEligible(bool switchEnabled) => (SwitchSelector != 0) == switchEnabled;

    public bool OrdinaryHostAdmission(bool switchEnabled = false) =>
        HasCarrier == 0 && StartsHidden == 0 && UpdateEligible(switchEnabled);
}

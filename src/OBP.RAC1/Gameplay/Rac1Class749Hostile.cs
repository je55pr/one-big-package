using System.Buffers.Binary;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded retail-backed native facts for the representative Veldin class-749 hostile.
/// Targeting, state, health, sequence and attack semantics intentionally remain R&C1-specific.
/// </summary>
public static class Rac1Class749Hostile
{
    public const int NativeClassId = 749;
    public const string PVarPayloadFormat = "rac1-pvar";
    public const int PVarSize = 0x280;
    public const int HealthOffset = 0x20;

    public const int TargetSearchNativeState = 5;
    public const int TargetedNativeState = 6;
    public const int AttackNativeState = 7;
    public const int AttackSequenceId = 5;
    public const int DamageNativeState = 12;
    public const int TerminalNativeStateFd = 0xfd;
    public const int TerminalNativeStateFe = 0xfe;
    public const double AttackDistanceExclusive = 2d;
    public const uint AttackFacingErrorRawBits = 0x3e32b8c2u;
    public static readonly float AttackFacingErrorExclusive =
        BitConverter.Int32BitsToSingle(unchecked((int)AttackFacingErrorRawBits));
    public const double AttackMarker = 34d;
    public const double AttackDamage = 1d;

    public static Rac1Class749AuthoredState? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != NativeClassId) return null;
        byte[] pvar = RequirePVar(source);
        float health = ReadHealth(pvar);
        return new Rac1Class749AuthoredState(
            new Rac1Class749Key(source.NativeClassId, source.InstanceIndex), health, pvar.Length);
    }

    internal static byte[] RequirePVar(RuntimeDynamicObject source)
    {
        var payloads = source.NativePayloads?
            .Where(p => p.Format == PVarPayloadFormat)
            .ToArray() ?? [];
        if (payloads.Length != 1)
            throw new InvalidDataException(
                $"R&C1 class-749 instance {source.InstanceIndex} requires exactly one {PVarPayloadFormat} payload.");
        byte[] pvar = payloads[0].Data;
        if (pvar.Length != PVarSize)
            throw new InvalidDataException(
                $"R&C1 class-749 instance {source.InstanceIndex} PVar is 0x{pvar.Length:x} bytes, expected 0x{PVarSize:x}.");
        _ = ReadHealth(pvar);
        return pvar.ToArray();
    }

    internal static float ReadHealth(ReadOnlySpan<byte> pvar)
    {
        int bits = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(HealthOffset, sizeof(int)));
        float health = BitConverter.Int32BitsToSingle(bits);
        if (!float.IsFinite(health))
            throw new InvalidDataException("R&C1 class-749 health is non-finite.");
        return health;
    }

    internal static void WriteHealth(Span<byte> pvar, float health)
    {
        if (!float.IsFinite(health)) throw new ArgumentOutOfRangeException(nameof(health));
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.Slice(HealthOffset, sizeof(int)), BitConverter.SingleToInt32Bits(health));
    }
}

public readonly record struct Rac1Class749Key(int NativeClassId, int InstanceIndex);

public sealed record Rac1Class749AuthoredState(
    Rac1Class749Key Key,
    float Health,
    int PVarSize);

/// <summary>
/// Host-supplied targeting facts for the recovered state-6 attack selector.
/// Distance and facing error are already-resolved native-world magnitudes; this contract
/// deliberately assigns no locomotion, helper-argument or acquisition-radius semantics.
/// </summary>
public readonly record struct Rac1Class749TargetFacts(
    bool TargetAcquired,
    double Distance,
    double FacingError);

public sealed record Rac1Class749AttackEvent(
    double NativeMarker,
    double NativeDamage);

/// <summary>
/// Deterministic engine-independent probe returned to a host. Native semantics stay here;
/// only the embedded RuntimeEntityState carries the independently justified neutral lifetime projection.
/// </summary>
public sealed record Rac1Class749HostProbe(
    Rac1Class749Key Key,
    int NativeState,
    float Health,
    int? NativeSequence,
    Rac1Class749AttackEvent? Attack,
    RuntimeEntityState EntityState);

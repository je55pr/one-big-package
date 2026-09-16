using System.Buffers.Binary;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded retail-backed native facts for the representative Veldin class-749 hostile.
/// Targeting, state, health, sequence and attack semantics intentionally remain R&amp;C1-specific.
/// </summary>
public static class Rac1Class749Hostile
{
    public const int NativeClassId = 749;
    public const string PVarPayloadFormat = "rac1-pvar";
    public const int PVarSize = 0x280;
    public const int HealthOffset = 0x20;
    public const int StatusSentinelOffset = 0x1c4;
    public const int HomePositionOffset = 0x1d0;
    public const int StatusSentinelTwo = 2;

    public const int TargetSearchNativeState = 5;
    public const int TargetedNativeState = 6;
    public const int AttackNativeState = 7;
    public const int ReturnHomeNativeState = 8;
    public const int StateFiveSequenceId = 1;
    public const int StateSixOrEightSequenceId = 4;
    public const int AttackSequenceId = 5;
    public const int DamageNativeState = 12;
    public const int TerminalNativeStateFd = 0xfd;
    public const int TerminalNativeStateFe = 0xfe;

    public const double AttackDistanceExclusive = 2d;
    public const double AttackRetainDistanceInclusive = 1.5d;
    public const double HomeDistanceExclusive = 1.5d;
    public const uint AttackFacingErrorRawBits = 0x3e32b8c2u;
    public static readonly float AttackFacingErrorExclusive =
        BitConverter.Int32BitsToSingle(unchecked((int)AttackFacingErrorRawBits));

    public const int AttackSequenceFrameCount = 21;
    public const int AttackSequenceNativeUpdates = 102;
    public const int AttackMarkerFrameIndex = 13;
    public const int AttackMarkerNativeUpdate = 68;
    public const double AttackMarker = 34d;
    public const double AttackDamage = 1d;

    public static Rac1Class749AuthoredState? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != NativeClassId) return null;
        byte[] pvar = RequirePVar(source);
        return new Rac1Class749AuthoredState(
            new Rac1Class749Key(source.NativeClassId, source.InstanceIndex),
            ReadHealth(pvar),
            pvar.Length,
            ReadStatusSentinel(pvar),
            ReadHomePosition(pvar));
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
        _ = ReadHomePosition(pvar);
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

    internal static int ReadStatusSentinel(ReadOnlySpan<byte> pvar) =>
        BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(StatusSentinelOffset, sizeof(int)));

    internal static Rac1Class749WorldPoint ReadHomePosition(ReadOnlySpan<byte> pvar)
    {
        float nativeX = ReadFiniteSingle(pvar, HomePositionOffset);
        float nativeY = ReadFiniteSingle(pvar, HomePositionOffset + sizeof(float));
        float nativeZ = ReadFiniteSingle(pvar, HomePositionOffset + 2 * sizeof(float));
        return new Rac1Class749WorldPoint(nativeX, nativeZ, nativeY);
    }

    internal static void WriteHealth(Span<byte> pvar, float health)
    {
        if (!float.IsFinite(health)) throw new ArgumentOutOfRangeException(nameof(health));
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.Slice(HealthOffset, sizeof(int)), BitConverter.SingleToInt32Bits(health));
    }

    private static float ReadFiniteSingle(ReadOnlySpan<byte> pvar, int offset)
    {
        int bits = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(offset, sizeof(int)));
        float value = BitConverter.Int32BitsToSingle(bits);
        if (!float.IsFinite(value))
            throw new InvalidDataException($"R&C1 class-749 PVar float at +0x{offset:x} is non-finite.");
        return value;
    }
}

public readonly record struct Rac1Class749Key(int NativeClassId, int InstanceIndex);

public readonly record struct Rac1Class749WorldPoint(double X, double Y, double Z)
{
    public double DistanceTo(Rac1Class749WorldPoint other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        double dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public sealed record Rac1Class749AuthoredState(
    Rac1Class749Key Key,
    float Health,
    int PVarSize,
    int StatusSentinel,
    Rac1Class749WorldPoint HomePosition);

/// <summary>
/// Host-supplied world facts used by the recovered class-749 states. The optional
/// status value is deliberately unnamed beyond its native PVar +0x1c4 role.
/// </summary>
public readonly record struct Rac1Class749TargetFacts(
    double Distance,
    double FacingError,
    Rac1Class749WorldPoint CurrentPosition = default,
    int? StatusSentinel = null);

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
    int NativeSequenceUpdate,
    Rac1Class749AttackEvent? Attack,
    RuntimeEntityState EntityState);

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
    public const int RetainedRuntimeWitnessLevelId = 0;
    public const int RetainedRuntimeWitnessInstanceIndex = 149;
    public const string PVarPayloadFormat = "rac1-pvar";
    public const int PVarSize = 0x280;
    public const int HealthOffset = 0x20;
    public const int TargetDestinationOffset = 0x180;
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
    public const int TerminalNativeStateFd = Rac1MobyRuntime.TerminalNativeStateFd;
    public const int TerminalNativeStateFe = Rac1MobyRuntime.TerminalNativeStateFe;

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

    /// <summary>
    /// Evidence-policy gate for the retained loaded-Veldin runtime witness.
    /// Other class-749 placements remain authored/presented data until their live activation
    /// inputs are independently recovered.
    /// </summary>
    public static bool IsRetainedRuntimeWitness(int levelId, RuntimeDynamicObject source) =>
        levelId == RetainedRuntimeWitnessLevelId &&
        source.SourceGame == "rac1" &&
        source.NativeClassId == NativeClassId &&
        source.InstanceIndex == RetainedRuntimeWitnessInstanceIndex;

    public static Rac1Class749AuthoredState? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != NativeClassId) return null;
        byte[] pvar = RequirePVar(source);
        return new Rac1Class749AuthoredState(
            new Rac1Class749Key(source.NativeClassId, source.InstanceIndex),
            ReadHealth(pvar),
            pvar.Length,
            ReadTargetDestination(pvar),
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
        _ = ReadTargetDestination(pvar);
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

    internal static Rac1Class749WorldPoint ReadTargetDestination(ReadOnlySpan<byte> pvar) =>
        ReadWorldPoint(pvar, TargetDestinationOffset);

    internal static Rac1Class749WorldPoint ReadHomePosition(ReadOnlySpan<byte> pvar) =>
        ReadWorldPoint(pvar, HomePositionOffset);

    private static Rac1Class749WorldPoint ReadWorldPoint(ReadOnlySpan<byte> pvar, int offset)
    {
        float nativeX = ReadFiniteSingle(pvar, offset);
        float nativeY = ReadFiniteSingle(pvar, offset + sizeof(float));
        float nativeZ = ReadFiniteSingle(pvar, offset + 2 * sizeof(float));
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
    Rac1Class749WorldPoint TargetDestination,
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

public enum Rac1Class749NavigationIntentKind
{
    PursueRecoveredTarget,
    ReturnHome,
}

public sealed record Rac1Class749NavigationIntent(
    Rac1Class749NavigationIntentKind Kind,
    Rac1Class749WorldPoint? Destination = null) : IRac1MobyHostIntent;

public sealed record Rac1Class749AttackEvent(
    double NativeMarker,
    double NativeDamage) : IRac1MobyHostEvent;

/// <summary>
/// Deterministic engine-independent probe returned to a host. Class-specific
/// policy stays here while the embedded live-Moby state carries only recovered
/// engine-common native state and neutral lifetime projection.
/// </summary>
public sealed record Rac1Class749HostProbe(
    Rac1Class749Key Key,
    float Health,
    int? NativeSequence,
    int NativeSequenceUpdate,
    Rac1Class749AttackEvent? Attack,
    Rac1MobyRuntimeState RuntimeState,
    IReadOnlyList<IRac1MobyHostIntent> HostIntents,
    IReadOnlyList<IRac1MobyHostEvent> HostEvents)
{
    public int NativeState => RuntimeState.NativeState;
    public RuntimeEntityState EntityState => RuntimeState.EntityState;
}

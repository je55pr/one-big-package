using System.Buffers.Binary;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Recovered class-1440 damageable/targeting family boundary from the fixed
/// Veldin witness. The name deliberately stops short of assigning unresolved
/// outgoing damage or generic hostile semantics.
/// </summary>
public static class Rac1Class1440ContactFamily
{
    public const int NativeClassId = 1440;
    public const string PVarPayloadFormat = "rac1-pvar";
    public const int HealthOffset = 0x20;
    public const int TargetMobyOffset = 0x110;
    public const int MinimumPVarSize = TargetMobyOffset + sizeof(uint);
    public const int NativeStateCount = 9;
    public const int ContactQueryNativeState = 5;
    public const int ContactTransitionNativeState = 6;
    public const float WitnessHealth = 2f;

    public const int ContactQuerySiteCount = 2;
    public const uint ContactDescriptorWord84 = 0x00010000u;
    public const float ContactDescriptorScalar8c = 1f;
    public const int ContactDescriptorInteger90 = 1;

    public static Rac1Class1440AuthoredState? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != NativeClassId) return null;
        byte[] pvar = RequirePVar(source);
        return new Rac1Class1440AuthoredState(
            new Rac1Class1440Key(source.NativeClassId, source.InstanceIndex),
            ReadHealth(pvar),
            pvar.Length);
    }

    internal static byte[] RequirePVar(RuntimeDynamicObject source)
    {
        var payloads = source.NativePayloads?
            .Where(p => p.Format == PVarPayloadFormat)
            .ToArray() ?? [];
        if (payloads.Length != 1)
            throw new InvalidDataException(
                $"R&C1 class-1440 instance {source.InstanceIndex} requires exactly one {PVarPayloadFormat} payload.");

        byte[] pvar = payloads[0].Data;
        if (pvar.Length < MinimumPVarSize)
            throw new InvalidDataException(
                $"R&C1 class-1440 instance {source.InstanceIndex} PVar is 0x{pvar.Length:x} bytes; " +
                $"recovered fields require at least 0x{MinimumPVarSize:x}.");
        _ = ReadHealth(pvar);
        return pvar.ToArray();
    }

    internal static float ReadHealth(ReadOnlySpan<byte> pvar)
    {
        int bits = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(HealthOffset, sizeof(int)));
        float health = BitConverter.Int32BitsToSingle(bits);
        if (!float.IsFinite(health))
            throw new InvalidDataException("R&C1 class-1440 health is non-finite.");
        return health;
    }

    internal static void WriteHealth(Span<byte> pvar, float health)
    {
        if (!float.IsFinite(health)) throw new ArgumentOutOfRangeException(nameof(health));
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.Slice(HealthOffset, sizeof(int)),
            BitConverter.SingleToInt32Bits(health));
    }

    internal static void ValidateRecoveredNativeState(int nativeState)
    {
        if (nativeState is < 0 or >= NativeStateCount)
            throw new ArgumentOutOfRangeException(
                nameof(nativeState),
                $"R&C1 class-1440 recovered dispatch covers native states 0..{NativeStateCount - 1}.");
    }
}

public readonly record struct Rac1Class1440Key(int NativeClassId, int InstanceIndex);

public sealed record Rac1Class1440AuthoredState(
    Rac1Class1440Key Key,
    float Health,
    int PVarSize);

/// <summary>
/// Identifies one of the two recovered VU-backed query sites in native state 5.
/// The descriptor values are preserved as opaque native facts; the 1.0 scalar
/// is not named as damage.
/// </summary>
public sealed record Rac1Class1440ContactQueryIntent(
    Rac1MobyRuntimeKey Source,
    int QuerySite,
    uint NativeWord84,
    int NativeClassId,
    float NativeScalar8c,
    int NativeInteger90) : IRac1MobyHostIntent;

public sealed record Rac1Class1440PlayerContactEvent(
    Rac1MobyRuntimeKey Key,
    int QuerySite) : IRac1MobyHostEvent;

/// <summary>
/// Deterministic host-facing snapshot. Only recovered state-5 contact branching
/// and class-owned damage intake are implemented; states 0..4 and 6..8 remain
/// opaque native states until their semantics are independently recovered.
/// </summary>
public sealed record Rac1Class1440ContactProbe(
    Rac1Class1440Key Key,
    float Health,
    Rac1MobyRuntimeState RuntimeState,
    IReadOnlyList<IRac1MobyHostIntent> HostIntents,
    IReadOnlyList<IRac1MobyHostEvent> HostEvents)
{
    public int NativeState => RuntimeState.NativeState;
    public RuntimeEntityState EntityState => RuntimeState.EntityState;
}

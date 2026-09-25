using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// TABLE1 evidence-safe profile for UYA native class 5821.
///
/// Retail proves an actor-local lifetime scalar and a live target pointer, but
/// the native incoming-damage admission and outbound attack consequence are not
/// yet recovered. This type therefore exposes observations only and deliberately
/// does not implement <see cref="IUyaMobyDamageConsumer"/>.
/// </summary>
public static class UyaClass5821Actor
{
    public const int NativeClassId = 5821;
    public const int PVarSize = 0x5F0;
    public const int AuthoredLifetimeOffset = 0x34;
    public const int RuntimeLifetimeOffset = 0x30;
    public const int RuntimeTargetPointerOffset = 0x230;
    public const byte NativeDormantState = 0x00;
    public const byte NativeObservedTerminalState = 0xFD;
    public const double NativeObservedTerminalLifetime = -1999d;
    public static UyaClass5821AuthoredState ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac3" || source.NativeClassId != NativeClassId)
            throw new ArgumentException(
                $"Source is not UYA native class {NativeClassId}.",
                nameof(source));

        byte[] pvar = source.NativePayloads?
            .SingleOrDefault(payload =>
                payload.Format == UyaMobyRuntimeSession.PVarPayloadFormat)?.Data
            ?? throw new InvalidDataException(
                $"UYA class {NativeClassId} instance {source.InstanceIndex} has no PVar payload.");

        if (pvar.Length != PVarSize)
            throw new InvalidDataException(
                $"UYA class {NativeClassId} instance {source.InstanceIndex} has " +
                $"PVar length {pvar.Length}, expected {PVarSize}.");

        short authoredLifetime = BinaryPrimitives.ReadInt16LittleEndian(
            pvar.AsSpan(AuthoredLifetimeOffset, sizeof(short)));

        return new UyaClass5821AuthoredState(
            new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex),
            authoredLifetime);
    }
    public static UyaClass5821NativeObservation Observe(
        byte nativeState,
        double nativeLifetime,
        bool targetsRatchet)
    {
        if (!double.IsFinite(nativeLifetime))
            throw new ArgumentOutOfRangeException(nameof(nativeLifetime));

        return new UyaClass5821NativeObservation(
            nativeState,
            nativeLifetime,
            targetsRatchet);
    }

    /// <summary>
    /// Exact terminal projection admitted by the retained TABLE1 runtime witness:
    /// state 0xFD with a non-positive lifetime. State 0xFE is intentionally not
    /// promoted here merely because the update routine checks it on linked Mobies.
    /// </summary>
    public static bool IsObservedTerminal(
        UyaClass5821NativeObservation observation) =>
        observation.NativeState == NativeObservedTerminalState &&
        observation.NativeLifetime <= 0d;
}

public sealed record UyaClass5821AuthoredState(
    UyaMobyRuntimeKey Key,
    short AuthoredLifetime);

public sealed record UyaClass5821NativeObservation(
    byte NativeState,
    double NativeLifetime,
    bool TargetsRatchet);

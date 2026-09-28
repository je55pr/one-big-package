using System.Buffers.Binary;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Retail-backed TABLE1 behavior for UYA oClass 500. The familiar crate identity
/// is deliberately not encoded here: UYA retail proves a destructible/reward
/// family, while the exact player-facing object name is still only corroborated
/// by cross-game tooling.
/// </summary>
public static class UyaClass500Destructible
{
    public const int NativeClassId = 500;
    public const int PVarSize = 0x140;
    public const string InstancePayloadFormat = "rac3-moby-instance-gc-layout-compat";

    public const int AuthoredUidOffset = 0x10;
    public const int AuthoredValueOffset = 0x14;
    public const int PVarB0Offset = 0xb0;
    public const int PVarC8Offset = 0xc8;
    public const int PVarF8Offset = 0xf8;
    public const int PVarFbOffset = 0xfb;

    public const int ActiveNativeState = 1;
    public const int BreakTransitionNativeState = 3;

    // TABLE1 loaded update 0x0033A5C0 calls the owned-damage resolver with
    // this exact mask, drops exact 0x02000000, then state 1 requires positive
    // record +0x2c before entering the break/reward helpers.
    public const uint QueriedDamageMask = 0x0b030001;
    public const uint IgnoredExactDamageFlags = 0x02000000;

    public static bool ShouldBreak(uint damageFlags, double damageHp) =>
        (damageFlags & QueriedDamageMask) != 0 &&
        damageFlags != IgnoredExactDamageFlags &&
        damageHp > 0d;

    public static UyaClass500AuthoredState ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac3" || source.NativeClassId != NativeClassId)
            throw new ArgumentException(
                "Source is not a UYA class-500 authored Moby.",
                nameof(source));

        byte[] raw = RequirePayload(source, InstancePayloadFormat, minimumSize: 0x18);
        byte[] pvar = RequirePayload(
            source,
            UyaMobyRuntimeSession.PVarPayloadFormat,
            minimumSize: PVarSize);
        if (pvar.Length != PVarSize)
            throw new InvalidDataException(
                $"UYA class-500 PVar is {pvar.Length} bytes; TABLE1/native family evidence expects 0x{PVarSize:x}.");

        int uid = BinaryPrimitives.ReadInt32LittleEndian(
            raw.AsSpan(AuthoredUidOffset, sizeof(int)));
        int authoredValue = BinaryPrimitives.ReadInt32LittleEndian(
            raw.AsSpan(AuthoredValueOffset, sizeof(int)));
        uint pvarB0 = BinaryPrimitives.ReadUInt32LittleEndian(
            pvar.AsSpan(PVarB0Offset, sizeof(uint)));

        return new UyaClass500AuthoredState(
            uid,
            authoredValue,
            pvarB0,
            pvar[PVarC8Offset],
            pvar[PVarF8Offset],
            pvar[PVarFbOffset]);
    }
    public static UyaClass500PostBreakRoute PostBreakRoute(
        UyaClass500AuthoredState authored)
    {
        // State 3 takes PVar+F8 == 0 into 0x0033CA98. That helper calls the
        // native deactivation path directly when PVar+B0 == 0. Every authored
        // TABLE1 class-500 instance satisfies both conditions.
        return authored.PVarF8 == 0 && authored.PVarB0 == 0
            ? UyaClass500PostBreakRoute.Deactivate
            : UyaClass500PostBreakRoute.Unrecovered;
    }

    private static byte[] RequirePayload(
        RuntimeDynamicObject source,
        string format,
        int minimumSize)
    {
        byte[]? bytes = source.NativePayloads?
            .SingleOrDefault(payload => payload.Format == format)?.Data;
        if (bytes is null || bytes.Length < minimumSize)
            throw new InvalidDataException(
                $"UYA class-500 source is missing required '{format}' payload.");
        return bytes;
    }
}

public sealed record UyaClass500AuthoredState(
    int Uid,
    int AuthoredValue,
    uint PVarB0,
    byte PVarC8,
    byte PVarF8,
    byte PVarFb);

public enum UyaClass500PostBreakRoute
{
    Deactivate,
    Unrecovered,
}

public sealed record UyaClass500BreakResult(
    UyaClass500AuthoredState Authored,
    int NativeStateBefore,
    int NativeBreakTransitionState,
    UyaClass500PostBreakRoute PostBreakRoute,
    RuntimeEntityState EntityState);
/// <summary>
/// Class-local damage consequence for the recovered UYA class-500 ordinary
/// TABLE1 path. It deliberately refuses PVar variants whose post-break native
/// route has not been recovered.
/// </summary>
public sealed class UyaClass500DestructibleSession : IUyaMobyDamageConsumer
{
    private readonly UyaMobyRuntimeSession _runtime;

    public UyaClass500DestructibleSession(UyaMobyRuntimeSession runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.RegisterDamageConsumer(this);
    }

    public int NativeClassId => UyaClass500Destructible.NativeClassId;

    public bool CanApplyDamage(
        RuntimeDynamicObject source,
        UyaGameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(damage);
        if (source.SourceGame != "rac3" ||
            source.NativeClassId != NativeClassId ||
            !damage.Target.MatchesMoby(
                new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex)) ||
            damage.NativeDamageFlags is not uint flags ||
            damage.NativeDamage is not double damageHp ||
            !UyaClass500Destructible.ShouldBreak(flags, damageHp))
            return false;

        var authored = UyaClass500Destructible.ReadAuthored(source);
        return UyaClass500Destructible.PostBreakRoute(authored) ==
            UyaClass500PostBreakRoute.Deactivate;
    }

    public object ApplyDamage(
        RuntimeDynamicObject source,
        UyaGameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage))
            throw new NotSupportedException(
                "UYA class-500 damage does not match the recovered ordinary TABLE1 break route.");

        var instance = _runtime.Require(source);
        var authored = UyaClass500Destructible.ReadAuthored(source);
        UyaMobyRuntimeState terminal = _runtime.Terminalize(instance);

        return new UyaClass500BreakResult(
            authored,
            UyaClass500Destructible.ActiveNativeState,
            UyaClass500Destructible.BreakTransitionNativeState,
            UyaClass500PostBreakRoute.Deactivate,
            terminal.EntityState);
    }
}

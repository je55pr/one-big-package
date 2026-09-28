using System.Buffers.Binary;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class UyaClass500DestructibleTests
{
    [Theory]
    [InlineData(0x00000001u, 1d, true)]
    [InlineData(0x00010000u, 0.25d, true)]
    [InlineData(0x02000001u, 1d, true)]
    [InlineData(0x00000000u, 1d, false)]
    [InlineData(0x02000000u, 1d, false)]
    [InlineData(0x00000001u, 0d, false)]
    [InlineData(0x00000001u, -1d, false)]
    public void BreakPredicateMatchesTable1LoadedStateOne(
        uint damageFlags,
        double damageHp,
        bool expected)
    {
        Assert.Equal(
            expected,
            UyaClass500Destructible.ShouldBreak(damageFlags, damageHp));
    }

    [Fact]
    public void OrdinaryAuthoredRouteTerminalizesThroughUyaRuntime()
    {
        var source = Class500(
            instanceIndex: 311,
            uid: 352,
            authoredValue: 44,
            pvarB0: 0,
            pvarF8: 0);
        var runtime = new UyaMobyRuntimeSession();
        var instance = runtime.Register(source);
        _ = new UyaClass500DestructibleSession(runtime);
        var damage = Damage(source, 0x00000001u, 1d);

        var result = runtime.DispatchDamage<UyaClass500BreakResult>(damage);

        Assert.Equal(352, result.Authored.Uid);
        Assert.Equal(44, result.Authored.AuthoredValue);
        Assert.Equal(1, result.NativeStateBefore);
        Assert.Equal(3, result.NativeBreakTransitionState);
        Assert.Equal(UyaClass500PostBreakRoute.Deactivate, result.PostBreakRoute);
        Assert.Equal(RuntimeEntityPresence.Inactive, result.EntityState.Presentation.Presence);
        Assert.False(instance.IsActive);
        Assert.False(runtime.CanDispatchDamage(damage));
    }
    [Fact]
    public void UnrecoveredPvarVariantFailsClosed()
    {
        var source = Class500(
            instanceIndex: 1,
            uid: 2,
            authoredValue: 44,
            pvarB0: 1,
            pvarF8: 0);
        var runtime = new UyaMobyRuntimeSession();
        runtime.Register(source);
        _ = new UyaClass500DestructibleSession(runtime);
        var damage = Damage(source, 0x00000001u, 1d);

        Assert.False(runtime.CanDispatchDamage(damage));
        Assert.False(runtime.TryDispatchDamage<UyaClass500BreakResult>(
            damage,
            out var result));
        Assert.Null(result);
        Assert.True(runtime.Require(source).IsActive);
        Assert.Throws<NotSupportedException>(() =>
            runtime.DispatchDamage<UyaClass500BreakResult>(damage));
    }

    [Fact]
    public void UnknownClassDoesNotGainClass500DamageSemantics()
    {
        var source = Class500(
            instanceIndex: 7,
            uid: 9,
            authoredValue: 44,
            pvarB0: 0,
            pvarF8: 0) with
        {
            NativeClassId = 5821,
            ModelRef = "moby:5821",
        };
        var runtime = new UyaMobyRuntimeSession();
        runtime.Register(source);
        _ = new UyaClass500DestructibleSession(runtime);
        var damage = new UyaGameplayDamageEvent(
            UyaGameplayEntityRef.Player,
            UyaGameplayEntityRef.Moby(
                new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: 1d,
            nativeDamageFlags: 1u);

        Assert.False(runtime.CanDispatchDamage(damage));
        Assert.True(runtime.Require(source).IsActive);
    }

    [Fact]
    public void AuthoredReaderRejectsMissingOrWrongSizedAuthority()
    {
        var source = Class500(
            instanceIndex: 1,
            uid: 2,
            authoredValue: 44,
            pvarB0: 0,
            pvarF8: 0);
        var withoutPvar = source with
        {
            NativePayloads = source.NativePayloads!
                .Where(p => p.Format != UyaMobyRuntimeSession.PVarPayloadFormat)
                .ToArray(),
        };

        Assert.Throws<InvalidDataException>(() =>
            UyaClass500Destructible.ReadAuthored(withoutPvar));
        Assert.Throws<ArgumentException>(() =>
            UyaClass500Destructible.ReadAuthored(source with { SourceGame = "rac2" }));
    }
    private static UyaGameplayDamageEvent Damage(
        RuntimeDynamicObject source,
        uint flags,
        double hp) =>
        new(
            UyaGameplayEntityRef.Player,
            UyaGameplayEntityRef.Moby(
                new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: hp,
            nativeDamageFlags: flags);

    private static RuntimeDynamicObject Class500(
        int instanceIndex,
        int uid,
        int authoredValue,
        uint pvarB0,
        byte pvarF8)
    {
        byte[] raw = new byte[0x88];
        BinaryPrimitives.WriteInt32LittleEndian(
            raw.AsSpan(UyaClass500Destructible.AuthoredUidOffset, 4),
            uid);
        BinaryPrimitives.WriteInt32LittleEndian(
            raw.AsSpan(UyaClass500Destructible.AuthoredValueOffset, 4),
            authoredValue);

        byte[] pvar = new byte[UyaClass500Destructible.PVarSize];
        BinaryPrimitives.WriteUInt32LittleEndian(
            pvar.AsSpan(UyaClass500Destructible.PVarB0Offset, 4),
            pvarB0);
        pvar[UyaClass500Destructible.PVarF8Offset] = pvarF8;

        return new RuntimeDynamicObject(
            "rac3",
            UyaClass500Destructible.NativeClassId,
            instanceIndex,
            uid,
            $"moby:{UyaClass500Destructible.NativeClassId}",
            $"table:1:moby:{instanceIndex}",
            new RuntimeObjectTransform(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            ]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload(
                    UyaClass500Destructible.InstancePayloadFormat,
                    raw),
                new RuntimeOpaquePayload(
                    UyaMobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ]);
    }
}

using System.Buffers.Binary;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using static OBP.Tests.Rac3Class5821Fixtures;

namespace OBP.Tests;

/// <summary>Class-5821 lifetime damage admission, damage dispatch, terminal projection and the class-500 separation.</summary>
public sealed class Rac3Class5821DamageTests
{
    [Theory]
    [InlineData(0x00010000u, 1d, 0, true)]
    [InlineData(0x00010001u, 0.25d, 5, true)]
    [InlineData(0x00000001u, 1d, 0, false)]
    [InlineData(0x00010000u, 1d, 10, false)]
    [InlineData(0x00010000u, 0d, 0, false)]
    [InlineData(0x00010000u, -1d, 0, false)]
    public void LifetimeDamageAdmissionMatchesRecoveredResolverPath(
        uint flags,
        double damage,
        byte recordKind,
        bool expected)
    {
        Assert.Equal(
            expected,
            Rac3Class5821Actor.AdmitsLifetimeDamage(
                flags,
                damage,
                recordKind));
    }

    [Fact]
    public void PositiveSubUnitDamageClampsToOneAndMakesLifetimeNonPositive()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        var runtime = new Rac3MobyRuntimeSession();
        var instance = runtime.Register(source);
        _ = new Rac3Class5821DamageSession(runtime);
        var damage = Damage(source, hp: 0.25d, flags: 0x00010000u, recordKind: 0);

        var result = runtime.DispatchDamage<Rac3Class5821DamageResult>(damage);

        Assert.Equal(1f, result.LifetimeBefore);
        Assert.Equal(0.25f, result.NativeRecordDamage);
        Assert.Equal(1f, result.AppliedLifetimeDamage);
        Assert.Equal(0f, result.LifetimeAfter);
        Assert.True(result.ReachedNonPositiveLifetime);
        Assert.True(instance.IsActive);
        Assert.False(runtime.CanDispatchDamage(damage));
    }

    [Fact]
    public void PositiveDamageSubtractsFromRecoveredRuntimeLifetime()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        var runtime = new Rac3MobyRuntimeSession();
        runtime.Register(source);
        _ = new Rac3Class5821DamageSession(runtime);

        var result = runtime.DispatchDamage<Rac3Class5821DamageResult>(
            Damage(source, hp: 2d, flags: 0x00010000u, recordKind: 3));

        Assert.Equal(1f, result.LifetimeBefore);
        Assert.Equal(2f, result.AppliedLifetimeDamage);
        Assert.Equal(-1f, result.LifetimeAfter);
        Assert.True(result.ReachedNonPositiveLifetime);
    }

    [Fact]
    public void RecordKindTenAndUnknownKindFailClosed()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        var runtime = new Rac3MobyRuntimeSession();
        runtime.Register(source);
        _ = new Rac3Class5821DamageSession(runtime);

        Assert.False(runtime.CanDispatchDamage(
            Damage(source, hp: 1d, flags: 0x00010000u, recordKind: 10)));

        var unknownKind = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(
                new Rac3MobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: 1d,
            nativeDamageFlags: 0x00010000u);
        Assert.False(runtime.CanDispatchDamage(unknownKind));
    }

    [Fact]
    public void UnrecoveredDamageProfileFailsClosed()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        source = WithPvar(source, pvar =>
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac3Class5821Actor.LifetimeRelativePointerOffset, sizeof(int)),
                0x44));

        var runtime = new Rac3MobyRuntimeSession();
        runtime.Register(source);
        _ = new Rac3Class5821DamageSession(runtime);

        Assert.False(runtime.CanDispatchDamage(
            Damage(source, hp: 1d, flags: 0x00010000u, recordKind: 0)));
    }

    [Theory]
    [InlineData(0xFD, -1999d, true, true)]
    [InlineData(0xFD, 0d, true, true)]
    [InlineData(0xFD, 1d, true, false)]
    [InlineData(0x00, -1999d, false, false)]
    [InlineData(0xFE, -1999d, true, false)]
    public void TerminalProjectionRequiresBothObservedInactiveStateAndNonPositiveLifetime(
        byte nativeState,
        double nativeLifetime,
        bool targetsRatchet,
        bool expectedTerminal)
    {
        var observation = Rac3Class5821Actor.Observe(
            nativeState,
            nativeLifetime,
            targetsRatchet);

        Assert.Equal(
            expectedTerminal,
            Rac3Class5821Actor.IsObservedTerminal(observation));
    }

    [Fact]
    public void Class5821DoesNotGainClass500DamageAdmission()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        var runtime = new Rac3MobyRuntimeSession();
        runtime.Register(source);
        _ = new Rac3Class500DestructibleSession(runtime);
        var damage = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(
                new Rac3MobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: 1d,
            nativeDamageFlags: 1u);

        Assert.False(runtime.CanDispatchDamage(damage));
        Assert.True(runtime.Require(source).IsActive);
    }

    private static Rac3GameplayDamageEvent Damage(
        RuntimeDynamicObject source,
        double hp,
        uint flags,
        byte recordKind) =>
        new(
            Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(
                new Rac3MobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: hp,
            nativeDamageFlags: flags,
            nativeRecordKind: recordKind);
}

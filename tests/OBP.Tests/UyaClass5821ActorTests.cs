using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class UyaClass5821ActorTests
{
    [Fact]
    public void AuthoredReaderPreservesRecoveredDamageProfile()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);

        UyaClass5821AuthoredState authored =
            UyaClass5821Actor.ReadAuthored(source);

        Assert.Equal(new UyaMobyRuntimeKey(5821, 430), authored.Key);
        Assert.Equal(0x30, authored.LifetimeRelativePointer);
        Assert.Equal(0x160, authored.DamageConfigRelativePointer);
        Assert.Equal(1f, authored.InitialLifetime);
        Assert.Equal(1, authored.AuthoredLifetime);
        Assert.Equal(0f, authored.DamageConfigVerticalThreshold);
        Assert.Equal(0, authored.DamageConfigByte49);
        Assert.Equal(0f, authored.DamageConfigSameClassMultiplier);
        Assert.Equal(1f, authored.DamageConfigOptionalMultiplier);
        Assert.True(UyaClass5821Actor.HasRecoveredTable1DamageProfile(authored));
    }

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
            UyaClass5821Actor.AdmitsLifetimeDamage(
                flags,
                damage,
                recordKind));
    }

    [Fact]
    public void State25AttackDescriptorMatchesTable1Emitter()
    {
        UyaClass5821AttackDescriptor attack =
            UyaClass5821Actor.NativeState25Attack();

        Assert.Equal(25, attack.NativeState);
        Assert.Equal(0.5f, attack.Radius);
        Assert.Equal(1f, attack.Damage);
        Assert.Equal(0x02000001u, attack.Flags);
        Assert.Equal(0, attack.RecordKind);
        Assert.Equal(1, attack.RecordByte29);

        var source = new UyaMobyRuntimeKey(
            UyaClass5821Actor.NativeClassId,
            430);
        UyaGameplayDamageEvent playerDamage =
            UyaClass5821Actor.NativeState25RatchetDamage(source);
        Assert.Equal(UyaGameplayEntityRef.Moby(source), playerDamage.Source);
        Assert.Equal(UyaGameplayEntityRef.Player, playerDamage.Target);
        Assert.Equal(1d, playerDamage.NativeDamage);
        Assert.Equal(0x02000001u, playerDamage.NativeDamageFlags);
        Assert.Equal((byte)0, playerDamage.NativeRecordKind);
    }

    [Fact]
    public void PositiveSubUnitDamageClampsToOneAndMakesLifetimeNonPositive()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        var runtime = new UyaMobyRuntimeSession();
        var instance = runtime.Register(source);
        _ = new UyaClass5821DamageSession(runtime);
        var damage = Damage(source, hp: 0.25d, flags: 0x00010000u, recordKind: 0);

        var result = runtime.DispatchDamage<UyaClass5821DamageResult>(damage);

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
        var runtime = new UyaMobyRuntimeSession();
        runtime.Register(source);
        _ = new UyaClass5821DamageSession(runtime);

        var result = runtime.DispatchDamage<UyaClass5821DamageResult>(
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
        var runtime = new UyaMobyRuntimeSession();
        runtime.Register(source);
        _ = new UyaClass5821DamageSession(runtime);

        Assert.False(runtime.CanDispatchDamage(
            Damage(source, hp: 1d, flags: 0x00010000u, recordKind: 10)));

        var unknownKind = new UyaGameplayDamageEvent(
            UyaGameplayEntityRef.Player,
            UyaGameplayEntityRef.Moby(
                new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: 1d,
            nativeDamageFlags: 0x00010000u);
        Assert.False(runtime.CanDispatchDamage(unknownKind));
    }

    [Fact]
    public void UnrecoveredDamageProfileFailsClosed()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        byte[] pvar = source.NativePayloads!
            .Single(payload => payload.Format == UyaMobyRuntimeSession.PVarPayloadFormat)
            .Data.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(UyaClass5821Actor.LifetimeRelativePointerOffset, sizeof(int)),
            0x44);
        source = source with
        {
            NativePayloads =
            [
                new RuntimeOpaquePayload(
                    UyaMobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ],
        };

        var runtime = new UyaMobyRuntimeSession();
        runtime.Register(source);
        _ = new UyaClass5821DamageSession(runtime);

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
        var observation = UyaClass5821Actor.Observe(
            nativeState,
            nativeLifetime,
            targetsRatchet);

        Assert.Equal(
            expectedTerminal,
            UyaClass5821Actor.IsObservedTerminal(observation));
    }

    [Fact]
    public void Class5821DoesNotGainClass500DamageAdmission()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
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

    [SkippableFact]
    public void RetailTable1Class5821PopulationCarriesRecoveredDamageProfile()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld world = Rac3WorldImport.Build(reader, 1);
        var sources = world.DynamicObjects!
            .Where(source => source.NativeClassId == UyaClass5821Actor.NativeClassId)
            .OrderBy(source => source.InstanceIndex)
            .ToArray();

        Assert.Equal(62, sources.Length);
        Assert.All(sources, source =>
        {
            UyaClass5821AuthoredState authored =
                UyaClass5821Actor.ReadAuthored(source);
            Assert.Equal(1, authored.AuthoredLifetime);
            Assert.True(UyaClass5821Actor.HasRecoveredTable1DamageProfile(authored));
        });
    }

    [Fact]
    public void AuthoredReaderFailsClosedOnWrongClassOrPvarSize()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        Assert.Throws<ArgumentException>(() =>
            UyaClass5821Actor.ReadAuthored(
                source with { NativeClassId = 6306 }));

        var payloads = source.NativePayloads!
            .Select(payload => payload.Format == UyaMobyRuntimeSession.PVarPayloadFormat
                ? new RuntimeOpaquePayload(payload.Format, new byte[8])
                : payload)
            .ToArray();
        Assert.Throws<InvalidDataException>(() =>
            UyaClass5821Actor.ReadAuthored(
                source with { NativePayloads = payloads }));
    }

    private static UyaGameplayDamageEvent Damage(
        RuntimeDynamicObject source,
        double hp,
        uint flags,
        byte recordKind) =>
        new(
            UyaGameplayEntityRef.Player,
            UyaGameplayEntityRef.Moby(
                new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            nativeDamage: hp,
            nativeDamageFlags: flags,
            nativeRecordKind: recordKind);

    private static RuntimeDynamicObject Class5821(
        int instanceIndex,
        short authoredLifetime)
    {
        byte[] pvar = new byte[UyaClass5821Actor.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(UyaClass5821Actor.LifetimeRelativePointerOffset, sizeof(int)),
            UyaClass5821Actor.RuntimeLifetimeOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(UyaClass5821Actor.DamageConfigRelativePointerOffset, sizeof(int)),
            UyaClass5821Actor.DamageConfigOffset);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(UyaClass5821Actor.RuntimeLifetimeOffset, sizeof(float)),
            1f);
        BinaryPrimitives.WriteInt16LittleEndian(
            pvar.AsSpan(UyaClass5821Actor.AuthoredLifetimeOffset, sizeof(short)),
            authoredLifetime);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(UyaClass5821Actor.DamageConfigVerticalThresholdOffset, sizeof(float)),
            0f);
        pvar[UyaClass5821Actor.DamageConfigByte49Offset] = 0;
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(UyaClass5821Actor.DamageConfigSameClassMultiplierOffset, sizeof(float)),
            0f);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(UyaClass5821Actor.DamageConfigOptionalMultiplierOffset, sizeof(float)),
            1f);

        return new RuntimeDynamicObject(
            "rac3",
            UyaClass5821Actor.NativeClassId,
            instanceIndex,
            null,
            $"moby:{UyaClass5821Actor.NativeClassId}",
            $"table:1:moby:{instanceIndex}",
            new RuntimeObjectTransform(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 10, 0, 1,
            ]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload(
                    UyaMobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ]);
    }
}

using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class UyaClass5821ActorTests
{
    [Fact]
    public void AuthoredReaderPreservesRecoveredLifetimeAuthority()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);

        UyaClass5821AuthoredState authored =
            UyaClass5821Actor.ReadAuthored(source);

        Assert.Equal(new UyaMobyRuntimeKey(5821, 430), authored.Key);
        Assert.Equal(1, authored.AuthoredLifetime);
    }

    [Theory]
    [InlineData(0xFD, -1999d, true, true)]
    [InlineData(0xFD, 0d, true, true)]
    [InlineData(0xFD, 1d, true, false)]
    [InlineData(0x00, -1999d, false, false)]
    [InlineData(0xFE, -1999d, true, false)]
    public void TerminalProjectionStaysLimitedToObservedClass5821State(
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
    public void RetailTable1Class5821PopulationCarriesRecoveredAuthoredLifetime()
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

    private static RuntimeDynamicObject Class5821(
        int instanceIndex,
        short authoredLifetime)
    {
        byte[] pvar = new byte[UyaClass5821Actor.PVarSize];
        BinaryPrimitives.WriteInt16LittleEndian(
            pvar.AsSpan(UyaClass5821Actor.AuthoredLifetimeOffset, sizeof(short)),
            authoredLifetime);

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
                0, 0, 0, 1,
            ]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload(
                    UyaMobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ]);
    }
}

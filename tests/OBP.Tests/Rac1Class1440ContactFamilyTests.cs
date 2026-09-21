using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1Class1440ContactFamilyTests
{
    [Fact]
    public void AuthoredReaderUsesOnlyRecoveredClassFields()
    {
        var source = Class1440(instanceIndex: 41, health: 2f);

        var authored = Assert.IsType<Rac1Class1440AuthoredState>(
            Rac1Class1440ContactFamily.ReadAuthored(source));

        Assert.Equal(new Rac1Class1440Key(1440, 41), authored.Key);
        Assert.Equal(2f, authored.Health);
        Assert.Equal(Rac1Class1440ContactFamily.MinimumPVarSize, authored.PVarSize);
        Assert.Equal(0x110, Rac1Class1440ContactFamily.TargetMobyOffset);
        Assert.Null(Rac1Class1440ContactFamily.ReadAuthored(
            Dynamic(nativeClassId: 749, instanceIndex: 41)));
    }

    [Fact]
    public void AuthoredReaderRejectsMissingRecoveredPvarFields()
    {
        var source = Dynamic(
            Rac1Class1440ContactFamily.NativeClassId,
            instanceIndex: 4,
            new RuntimeOpaquePayload(
                Rac1Class1440ContactFamily.PVarPayloadFormat,
                new byte[Rac1Class1440ContactFamily.MinimumPVarSize - 1]));

        Assert.Throws<InvalidDataException>(() =>
            Rac1Class1440ContactFamily.ReadAuthored(source));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void RegistrationPreservesCallerSuppliedRecoveredNativeState(int nativeState)
    {
        var source = Class1440(instanceIndex: 6, health: 2f);
        var session = new Rac1Class1440ContactFamilySession();

        var probe = session.RegisterRecovered(
            source,
            RuntimeEntityState.FromAuthored(source),
            nativeState);

        Assert.Equal(nativeState, probe.NativeState);
        Assert.Equal(2f, probe.Health);
        Assert.Equal(RuntimeEntityPresence.Active, probe.EntityState.Presentation.Presence);
        Assert.Empty(probe.HostIntents);
        Assert.Empty(probe.HostEvents);
    }

    [Fact]
    public void StateFiveExposesRecoveredQueryDescriptorAndPlayerGate()
    {
        var source = Class1440(instanceIndex: 7, health: 2f);
        var session = Registered(source, Rac1Class1440ContactFamily.ContactQueryNativeState);

        var first = session.BeginContactQuery(source, querySite: 0);
        var firstIntent = Assert.IsType<Rac1Class1440ContactQueryIntent>(
            Assert.Single(first.HostIntents));
        Assert.Equal(new Rac1MobyRuntimeKey(1440, 7), firstIntent.Source);
        Assert.Equal(0, firstIntent.QuerySite);
        Assert.Equal(0x00010000u, firstIntent.NativeWord84);
        Assert.Equal(1440, firstIntent.NativeClassId);
        Assert.Equal(1f, firstIntent.NativeScalar8c);
        Assert.Equal(1, firstIntent.NativeInteger90);
        Assert.Empty(first.HostEvents);

        var missed = session.ResolveContactQuery(source, querySite: 0, queryHitPlayer: false);
        Assert.Equal(Rac1Class1440ContactFamily.ContactQueryNativeState, missed.NativeState);
        Assert.Empty(missed.HostEvents);

        var second = session.BeginContactQuery(source, querySite: 1);
        Assert.Equal(1, Assert.IsType<Rac1Class1440ContactQueryIntent>(
            Assert.Single(second.HostIntents)).QuerySite);

        var hit = session.ResolveContactQuery(source, querySite: 1, queryHitPlayer: true);
        Assert.Equal(Rac1Class1440ContactFamily.ContactTransitionNativeState, hit.NativeState);
        Assert.Collection(
            hit.HostEvents,
            item =>
            {
                var contact = Assert.IsType<Rac1Class1440PlayerContactEvent>(item);
                Assert.Equal(new Rac1MobyRuntimeKey(1440, 7), contact.Key);
                Assert.Equal(1, contact.QuerySite);
            },
            item =>
            {
                var state = Assert.IsType<Rac1MobyNativeStateChangedEvent>(item);
                Assert.Equal(5, state.NativeStateBefore);
                Assert.Equal(6, state.NativeStateAfter);
            });

        Assert.Empty(session.Probe(source).HostEvents);
        Assert.Throws<InvalidOperationException>(() =>
            session.BeginContactQuery(source, querySite: 0));
    }

    [Fact]
    public void ConsumedDamageSubtractsClassHealthWithoutInventingDeathState()
    {
        var source = Class1440(instanceIndex: 8, health: 2f);
        var session = Registered(source, initialNativeState: 3);
        var damage = new Rac1NativeDamageEnvelope(1d, 0x00010000u);

        var first = session.ApplyConsumedNativeDamage(source, damage);

        Assert.Equal(1f, first.Health);
        Assert.Equal(3, first.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, first.EntityState.Presentation.Presence);
        var consumed = Assert.IsType<Rac1MobyDamageConsumedEvent>(
            Assert.Single(first.HostEvents));
        Assert.Equal(new Rac1MobyRuntimeKey(1440, 8), consumed.Victim);
        Assert.Equal(damage, consumed.Damage);

        var zero = session.ApplyConsumedNativeDamage(source, damage);
        Assert.Equal(0f, zero.Health);
        Assert.Equal(3, zero.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, zero.EntityState.Presentation.Presence);
        Assert.IsType<Rac1MobyDamageConsumedEvent>(Assert.Single(zero.HostEvents));

        Assert.Throws<NotSupportedException>(() =>
            session.ApplyConsumedNativeDamage(
                source,
                new Rac1NativeDamageEnvelope(0d, 0x00010000u)));
    }

    [Fact]
    public void RegistrationRejectsUnwitnessedHealthAndOutOfDispatchState()
    {
        var source = Class1440(instanceIndex: 9, health: 1f);
        var session = new Rac1Class1440ContactFamilySession();

        Assert.Throws<NotSupportedException>(() =>
            session.RegisterRecovered(
                source,
                RuntimeEntityState.FromAuthored(source),
                initialNativeState: 1));

        var witnessed = Class1440(instanceIndex: 10, health: 2f);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.RegisterRecovered(
                witnessed,
                RuntimeEntityState.FromAuthored(witnessed),
                initialNativeState: 9));
    }

    private static Rac1Class1440ContactFamilySession Registered(
        RuntimeDynamicObject source,
        int initialNativeState)
    {
        var session = new Rac1Class1440ContactFamilySession();
        session.RegisterRecovered(
            source,
            RuntimeEntityState.FromAuthored(source),
            initialNativeState);
        return session;
    }

    private static RuntimeDynamicObject Class1440(int instanceIndex, float health)
    {
        var pvar = new byte[Rac1Class1440ContactFamily.MinimumPVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class1440ContactFamily.HealthOffset, sizeof(int)),
            BitConverter.SingleToInt32Bits(health));
        return Dynamic(
            Rac1Class1440ContactFamily.NativeClassId,
            instanceIndex,
            new RuntimeOpaquePayload(Rac1Class1440ContactFamily.PVarPayloadFormat, pvar));
    }

    private static RuntimeDynamicObject Dynamic(
        int nativeClassId,
        int instanceIndex,
        params RuntimeOpaquePayload[] payloads) =>
        new(
            "rac1",
            nativeClassId,
            instanceIndex,
            null,
            $"moby:{nativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            payloads);
}

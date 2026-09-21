using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1MobyRuntimeTests
{
    [Fact]
    public void CommonRuntimeTracksNativeStateWithoutInventingClassPolicy()
    {
        var source = Dynamic(nativeClassId: 766, instanceIndex: 3);
        var authored = RuntimeEntityState.FromAuthored(source);

        var state = Rac1MobyRuntime.Create(source, nativeState: 5, authored);
        var changed = Rac1MobyRuntime.WithNativeState(state, nativeState: 6);

        Assert.Equal(new Rac1MobyRuntimeKey(766, 3), state.Key);
        Assert.Equal(5, state.NativeState);
        Assert.Equal(6, changed.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, changed.EntityState.Presentation.Presence);
        Assert.Equal(authored.Identity, changed.EntityState.Identity);
    }

    [Theory]
    [InlineData(Rac1MobyRuntime.TerminalNativeStateFd)]
    [InlineData(Rac1MobyRuntime.TerminalNativeStateFe)]
    public void TerminalizationUsesRecoveredCommonStates(int terminalState)
    {
        var source = Dynamic(nativeClassId: 500, instanceIndex: 9);
        var state = Rac1MobyRuntime.Create(
            source,
            nativeState: 12,
            RuntimeEntityState.FromAuthored(source));

        var terminal = Rac1MobyRuntime.Terminalize(state, terminalState);

        Assert.Equal(terminalState, terminal.NativeState);
        Assert.Equal(RuntimeEntityPresence.Inactive, terminal.EntityState.Presentation.Presence);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1MobyRuntime.Terminalize(state, nativeState: 12));
    }

    [Fact]
    public void NativeEnvelopeKeepsRecoveredTransportValues()
    {
        var envelope = new Rac1NativeDamageEnvelope(1d, 0x00010000u);

        Assert.Equal(1d, envelope.NativeDamage);
        Assert.Equal(0x00010000u, envelope.NativeDamageFlags);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1NativeDamageEnvelope(double.NaN, 0u));
    }

    [Fact]
    public void Class749ExposesNavigationIntentWithoutEmbeddingHostMotion()
    {
        var targetDestination = new Rac1Class749WorldPoint(12, 5, -1);
        var home = new Rac1Class749WorldPoint(10, 4, -3);
        var source = Class749(
            149,
            health: 1f,
            targetDestination: targetDestination,
            statusSentinel: 0,
            home: home);
        var session = new Rac1Class749HostileSession();
        session.RegisterRepresentative(source, RuntimeEntityState.FromAuthored(source));

        var enteredTargeted = session.Step(source, Facts(distance: 3, facingError: 0));
        var transition = Assert.Single(enteredTargeted.HostEvents);
        var stateChange = Assert.IsType<Rac1MobyNativeStateChangedEvent>(transition);
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, stateChange.NativeStateBefore);
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, stateChange.NativeStateAfter);
        Assert.Empty(enteredTargeted.HostIntents);

        var pursue = session.Step(source, Facts(distance: 3, facingError: 0));
        var pursueIntent = Assert.IsType<Rac1Class749NavigationIntent>(
            Assert.Single(pursue.HostIntents));
        Assert.Equal(Rac1Class749NavigationIntentKind.PursueRecoveredTarget, pursueIntent.Kind);
        Assert.Equal(targetDestination, pursueIntent.Destination);
        Assert.Empty(pursue.HostEvents);
        var enteredReturn = session.Step(
            source,
            Facts(
                distance: 3,
                facingError: 0,
                statusSentinel: 2,
                currentPosition: new Rac1Class749WorldPoint(20, 4, -3)));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, enteredReturn.NativeState);
        Assert.Equal(
            Rac1Class749NavigationIntentKind.PursueRecoveredTarget,
            Assert.IsType<Rac1Class749NavigationIntent>(
                Assert.Single(enteredReturn.HostIntents)).Kind);

        var returning = session.Step(
            source,
            Facts(
                distance: 3,
                facingError: 0,
                statusSentinel: 2,
                currentPosition: new Rac1Class749WorldPoint(20, 4, -3)));
        var returnIntent = Assert.IsType<Rac1Class749NavigationIntent>(
            Assert.Single(returning.HostIntents));
        Assert.Equal(Rac1Class749NavigationIntentKind.ReturnHome, returnIntent.Kind);
        Assert.Equal(home, returnIntent.Destination);
        Assert.Empty(returning.HostEvents);
    }

    [Fact]
    public void Class749DamageAndTerminalizationEmitCommonRuntimeEvents()
    {
        var source = Class749(149, health: 1f);
        var session = new Rac1Class749HostileSession();
        session.RegisterRepresentative(source, RuntimeEntityState.FromAuthored(source));
        var result = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.ForwardDirectRecord,
            Rac1WrenchCombatController.NativeDamage,
            Rac1WrenchCombatController.NativeDamageFlags);

        var damaged = session.ApplyWrenchDamage(source, result);

        Assert.Equal(Rac1Class749Hostile.DamageNativeState, damaged.RuntimeState.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, damaged.EntityState.Presentation.Presence);
        Assert.Collection(
            damaged.HostEvents,
            item =>
            {
                var consumed = Assert.IsType<Rac1MobyDamageConsumedEvent>(item);
                Assert.Equal(new Rac1MobyRuntimeKey(749, 149), consumed.Victim);
                Assert.Equal(result.DamageEnvelope, consumed.Damage);
            },
            item =>
            {
                var state = Assert.IsType<Rac1MobyNativeStateChangedEvent>(item);
                Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, state.NativeStateBefore);
                Assert.Equal(Rac1Class749Hostile.DamageNativeState, state.NativeStateAfter);
            });

        var terminal = session.ApplyTerminalStatus(
            source,
            Rac1Class749Hostile.TerminalNativeStateFd);

        Assert.Equal(RuntimeEntityPresence.Inactive, terminal.EntityState.Presentation.Presence);
        Assert.Collection(
            terminal.HostEvents,
            item =>
            {
                var state = Assert.IsType<Rac1MobyNativeStateChangedEvent>(item);
                Assert.Equal(Rac1Class749Hostile.DamageNativeState, state.NativeStateBefore);
                Assert.Equal(Rac1Class749Hostile.TerminalNativeStateFd, state.NativeStateAfter);
            },
            item =>
            {
                var ended = Assert.IsType<Rac1MobyTerminalizedEvent>(item);
                Assert.Equal(Rac1Class749Hostile.TerminalNativeStateFd, ended.NativeTerminalState);
            });
        Assert.Empty(session.Probe(source).HostEvents);
    }

    [Fact]
    public void Class749AttackEventIsTransientHostEvent()
    {
        var source = Class749(149, health: 1f);
        var session = new Rac1Class749HostileSession();
        session.RegisterRepresentative(source, RuntimeEntityState.FromAuthored(source));
        session.Step(source, Facts(distance: 3, facingError: 0));
        session.Step(source, Facts(distance: 1, facingError: 0));

        Rac1Class749HostProbe? marker = null;
        for (int tick = 1; tick <= Rac1Class749Hostile.AttackMarkerNativeUpdate; tick++)
            marker = session.Step(source, Facts(distance: 1, facingError: 0));

        Assert.NotNull(marker);
        Assert.NotNull(marker!.Attack);
        Assert.Same(marker.Attack, Assert.Single(marker.HostEvents));
        Assert.Empty(session.Probe(source).HostEvents);
    }

    private static Rac1Class749TargetFacts Facts(
        double distance,
        double facingError,
        int? statusSentinel = 0,
        Rac1Class749WorldPoint currentPosition = default) =>
        new(distance, facingError, currentPosition, statusSentinel);

    private static RuntimeDynamicObject Class749(
        int instanceIndex,
        float health,
        Rac1Class749WorldPoint targetDestination = default,
        int statusSentinel = 0,
        Rac1Class749WorldPoint home = default)
    {
        var pvar = new byte[Rac1Class749Hostile.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class749Hostile.HealthOffset, sizeof(int)),
            BitConverter.SingleToInt32Bits(health));
        WriteSingle(
            pvar,
            Rac1Class749Hostile.TargetDestinationOffset,
            checked((float)targetDestination.X));
        WriteSingle(
            pvar,
            Rac1Class749Hostile.TargetDestinationOffset + sizeof(float),
            checked((float)targetDestination.Z));
        WriteSingle(
            pvar,
            Rac1Class749Hostile.TargetDestinationOffset + 2 * sizeof(float),
            checked((float)targetDestination.Y));
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class749Hostile.StatusSentinelOffset, sizeof(int)),
            statusSentinel);
        WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset, checked((float)home.X));
        WriteSingle(
            pvar,
            Rac1Class749Hostile.HomePositionOffset + sizeof(float),
            checked((float)home.Z));
        WriteSingle(
            pvar,
            Rac1Class749Hostile.HomePositionOffset + 2 * sizeof(float),
            checked((float)home.Y));

        return Dynamic(
            Rac1Class749Hostile.NativeClassId,
            instanceIndex,
            new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar));
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

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(int)),
            BitConverter.SingleToInt32Bits(value));
}

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
        session.Register(source, RuntimeEntityState.FromAuthored(source));

        var enteredTargeted = session.Step(source, Facts(distance: 3, facingError: 0));
        var transition = Assert.Single(enteredTargeted.HostEvents);
        var stateChange = Assert.IsType<Rac1MobyNativeStateChangedEvent>(transition);
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, stateChange.NativeStateBefore);
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, stateChange.NativeStateAfter);
        var idleTurn = Assert.IsType<Rac1Class749IdleTurnIntent>(
            Assert.Single(enteredTargeted.HostIntents));
        Assert.Equal(Rac1Class749Hostile.IdleTurnDescriptorOffset, idleTurn.DescriptorOffset);
        Assert.Equal(Rac1Class749Hostile.IdleTurnInput, idleTurn.InputA);
        Assert.Equal(Rac1Class749Hostile.IdleTurnInput, idleTurn.InputB);

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
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        var result = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);

        var damageEvent = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(source.NativeClassId, source.InstanceIndex),
            result);
        var damaged = session.ApplyDamage(source, damageEvent);

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
        session.Register(source, RuntimeEntityState.FromAuthored(source));
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

    [Fact]
    public void RuntimeSessionOwnsStableIdentityPVarPresenceAndTerminalization()
    {
        var source = Dynamic(nativeClassId: 766, instanceIndex: 4, nativeUid: 91);
        var pvar = new byte[] { 1, 2, 3, 4 };
        var runtime = new Rac1MobyRuntimeSession();

        var instance = runtime.Register(
            source,
            RuntimeEntityState.FromAuthored(source),
            nativeState: 5,
            pvar);

        pvar[0] = 99;
        Assert.Equal(new Rac1MobyRuntimeKey(766, 4), instance.Key);
        Assert.Equal(91, instance.NativeUid);
        Assert.Equal(source.Transform, instance.AuthoredTransform);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, instance.PVar.ToArray());
        Assert.Same(instance, runtime.Require(source));

        runtime.SetPresence(instance, RuntimeEntityPresence.Inactive);
        Assert.False(instance.IsActive);
        Assert.Equal(5, instance.State.NativeState);

        runtime.SetPresence(instance, RuntimeEntityPresence.Active);
        runtime.Terminalize(instance, Rac1MobyRuntime.TerminalNativeStateFd);
        Assert.Equal(Rac1MobyRuntime.TerminalNativeStateFd, instance.State.NativeState);
        Assert.Equal(RuntimeEntityPresence.Inactive, instance.Presence);
    }

    [Fact]
    public void RuntimeSessionTracksLiveTransformWithoutMutatingAuthoredIdentity()
    {
        var source = Dynamic(nativeClassId: 766, instanceIndex: 4, nativeUid: 91);
        var runtime = new Rac1MobyRuntimeSession();
        var instance = runtime.Register(
            source,
            RuntimeEntityState.FromAuthored(source),
            nativeState: 5);
        var liveTransform = new RuntimeObjectTransform(
        [
            1d, 0d, 0d, 0d,
            0d, 1d, 0d, 0d,
            0d, 0d, 1d, 0d,
            10d, 20d, 30d, 1d,
        ]);

        var state = runtime.SetTransform(instance, liveTransform);

        Assert.Equal(liveTransform, state.Presentation.Transform);
        Assert.Equal(liveTransform, instance.EntityState.Presentation.Transform);
        Assert.Equal(5, instance.State.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, instance.Presence);
        Assert.NotEqual(liveTransform, source.Transform);
        Assert.Equal(RuntimeEntityIdentity.From(source), instance.Identity);
        Assert.Throws<ArgumentException>(() =>
            runtime.SetTransform(
                instance,
                new RuntimeObjectTransform(new double[15])));
    }

    [Fact]
    public void SameClassInstancesDispatchWithoutPrivilegedWitness()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var hostiles = new Rac1Class749HostileSession(runtime);
        var first = Class749(149, health: 1f);
        var second = Class749(150, health: 1f);
        hostiles.Register(first, RuntimeEntityState.FromAuthored(first));
        hostiles.Register(second, RuntimeEntityState.FromAuthored(second));

        var firstUpdate = runtime.DispatchUpdate<Rac1Class749HostProbe>(
            first, Facts(distance: 3, facingError: 0));
        var secondUpdate = runtime.DispatchUpdate<Rac1Class749HostProbe>(
            second, Facts(distance: 3, facingError: 0));

        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, firstUpdate.NativeState);
        Assert.Equal(firstUpdate.NativeState, secondUpdate.NativeState);
        Assert.Equal(2, runtime.RegisteredCount);
        Assert.Equal(2, hostiles.RegisteredCount);
        Assert.NotEqual(firstUpdate.RuntimeState.Key, secondUpdate.RuntimeState.Key);
    }

    [Fact]
    public void SharedRuntimeDispatchesRecoveredDamageConsumerByNativeClass()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var hostiles = new Rac1Class749HostileSession(runtime);
        var source = Class749(149, health: 1f);
        hostiles.Register(source, RuntimeEntityState.FromAuthored(source));
        var target = new Rac1MobyRuntimeKey(
            source.NativeClassId,
            source.InstanceIndex);
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);
        var damage = Rac1DamageRuntime.FromWrench(target, wrench);

        var result = runtime.DispatchDamage<Rac1Class749HostProbe>(
            source,
            damage);

        Assert.Equal(0f, result.Health);
        Assert.Equal(
            Rac1Class749Hostile.DamageNativeState,
            result.NativeState);
    }

    [Fact]
    public void SharedRuntimeRoutesDamageDirectlyFromStableEventTargetIdentity()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var hostiles = new Rac1Class749HostileSession(runtime);
        var source = Class749(149, health: 1f);
        hostiles.Register(source, RuntimeEntityState.FromAuthored(source));
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);
        var damage = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(source.NativeClassId, source.InstanceIndex),
            wrench);

        Assert.True(runtime.TryResolveDamageTarget(damage, out var resolved));
        Assert.NotNull(resolved);
        Assert.Same(source, resolved!.Source);
        Assert.True(runtime.CanDispatchDamage(damage));

        var result = runtime.DispatchDamage<Rac1Class749HostProbe>(damage);

        Assert.Equal(0f, result.Health);
        Assert.Equal(Rac1Class749Hostile.DamageNativeState, result.NativeState);
    }

    [Fact]
    public void EventOnlyDamageRoutingFailsClosedWhenTargetIsNotRegisteredMoby()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);
        var unregistered = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(Rac1Class749Hostile.NativeClassId, 149),
            wrench);

        Assert.False(runtime.TryResolveDamageTarget(unregistered, out var missing));
        Assert.Null(missing);
        Assert.False(runtime.CanDispatchDamage(unregistered));
        Assert.False(runtime.TryDispatchDamage<Rac1Class749HostProbe>(
            unregistered,
            out var absent));
        Assert.Null(absent);
        Assert.Throws<InvalidOperationException>(() =>
            runtime.DispatchDamage<Rac1Class749HostProbe>(unregistered));

        var playerTarget = new Rac1GameplayDamageEvent(
            Rac1GameplayEntityRef.Player,
            Rac1GameplayEntityRef.Player,
            1d,
            0x00010000u);
        Assert.False(runtime.TryResolveDamageTarget(playerTarget, out var notMoby));
        Assert.Null(notMoby);
        Assert.False(runtime.CanDispatchDamage(playerTarget));
    }

    [Fact]
    public void SharedRuntimeDamageAdmissionSeparatesTransportFromRecoveredConsequence()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var hostiles = new Rac1Class749HostileSession(runtime);
        var source = Class749(149, health: 1f);
        hostiles.Register(source, RuntimeEntityState.FromAuthored(source));
        var target = new Rac1MobyRuntimeKey(
            source.NativeClassId,
            source.InstanceIndex);

        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);
        var wrenchEvent = Rac1DamageRuntime.FromWrench(target, wrench);

        Assert.True(runtime.CanDispatchDamage(source, wrenchEvent));
        Assert.True(runtime.TryDispatchDamage<Rac1Class749HostProbe>(
            source,
            wrenchEvent,
            out var wrenchResult));
        Assert.NotNull(wrenchResult);
        Assert.Equal(0f, wrenchResult!.Health);

        var freshRuntime = new Rac1MobyRuntimeSession();
        var freshHostiles = new Rac1Class749HostileSession(freshRuntime);
        var freshSource = Class749(150, health: 1f);
        freshHostiles.Register(
            freshSource,
            RuntimeEntityState.FromAuthored(freshSource));
        var bombEvent = Rac1DamageRuntime.FromBombGlove(
            new Rac1BombGloveDamageResult(
                ProjectileId: 1,
                TargetNativeClassId: freshSource.NativeClassId,
                TargetInstanceIndex: freshSource.InstanceIndex,
                NativeDamage: Rac1BombGlove.NativeDamage,
                NativeDamageFlags: Rac1BombGlove.NativeDamageFlags));

        Assert.False(freshRuntime.CanDispatchDamage(freshSource, bombEvent));
        Assert.False(freshRuntime.TryDispatchDamage<Rac1Class749HostProbe>(
            freshSource,
            bombEvent,
            out var bombResult));
        Assert.Null(bombResult);
        Assert.Equal(1f, freshHostiles.Probe(freshSource).Health);
        Assert.Throws<NotSupportedException>(() =>
            freshRuntime.DispatchDamage<Rac1Class749HostProbe>(
                freshSource,
                bombEvent));
        Assert.Equal(1f, freshHostiles.Probe(freshSource).Health);
    }

    [Fact]
    public void SharedRuntimeDamageDispatchFailsClosedForWrongTargetOrUnknownConsumer()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var hostiles = new Rac1Class749HostileSession(runtime);
        var hostile = Class749(149, health: 1f);
        hostiles.Register(hostile, RuntimeEntityState.FromAuthored(hostile));
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);

        var wrongTarget = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(hostile.NativeClassId, 150),
            wrench);
        Assert.Throws<ArgumentException>(() =>
            runtime.DispatchDamage<Rac1Class749HostProbe>(
                hostile,
                wrongTarget));

        var unknown = Dynamic(nativeClassId: 766, instanceIndex: 4);
        runtime.Register(
            unknown,
            RuntimeEntityState.FromAuthored(unknown),
            nativeState: 5);
        var unknownDamage = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(unknown.NativeClassId, unknown.InstanceIndex),
            wrench);
        Assert.Throws<NotSupportedException>(() =>
            runtime.DispatchDamage<object>(unknown, unknownDamage));
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

        var transform = new double[16];
        transform[12] = home.X;
        transform[13] = home.Y;
        transform[14] = home.Z;
        transform[15] = 1d;
        return new RuntimeDynamicObject(
            "rac1",
            Rac1Class749Hostile.NativeClassId,
            instanceIndex,
            null,
            $"moby:{Rac1Class749Hostile.NativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(transform),
            Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar)]);
    }
    private static RuntimeDynamicObject Dynamic(
        int nativeClassId,
        int instanceIndex,
        int? nativeUid = null,
        params RuntimeOpaquePayload[] payloads) =>
        new(
            "rac1",
            nativeClassId,
            instanceIndex,
            nativeUid,
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

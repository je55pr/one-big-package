using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1Class749HostileTests
{
    [Fact]
    public void ReadsOnlyExactRac1PvarAuthority()
    {
        var source = Class749(149, health: 1f);

        var authored = Assert.IsType<Rac1Class749AuthoredState>(
            Rac1Class749Hostile.ReadAuthored(source));

        Assert.Equal(new Rac1Class749Key(749, 149), authored.Key);
        Assert.Equal(1f, authored.Health);
        Assert.Equal(0x280, authored.PVarSize);
        Assert.Null(source.NativeUid);
    }

    [Fact]
    public void RejectsWrongPvarSize()
    {
        var source = Class749(149, health: 1f, pvarSize: 0x27c);
        var error = Assert.Throws<InvalidDataException>(() =>
            Rac1Class749Hostile.ReadAuthored(source));
        Assert.Contains("expected 0x280", error.Message);
    }

    [Fact]
    public void TargetAcquisitionMovesRepresentativeStateFiveToSixOnly()
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);

        var before = session.Probe(source);
        var after = session.Step(
            source,
            new Rac1Class749TargetFacts(true, 1.5, 0.1),
            nativeAnimationMarker: 34);

        Assert.Equal(5, before.NativeState);
        Assert.Equal(6, after.NativeState);
        Assert.Null(after.NativeSequence);
        Assert.Null(after.Attack);
        Assert.Equal(RuntimeEntityPresence.Active, after.EntityState.Presentation.Presence);
    }

    [Theory]
    [InlineData(2.0, 0.0)]
    [InlineData(2.1, 0.0)]
    public void AttackSelectionRejectsDistanceBoundary(double distance, double facingError)
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);
        _ = session.Step(source, new Rac1Class749TargetFacts(true, 0, 0), 0);

        var probe = session.Step(
            source,
            new Rac1Class749TargetFacts(true, distance, facingError),
            nativeAnimationMarker: 0);

        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, probe.NativeState);
        Assert.Null(probe.NativeSequence);
        Assert.Null(probe.Attack);
    }

    [Fact]
    public void AttackSelectionRejectsFacingBoundaryRawFloatExactly()
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);
        _ = session.Step(source, new Rac1Class749TargetFacts(true, 0, 0), 0);

        var probe = session.Step(source, new Rac1Class749TargetFacts(
            true, 1.5, Rac1Class749Hostile.AttackFacingErrorExclusive), 0);

        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, probe.NativeState);
        Assert.Null(probe.NativeSequence);
    }

    [Fact]
    public void StateSixSelectsStateSevenSequenceFiveOnlyInsideStrictThresholds()
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);
        _ = session.Step(source, new Rac1Class749TargetFacts(true, 0, 0), 0);

        var probe = session.Step(source, new Rac1Class749TargetFacts(
            true,
            Math.BitDecrement(Rac1Class749Hostile.AttackDistanceExclusive),
            Math.BitDecrement((double)Rac1Class749Hostile.AttackFacingErrorExclusive)), 0);

        Assert.Equal(Rac1Class749Hostile.AttackNativeState, probe.NativeState);
        Assert.Equal(Rac1Class749Hostile.AttackSequenceId, probe.NativeSequence);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, probe.EntityState.Presentation.AnimationRole);
        Assert.Null(probe.Attack);
    }

    [Fact]
    public void StateSevenEmitsAttackOnlyAtMarkerThirtyFourAndOnlyOnce()
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);
        _ = session.Step(source, new Rac1Class749TargetFacts(true, 0, 0), 0);
        _ = session.Step(source, new Rac1Class749TargetFacts(true, 1, 0), 0);

        Assert.Null(session.Step(source, new Rac1Class749TargetFacts(true, 1, 0), 33.999).Attack);
        var atMarker = session.Step(source, new Rac1Class749TargetFacts(true, 1, 0), 34);
        Assert.Equal(new Rac1Class749AttackEvent(34, 1), atMarker.Attack);
        Assert.Null(session.Step(source, new Rac1Class749TargetFacts(true, 1, 0), 34).Attack);
    }

    [Theory]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFd)]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFe)]
    public void OrdinaryForwardWrenchDamageReproducesRepresentativeDeathConsequence(int terminalStatus)
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);
        var wrench = new Rac1WrenchCombatController();
        var target = new Rac1WrenchContactTarget(
            Rac1Class749Hostile.NativeClassId,
            Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: false);
        var damage = Assert.IsType<Rac1WrenchDamageResult>(
            wrench.ResolveForwardDirectRecord(
                Rac1WrenchCombatController.OrdinaryActionId,
                Rac1WrenchCombatController.OrdinaryProfileId,
                nativeAge: 20,
                target));

        var damaged = session.ApplyWrenchDamage(source, damage);
        Assert.Equal(0f, damaged.Health);
        Assert.Equal(Rac1Class749Hostile.DamageNativeState, damaged.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, damaged.EntityState.Presentation.Presence);

        var terminal = session.ApplyTerminalStatus(source, terminalStatus);
        Assert.Equal(terminalStatus, terminal.NativeState);
        Assert.Equal(RuntimeEntityPresence.Inactive, terminal.EntityState.Presentation.Presence);
    }

    [Fact]
    public void WrenchDamageDoesNotGeneralizeUnprovenHealthValues()
    {
        var source = Class749(149, health: 2f);
        var session = Registered(source);
        var damage = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.ForwardDirectRecord,
            Rac1WrenchCombatController.NativeDamage,
            Rac1WrenchCombatController.NativeDamageFlags);

        Assert.Throws<NotSupportedException>(() => session.ApplyWrenchDamage(source, damage));
        Assert.Equal(2f, session.Probe(source).Health);
    }

    private static Rac1Class749HostileSession Registered(RuntimeDynamicObject source)
    {
        var session = new Rac1Class749HostileSession();
        session.RegisterRepresentative(source, RuntimeEntityState.FromAuthored(source));
        return session;
    }

    private static RuntimeDynamicObject Class749(int instanceIndex, float health, int pvarSize = 0x280)
    {
        var pvar = new byte[pvarSize];
        if (pvarSize >= Rac1Class749Hostile.HealthOffset + sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.HealthOffset, sizeof(int)),
                BitConverter.SingleToInt32Bits(health));
        }

        return new RuntimeDynamicObject(
            "rac1", Rac1Class749Hostile.NativeClassId, instanceIndex, null,
            $"moby:{Rac1Class749Hostile.NativeClassId}", $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar)]);
    }
}

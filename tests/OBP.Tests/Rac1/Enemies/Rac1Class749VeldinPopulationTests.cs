using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using static OBP.Tests.Rac1Class749Fixtures;

namespace OBP.Tests;

/// <summary>
/// Class-749 Veldin population: authored activation groups, polygon admission, the target descriptor and the linked
/// instance-154 state. The hostile's own state machine and damage consumption are in
/// <see cref="Rac1Class749HostileTests"/>.
/// </summary>
public sealed class Rac1Class749VeldinPopulationTests
{
    [Fact]
    public void VeldinPopulationPinsAllAuthoredActivationGroupsWithoutInstancePrivilege()
    {
        int[] expected = [0, 0, 1, 1, 1, 2, 3, 3, 4, 16, 16, 20, 23, 23, 23, 23];
        for (int i = 0; i < expected.Length; i++)
        {
            int instanceIndex = Rac1Class749VeldinPopulation.FirstInstanceIndex + i;
            Assert.Equal(expected[i], Rac1Class749VeldinPopulation.GetActivationGroup(instanceIndex));
            Assert.True(Rac1Class749Hostile.IsRecoveredVeldinPlacement(
                Rac1Class749VeldinPopulation.LevelId,
                Class749(instanceIndex, health: 1f)));
        }

        Assert.False(Rac1Class749Hostile.IsRecoveredVeldinPlacement(
            18,
            Class749(149, health: 1f)));
    }

    [Fact]
    public void AuthoredVeldinStartDoesNotPromoteAnyPlacementToPursuit()
    {
        var session = new Rac1Class749HostileSession();
        for (int instanceIndex = 143; instanceIndex <= 158; instanceIndex++)
        {
            var source = VeldinClass749(instanceIndex);
            session.RegisterVeldinPlacement(source, RuntimeEntityState.FromAuthored(source));
            var probe = session.Step(
                source,
                new Rac1Class749TargetFacts(
                    10d,
                    0d,
                    TargetDescriptor: VeldinTarget(
                        instanceIndex,
                        Rac1Class749VeldinPopulation.AuthoredRatchetStart)));

            Assert.NotEqual(Rac1Class749Hostile.TargetedNativeState, probe.NativeState);
        }
    }

    [Fact]
    public void PolygonAdmissionPromotesOnlyMatchingAuthoredGroups()
    {
        var session = new Rac1Class749HostileSession();
        var group0 = VeldinClass749(143);
        var group3 = VeldinClass749(149);
        session.RegisterVeldinPlacement(group0, RuntimeEntityState.FromAuthored(group0));
        session.RegisterVeldinPlacement(group3, RuntimeEntityState.FromAuthored(group3));

        var group0Probe = session.Step(
            group0,
            new Rac1Class749TargetFacts(
                10d,
                0d,
                TargetDescriptor: PlayerTarget(new(150d, 0d, 120d))));
        var group3Suppressed = session.Step(
            group3,
            new Rac1Class749TargetFacts(
                10d,
                0d,
                TargetDescriptor: PlayerTarget(new(150d, 0d, 120d))));
        var group3Admitted = session.Step(
            group3,
            new Rac1Class749TargetFacts(
                10d,
                0d,
                TargetDescriptor: PlayerTarget(new(104d, 0d, 190d))));

        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, group0Probe.NativeState);
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, group3Suppressed.NativeState);
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, group3Admitted.NativeState);
    }

    [Fact]
    public void VeldinTargetDescriptorRejectsAnonymousOrNonPlayerTarget()
    {
        var position = new Rac1Class749WorldPoint(150d, 0d, 120d);

        var anonymousSource = VeldinClass749(143);
        var anonymous = new Rac1Class749HostileSession();
        anonymous.RegisterVeldinPlacement(
            anonymousSource,
            RuntimeEntityState.FromAuthored(anonymousSource));
        Assert.Throws<NotSupportedException>(() =>
            anonymous.Step(
                anonymousSource,
                new Rac1Class749TargetFacts(
                    10d,
                    0d,
                    TargetDescriptor: null)));
        Assert.Equal(
            Rac1Class749Hostile.TargetSearchNativeState,
            anonymous.Probe(anonymousSource).NativeState);

        var wrongSource = VeldinClass749(143);
        var wrong = new Rac1Class749HostileSession();
        wrong.RegisterVeldinPlacement(
            wrongSource,
            RuntimeEntityState.FromAuthored(wrongSource));
        Assert.Throws<NotSupportedException>(() =>
            wrong.Step(
                wrongSource,
                new Rac1Class749TargetFacts(
                    10d,
                    0d,
                    TargetDescriptor: new Rac1Class749TargetDescriptor(
                        Rac1GameplayEntityRef.Moby(new Rac1MobyRuntimeKey(900, 1)),
                        position))));
        Assert.Equal(
            Rac1Class749Hostile.TargetSearchNativeState,
            wrong.Probe(wrongSource).NativeState);

        var admittedSource = VeldinClass749(143);
        var admitted = new Rac1Class749HostileSession();
        admitted.RegisterVeldinPlacement(
            admittedSource,
            RuntimeEntityState.FromAuthored(admittedSource));
        var probe = admitted.Step(
            admittedSource,
            new Rac1Class749TargetFacts(
                10d,
                0d,
                TargetDescriptor: PlayerTarget(position)));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, probe.NativeState);
    }

    [Fact]
    public void LinkedVeldinStateRequiresAuthoredLinkedMobyIdentityWhileLive()
    {
        var source = VeldinClass749(154);
        var session = new Rac1Class749HostileSession();
        session.RegisterVeldinPlacement(
            source,
            RuntimeEntityState.FromAuthored(source));

        Assert.Throws<NotSupportedException>(() =>
            session.Step(
                source,
                new Rac1Class749TargetFacts(
                    5d,
                    0d,
                    TargetDescriptor: PlayerTarget(new(96d, 33d, 258d)))));

        Assert.Throws<NotSupportedException>(() =>
            session.Step(
                source,
                new Rac1Class749TargetFacts(
                    5d,
                    0d,
                    TargetDescriptor: new Rac1Class749TargetDescriptor(
                        Rac1GameplayEntityRef.Moby(new Rac1MobyRuntimeKey(900, 196)),
                        new(96d, 33d, 258d)))));

        var tracked = session.Step(
            source,
            new Rac1Class749TargetFacts(
                5d,
                0d,
                TargetDescriptor: LinkedTarget(new(96d, 33d, 258d))));
        Assert.Equal(Rac1Class749Hostile.LinkedObjectNativeState, tracked.NativeState);
    }

    [Fact]
    public void Instance154UsesLinkedConstructorThenReturnsHomeAfterTerminal()
    {
        var home = new Rac1Class749WorldPoint(96d, 33d, 258d);
        var source = VeldinClass749(154, home);
        var session = new Rac1Class749HostileSession();
        var initial = session.RegisterVeldinPlacement(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(Rac1Class749Hostile.LinkedObjectNativeState, initial.NativeState);
        Assert.Equal(
            Rac1Class749VeldinPopulation.SpecialLinkedMobyInstanceIndex,
            initial.LinkedTargetInstanceIndex);

        var ordinary = VeldinClass749(149, new Rac1Class749WorldPoint(104d, 33d, 190d));
        var ordinaryProbe = session.RegisterVeldinPlacement(
            ordinary,
            RuntimeEntityState.FromAuthored(ordinary));
        Assert.Null(ordinaryProbe.LinkedTargetInstanceIndex);

        var tracking = session.Step(
            source,
            new Rac1Class749TargetFacts(
                5d,
                0d,
                home,
                TargetDescriptor: LinkedTarget(new(96d, 33d, 258d))));
        Assert.Equal(Rac1Class749Hostile.LinkedObjectNativeState, tracking.NativeState);

        var terminal = session.Step(
            source,
            new Rac1Class749TargetFacts(
                5d,
                0d,
                home,
                LinkedObjectTerminal: true));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, terminal.NativeState);

        var atHome = session.Step(
            source,
            new Rac1Class749TargetFacts(
                5d,
                0d,
                home,
                TargetDescriptor: PlayerTarget(new(96d, 33d, 258d))));
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, atHome.NativeState);
    }

    private static Rac1Class749TargetDescriptor PlayerTarget(
        Rac1Class749WorldPoint position) =>
        new(Rac1GameplayEntityRef.Player, position);

    private static Rac1Class749TargetDescriptor LinkedTarget(
        Rac1Class749WorldPoint position) =>
        new(
            Rac1GameplayEntityRef.Moby(
                new Rac1MobyRuntimeKey(
                    NativeClassId: 900,
                    InstanceIndex: Rac1Class749VeldinPopulation.SpecialLinkedMobyInstanceIndex)),
            position);

    private static Rac1Class749TargetDescriptor VeldinTarget(
        int instanceIndex,
        Rac1Class749WorldPoint position) =>
        instanceIndex == Rac1Class749VeldinPopulation.SpecialLinkedInstanceIndex
            ? LinkedTarget(position)
            : PlayerTarget(position);

    private static RuntimeDynamicObject VeldinClass749(
        int instanceIndex,
        Rac1Class749WorldPoint home = default)
    {
        var source = Class749(instanceIndex, health: 1f, home: home);
        byte[] pvar = source.NativePayloads!.Single().Data;
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class749Hostile.ActivationGroupOffset, sizeof(int)),
            Rac1Class749VeldinPopulation.GetActivationGroup(instanceIndex));
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac1Class749Hostile.LinkModeOffset, sizeof(int)),
            instanceIndex == Rac1Class749VeldinPopulation.SpecialLinkedInstanceIndex ? 21 : -1);
        if (instanceIndex == Rac1Class749VeldinPopulation.SpecialLinkedInstanceIndex)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.StateThreeActivationGroupOffset, sizeof(int)), -1);
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.LinkedInstanceOffset, sizeof(int)), 197);
        }
        return source;
    }
}

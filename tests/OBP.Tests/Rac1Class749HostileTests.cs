using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1Class749HostileTests
{
    [Fact]
    public void ReadsExactRac1PvarAuthorityIncludingStatusAndHome()
    {
        var targetDestination = new Rac1Class749WorldPoint(4, 5, 6);
        var home = new Rac1Class749WorldPoint(10, 20, 30);
        var source = Class749(
            149,
            health: 1f,
            targetDestination: targetDestination,
            statusSentinel: 3,
            home: home);

        var authored = Assert.IsType<Rac1Class749AuthoredState>(
            Rac1Class749Hostile.ReadAuthored(source));

        Assert.Equal(new Rac1Class749Key(749, 149), authored.Key);
        Assert.Equal(1f, authored.Health);
        Assert.Equal(0x280, authored.PVarSize);
        Assert.Equal(targetDestination, authored.InitialTargetDestination);
        Assert.Equal(3, authored.InitialStatusSentinel);
        Assert.Equal(home, authored.InitialHomePosition);
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
    public void AttackSequenceFactsStayPinnedToRecoveredRetailValues()
    {
        Assert.Equal(0x3e32b8c2u, Rac1Class749Hostile.AttackFacingErrorRawBits);
        Assert.Equal(
            unchecked((int)Rac1Class749Hostile.AttackFacingErrorRawBits),
            BitConverter.SingleToInt32Bits(Rac1Class749Hostile.AttackFacingErrorExclusive));
        Assert.Equal(21, Rac1Class749Hostile.AttackSequenceFrameCount);
        Assert.Equal(102, Rac1Class749Hostile.AttackSequenceNativeUpdates);
        Assert.Equal(13, Rac1Class749Hostile.AttackMarkerFrameIndex);
        Assert.Equal(68, Rac1Class749Hostile.AttackMarkerNativeUpdate);
        Assert.Equal(34d, Rac1Class749Hostile.AttackMarker);
    }

    [Fact]
    public void StateFiveRetainsOnlyForStatusSentinelTwo()
    {
        var source = Class749(149, health: 1f, statusSentinel: 2);
        var session = Registered(source);

        var retained = session.Step(source, Facts(1.5, 0.1, statusSentinel: null));
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, retained.NativeState);

        var promoted = session.Step(source, Facts(1.5, 0.1, statusSentinel: 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, promoted.NativeState);
        Assert.Null(promoted.NativeSequence);
    }

    [Fact]
    public void StateSixUsesStrictAttackThresholdsAndStatusTwoFallback()
    {
        var source = Class749(149, health: 1f);
        var session = InStateSix(source);

        var atDistance = session.Step(source, Facts(Rac1Class749Hostile.AttackDistanceExclusive, 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, atDistance.NativeState);

        var atFacing = session.Step(source, Facts(1.5, Rac1Class749Hostile.AttackFacingErrorExclusive));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, atFacing.NativeState);

        var attack = session.Step(source, Facts(
            Math.BitDecrement(Rac1Class749Hostile.AttackDistanceExclusive),
            Math.BitDecrement((double)Rac1Class749Hostile.AttackFacingErrorExclusive)));
        Assert.Equal(Rac1Class749Hostile.AttackNativeState, attack.NativeState);
        Assert.Equal(Rac1Class749Hostile.AttackSequenceId, attack.NativeSequence);
        Assert.Equal(0, attack.NativeSequenceUpdate);
        Assert.Null(attack.Attack);

        var fallbackSource = Class749(150, health: 1f);
        var fallbackSession = InStateSix(fallbackSource);
        var fallback = fallbackSession.Step(fallbackSource, Facts(2, 0, statusSentinel: 2));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, fallback.NativeState);
        Assert.Equal(Rac1Class749Hostile.StateSixOrEightSequenceId, fallback.NativeSequence);
    }

    [Fact]
    public void StateSevenRetainsInclusiveDistanceButRequiresFacingInsideThreshold()
    {
        var source = Class749(149, health: 1f);
        var session = InStateSeven(source);

        var retained = session.Step(source, Facts(
            Rac1Class749Hostile.AttackRetainDistanceInclusive,
            Math.BitDecrement((double)Rac1Class749Hostile.AttackFacingErrorExclusive)));
        Assert.Equal(Rac1Class749Hostile.AttackNativeState, retained.NativeState);

        var thresholdSource = Class749(153, health: 1f);
        var thresholdSession = InStateSeven(thresholdSource);
        var thresholdExit = thresholdSession.Step(
            thresholdSource,
            Facts(1d, Rac1Class749Hostile.AttackFacingErrorExclusive));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, thresholdExit.NativeState);

        var distanceExitSource = Class749(150, health: 1f);
        var distanceExitSession = InStateSeven(distanceExitSource);
        var distanceExit = distanceExitSession.Step(distanceExitSource, Facts(
            Math.BitIncrement(Rac1Class749Hostile.AttackRetainDistanceInclusive), 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, distanceExit.NativeState);

        var facingExitSource = Class749(151, health: 1f);
        var facingExitSession = InStateSeven(facingExitSource);
        var facingExit = facingExitSession.Step(facingExitSource, Facts(
            1, Math.BitIncrement((double)Rac1Class749Hostile.AttackFacingErrorExclusive)));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, facingExit.NativeState);

        var sentinelSource = Class749(152, health: 1f);
        var sentinelSession = InStateSeven(sentinelSource);
        var sentinelExit = sentinelSession.Step(sentinelSource, Facts(1, 0, statusSentinel: 2));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, sentinelExit.NativeState);
    }

    [Fact]
    public void StateSevenEmitsOncePerMarkerCrossingAndNaturallyRepeatsAfterWrap()
    {
        var source = Class749(149, health: 1f);
        var session = InStateSeven(source);
        var attackTicks = new List<int>();

        for (int tick = 1; tick <= 170; tick++)
        {
            var probe = session.Step(source, Facts(1, 0));
            if (probe.Attack is not null) attackTicks.Add(tick);
            Assert.Equal(Rac1Class749Hostile.AttackNativeState, probe.NativeState);
        }

        Assert.Equal(new[] { 68, 170 }, attackTicks);
        Assert.Equal(102, attackTicks[1] - attackTicks[0]);
    }

    [Fact]
    public void StateEightUsesPvarHomeWithStrictDistanceAndStatusRecovery()
    {
        var home = new Rac1Class749WorldPoint(10, 4, -3);
        var source = Class749(149, health: 1f, home: home);
        var session = InStateEight(source, currentPosition: new Rac1Class749WorldPoint(20, 4, -3));

        var atBoundary = session.Step(source, Facts(
            3, 0, statusSentinel: 2,
            currentPosition: new Rac1Class749WorldPoint(11.5, 4, -3)));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, atBoundary.NativeState);

        var inside = session.Step(source, Facts(
            3, 0, statusSentinel: 2,
            currentPosition: new Rac1Class749WorldPoint(Math.BitDecrement(11.5), 4, -3)));
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, inside.NativeState);
        Assert.Equal(Rac1Class749Hostile.StateFiveSequenceId, inside.NativeSequence);

        var clearSource = Class749(150, health: 1f, home: home);
        var clearSession = InStateEight(clearSource, currentPosition: new Rac1Class749WorldPoint(20, 4, -3));
        var cleared = clearSession.Step(clearSource, Facts(
            3, 0, statusSentinel: 0, currentPosition: new Rac1Class749WorldPoint(20, 4, -3)));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, cleared.NativeState);
        Assert.Equal(Rac1Class749Hostile.StateSixOrEightSequenceId, cleared.NativeSequence);
    }

    [Theory]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFd)]
    [InlineData(Rac1Class749Hostile.TerminalNativeStateFe)]
    public void HostAdmittedWrenchStimulusReproducesRepresentativeDeathConsequence(int terminalStatus)
    {
        var source = Class749(149, health: 1f);
        var session = Registered(source);
        var wrench = new Rac1WrenchCombatController();
        var target = new Rac1WrenchContactTarget(
            Rac1Class749Hostile.NativeClassId,
            IsPlayerSelf: false);
        var damage = Assert.IsType<Rac1WrenchDamageResult>(
            wrench.ResolveHostAdmittedDamage(target));

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
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);

        Assert.Throws<NotSupportedException>(() => session.ApplyWrenchDamage(source, damage));
        Assert.Equal(2f, session.Probe(source).Health);
    }

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
                    TargetPosition: Rac1Class749VeldinPopulation.AuthoredRatchetStart));

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
            new Rac1Class749TargetFacts(10d, 0d, TargetPosition: new(150d, 0d, 120d)));
        var group3Suppressed = session.Step(
            group3,
            new Rac1Class749TargetFacts(10d, 0d, TargetPosition: new(150d, 0d, 120d)));
        var group3Admitted = session.Step(
            group3,
            new Rac1Class749TargetFacts(10d, 0d, TargetPosition: new(104d, 0d, 190d)));

        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, group0Probe.NativeState);
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, group3Suppressed.NativeState);
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, group3Admitted.NativeState);
    }

    [Fact]
    public void Instance154UsesLinkedConstructorThenReturnsHomeAfterTerminal()
    {
        var home = new Rac1Class749WorldPoint(96d, 33d, 258d);
        var source = VeldinClass749(154, home);
        var session = new Rac1Class749HostileSession();
        var initial = session.RegisterVeldinPlacement(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(Rac1Class749Hostile.LinkedObjectNativeState, initial.NativeState);

        var tracking = session.Step(
            source,
            new Rac1Class749TargetFacts(5d, 0d, home, TargetPosition: new(96d, 33d, 258d)));
        Assert.Equal(Rac1Class749Hostile.LinkedObjectNativeState, tracking.NativeState);

        var terminal = session.Step(
            source,
            new Rac1Class749TargetFacts(
                5d,
                0d,
                home,
                TargetPosition: new(96d, 33d, 258d),
                LinkedObjectTerminal: true));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, terminal.NativeState);

        var atHome = session.Step(
            source,
            new Rac1Class749TargetFacts(5d, 0d, home, TargetPosition: new(96d, 33d, 258d)));
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, atHome.NativeState);
    }

    [Fact]
    public void SessionCanTrackSyntheticClass749EntriesIndependently()
    {
        var session = new Rac1Class749HostileSession();
        var sources = Enumerable.Range(140, 16)
            .Select(index => Class749(index, health: 1f))
            .ToArray();

        foreach (var source in sources)
            session.Register(source, RuntimeEntityState.FromAuthored(source));

        Assert.Equal(16, session.RegisteredCount);

        var promoted = session.Step(sources[0], Facts(3, 0, statusSentinel: 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, promoted.NativeState);
        Assert.Equal(
            Rac1Class749Hostile.TargetSearchNativeState,
            session.Probe(sources[1]).NativeState);
        Assert.Throws<InvalidOperationException>(() =>
            session.Register(sources[0], RuntimeEntityState.FromAuthored(sources[0])));
    }

    private static Rac1Class749HostileSession Registered(RuntimeDynamicObject source)
    {
        var session = new Rac1Class749HostileSession();
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        return session;
    }

    private static Rac1Class749HostileSession InStateSix(RuntimeDynamicObject source)
    {
        var session = Registered(source);
        var probe = session.Step(source, Facts(3, 0));
        Assert.Equal(Rac1Class749Hostile.TargetedNativeState, probe.NativeState);
        return session;
    }

    private static Rac1Class749HostileSession InStateSeven(RuntimeDynamicObject source)
    {
        var session = InStateSix(source);
        var probe = session.Step(source, Facts(1, 0));
        Assert.Equal(Rac1Class749Hostile.AttackNativeState, probe.NativeState);
        return session;
    }

    private static Rac1Class749HostileSession InStateEight(
        RuntimeDynamicObject source,
        Rac1Class749WorldPoint currentPosition)
    {
        var session = InStateSix(source);
        var probe = session.Step(source, Facts(3, 0, statusSentinel: 2, currentPosition: currentPosition));
        Assert.Equal(Rac1Class749Hostile.ReturnHomeNativeState, probe.NativeState);
        return session;
    }

    private static Rac1Class749TargetFacts Facts(
        double distance,
        double facingError,
        int? statusSentinel = 0,
        Rac1Class749WorldPoint currentPosition = default) =>
        new(distance, facingError, currentPosition, statusSentinel);

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

    private static RuntimeDynamicObject Class749(
        int instanceIndex,
        float health,
        int pvarSize = 0x280,
        Rac1Class749WorldPoint targetDestination = default,
        int statusSentinel = 0,
        Rac1Class749WorldPoint home = default)
    {
        var pvar = new byte[pvarSize];
        if (pvarSize >= Rac1Class749Hostile.HealthOffset + sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.HealthOffset, sizeof(int)),
                BitConverter.SingleToInt32Bits(health));
        }
        if (pvarSize >= Rac1Class749Hostile.TargetDestinationOffset + 3 * sizeof(float))
        {
            WriteSingle(pvar, Rac1Class749Hostile.TargetDestinationOffset, checked((float)targetDestination.X));
            WriteSingle(pvar, Rac1Class749Hostile.TargetDestinationOffset + sizeof(float), checked((float)targetDestination.Z));
            WriteSingle(pvar, Rac1Class749Hostile.TargetDestinationOffset + 2 * sizeof(float), checked((float)targetDestination.Y));
        }
        if (pvarSize >= Rac1Class749Hostile.StatusSentinelOffset + sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.StatusSentinelOffset, sizeof(int)),
                statusSentinel);
        }
        if (pvarSize >= Rac1Class749Hostile.HomePositionOffset + 3 * sizeof(float))
        {
            WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset, checked((float)home.X));
            WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset + sizeof(float), checked((float)home.Z));
            WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset + 2 * sizeof(float), checked((float)home.Y));
        }

        var transform = new double[16];
        transform[12] = home.X;
        transform[13] = home.Y;
        transform[14] = home.Z;
        transform[15] = 1d;
        return new RuntimeDynamicObject(
            "rac1", Rac1Class749Hostile.NativeClassId, instanceIndex, null,
            $"moby:{Rac1Class749Hostile.NativeClassId}", $"moby:{instanceIndex}",
            new RuntimeObjectTransform(transform), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar)]);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(int)), BitConverter.SingleToInt32Bits(value));
}

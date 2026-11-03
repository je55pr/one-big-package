using OBP.RAC3.Gameplay;
using OBP.Runtime;
using static OBP.Tests.Rac3Class5821Fixtures;

namespace OBP.Tests;

/// <summary>Class-5821 native state-machine checks: windup (state 24), direct-attack gate (state 8), emitters (states 10 and 25).</summary>
public sealed class Rac3Class5821NativeStateTests
{
    [Fact]
    public void State24WindupUsesRetailSinglePrecisionRecurrence()
    {
        Assert.Equal(
            unchecked((int)0x3D888889),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821Actor.NativeAttackWindupInitial));
        Assert.Equal(
            unchecked((int)0xBB888889),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821Actor.NativeAttackAccumulatorDelta));
        Assert.Equal(
            unchecked((int)0x3E088889),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821Actor.NativeAttackWindupNormalizationSpan));

        float accumulator = Rac3Class5821Actor.NativeAttackWindupInitial;
        Rac3Class5821WindupStep step = null!;
        for (int tick = 1; tick <= 32; tick++)
        {
            step = Rac3Class5821Actor.AdvanceNativeState24(accumulator);
            accumulator = step.Accumulator;
            Assert.Equal(
                Rac3Class5821Actor.NativeAttackWindupState,
                step.NextNativeState);
        }

        Assert.Equal(
            unchecked((int)0x3F7FFFFF),
            BitConverter.SingleToInt32Bits(step.NormalizedProgress));

        step = Rac3Class5821Actor.AdvanceNativeState24(accumulator);
        Assert.Equal(
            Rac3Class5821Actor.NativeAttackWindupTicks,
            33);
        Assert.Equal(
            Rac3Class5821Actor.NativeDamageEmitterState,
            step.NextNativeState);
        Assert.Equal(1.03125f, step.NormalizedProgress);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(24, false)]
    [InlineData(25, false)]
    public void PopulationBackedDamageEmitterIsState10(
        byte nativeState,
        bool expected) =>
        Assert.Equal(
            expected,
            Rac3Class5821Actor.EmitsPopulationBackedNativeDamage(nativeState));

    [Fact]
    public void State8DirectAttackGateUsesRetailThresholdBits()
    {
        Assert.Equal(8, Rac3Class5821Actor.NativeOrdinaryAttackApproachState);
        Assert.Equal(
            unchecked((int)0x3EDF66F3),
            BitConverter.SingleToInt32Bits(
                Rac3Class5821Actor.NativeState8HeadingErrorUpperExclusive));
        Assert.Equal(1f, Rac3Class5821Actor.NativeState8TargetSeparationUpperExclusive);
        Assert.Equal(0x12u, Rac3Class5821Actor.NativeState8BlockedPlayerGlobalState);
        Assert.Equal(12, Rac3Class5821Actor.NativeState10SelectedAction);
    }

    [Theory]
    [InlineData(false, 0, 0.5f, 0f, false)]
    [InlineData(true, 0x12, 0.5f, 0f, false)]
    [InlineData(true, 0, 1f, 0f, false)]
    [InlineData(true, 0, 0.9999f, 0f, true)]
    public void State8DirectAttackGateFailsClosedOnTargetAndSeparation(
        bool hasTarget,
        uint playerState,
        float separation,
        float headingError,
        bool expected) =>
        Assert.Equal(
            expected,
            Rac3Class5821Actor.ShouldEnterNativeState10FromState8(
                new Rac3Class5821State8AttackFacts(
                    hasTarget,
                    playerState,
                    separation,
                    headingError)));

    [Fact]
    public void State8DirectAttackGateUsesStrictShortestHeadingError()
    {
        float threshold = Rac3Class5821Actor.NativeState8HeadingErrorUpperExclusive;
        Assert.False(
            Rac3Class5821Actor.ShouldEnterNativeState10FromState8(
                new Rac3Class5821State8AttackFacts(true, 0, 0.5f, threshold)));
        Assert.True(
            Rac3Class5821Actor.ShouldEnterNativeState10FromState8(
                new Rac3Class5821State8AttackFacts(
                    true,
                    0,
                    0.5f,
                    BitConverter.Int32BitsToSingle(
                        BitConverter.SingleToInt32Bits(threshold) - 1))));
    }

    [Fact]
    public void State8DirectAttackGateRejectsImpossibleScalarFacts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac3Class5821Actor.ShouldEnterNativeState10FromState8(
                new Rac3Class5821State8AttackFacts(
                    true,
                    0,
                    -0.01f,
                    0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac3Class5821Actor.ShouldEnterNativeState10FromState8(
                new Rac3Class5821State8AttackFacts(
                    true,
                    0,
                    0.5f,
                    -0.01f)));
    }

    [Theory]
    [InlineData(6f, 12, false)]
    [InlineData(6.0001f, 12, true)]
    [InlineData(9.9999f, 12, true)]
    [InlineData(10f, 12, false)]
    [InlineData(8f, 11, false)]
    public void State10EmitterUsesStrictProgressWindowAndMobyByte42(
        float progress,
        byte mobyByte42,
        bool expected) =>
        Assert.Equal(
            expected,
            Rac3Class5821Actor.IsNativeState10EmitterWindow(
                progress,
                mobyByte42));

    [Fact]
    public void State10AttackDescriptorMatchesAll62Table1Profile()
    {
        var authored = Rac3Class5821Actor.ReadAuthored(
            Class5821(instanceIndex: 430, authoredLifetime: 1));

        Rac3Class5821AttackDescriptor attack =
            Rac3Class5821Actor.NativeState10Attack(authored);

        Assert.Equal(10, attack.NativeState);
        Assert.Equal(0.5f, attack.Radius);
        Assert.Equal(1f, attack.Damage);
        Assert.Equal(0x00000001u, attack.Flags);
        Assert.Equal(0, attack.RecordKind);
        Assert.Equal(1, attack.RecordByte29);
        Assert.Equal(0.75f, attack.SpatialScalar);

        var source = new Rac3MobyRuntimeKey(
            Rac3Class5821Actor.NativeClassId,
            430);
        Rac3GameplayDamageEvent playerDamage =
            Rac3Class5821Actor.NativeState10RatchetDamage(source, authored);
        Assert.Equal(Rac3GameplayEntityRef.Moby(source), playerDamage.Source);
        Assert.Equal(Rac3GameplayEntityRef.Player, playerDamage.Target);
        Assert.Equal(1d, playerDamage.NativeDamage);
        Assert.Equal(0x00000001u, playerDamage.NativeDamageFlags);
        Assert.Equal((byte)0, playerDamage.NativeRecordKind);
    }

    [Fact]
    public void State10AttackFailsClosedOutsideTable1AuthoredProfile()
    {
        RuntimeDynamicObject source =
            Class5821(instanceIndex: 430, authoredLifetime: 1);
        source = WithPvar(source, pvar =>
            pvar[Rac3Class5821Actor.NativeOrdinaryAttackFlagSelectorOffset] = 1);

        Rac3Class5821AuthoredState authored =
            Rac3Class5821Actor.ReadAuthored(source);
        Assert.False(
            Rac3Class5821Actor.HasRecoveredTable1OrdinaryAttackProfile(authored));
        Assert.Throws<NotSupportedException>(() =>
            Rac3Class5821Actor.NativeState10Attack(authored));
    }

    [Fact]
    public void StateEntrySetupBitIsOneShot()
    {
        Assert.True(Rac3Class5821Actor.NeedsNativeStateEntrySetup(0x00));
        Assert.False(Rac3Class5821Actor.NeedsNativeStateEntrySetup(0x01));
        Assert.False(Rac3Class5821Actor.NeedsNativeStateEntrySetup(0x03));
    }

    [Theory]
    [InlineData(10f, 10f, true)]
    [InlineData(9.5f, 10f, true)]
    [InlineData(10.5f, 10f, false)]
    public void State25ExitUsesNativeZAgainstQueryProducedThreshold(
        float nativeZ,
        float threshold,
        bool expected) =>
        Assert.Equal(
            expected,
            Rac3Class5821Actor.ShouldEnterNativeState26(
                nativeZ,
                threshold));

    [Fact]
    public void State25AttackDescriptorMatchesTable1Emitter()
    {
        Rac3Class5821AttackDescriptor attack =
            Rac3Class5821Actor.NativeState25Attack();

        Assert.Equal(25, attack.NativeState);
        Assert.Equal(0.5f, attack.Radius);
        Assert.Equal(1f, attack.Damage);
        Assert.Equal(0x02000001u, attack.Flags);
        Assert.Equal(0, attack.RecordKind);
        Assert.Equal(1, attack.RecordByte29);
        Assert.Equal(1f, attack.SpatialScalar);

        var source = new Rac3MobyRuntimeKey(
            Rac3Class5821Actor.NativeClassId,
            430);
        Rac3GameplayDamageEvent playerDamage =
            Rac3Class5821Actor.NativeState25RatchetDamage(source);
        Assert.Equal(Rac3GameplayEntityRef.Moby(source), playerDamage.Source);
        Assert.Equal(Rac3GameplayEntityRef.Player, playerDamage.Target);
        Assert.Equal(1d, playerDamage.NativeDamage);
        Assert.Equal(0x02000001u, playerDamage.NativeDamageFlags);
        Assert.Equal((byte)0, playerDamage.NativeRecordKind);
    }
}

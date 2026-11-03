using OBP.Core;
using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac3Class5821ActorTests
{
    [Fact]
    public void AdmissionParsesEachPayloadOnceAndRetainsTheTypedResults()
    {
        var source = Class5821(430, 1);
        var payloads = new CountingPayloads([.. source.NativePayloads!, new(Rac3MobyRuntimeSession.InstancePayloadFormat, new byte[Rac3MobyPlacement.NativeRecordBytes])]);
        source = source with { NativePayloads = payloads };
        Assert.True(Rac3Class5821Admission.TryRead(source, out var admission, out var rejection), rejection);
        Assert.Same(source, admission!.Source);
        Assert.Equal(1f, admission.Authored.InitialLifetime);
        Assert.Equal(0f, admission.Placement.NativeX);
        Assert.Equal(2, payloads.Enumerations); // One state read and one placement read.
        Assert.Null(rejection);
    }

    [Theory]
    [InlineData("state-size")]
    [InlineData("placement-missing")]
    [InlineData("placement-size")]
    [InlineData("placement-nonfinite")]
    [InlineData("state-duplicate")]
    [InlineData("placement-duplicate")]
    [InlineData("unsupported-profile")]
    public void AdmissionRejectsMalformedStateOrPlacementWithoutPublishingAnEntry(string fault)
    {
        var source = Class5821(430, fault == "unsupported-profile" ? (short)0 : (short)1);
        var payloads = source.NativePayloads!.ToList();
        var placement = new RuntimeOpaquePayload(Rac3MobyRuntimeSession.InstancePayloadFormat, new byte[Rac3MobyPlacement.NativeRecordBytes]);
        if (fault != "placement-missing") payloads.Add(placement);
        if (fault == "state-size") payloads[0] = new(payloads[0].Format, new byte[8]);
        if (fault == "placement-size") payloads[1] = new(placement.Format, new byte[8]);
        if (fault == "placement-nonfinite") BinaryPrimitives.WriteSingleLittleEndian(placement.Data.AsSpan(Rac3MobyPlacement.NativePositionOffset), float.NaN);
        if (fault == "state-duplicate") payloads.Add(payloads[0]);
        if (fault == "placement-duplicate") payloads.Add(placement);
        Assert.False(Rac3Class5821Admission.TryRead(source with { NativePayloads = payloads }, out var admission, out var rejection));
        Assert.Null(admission);
        Assert.False(string.IsNullOrEmpty(rejection));
    }

    private sealed class CountingPayloads(RuntimeOpaquePayload[] items) : IReadOnlyList<RuntimeOpaquePayload>
    {
        public int Enumerations { get; private set; }
        public int Count => items.Length;
        public RuntimeOpaquePayload this[int index] => items[index];
        public IEnumerator<RuntimeOpaquePayload> GetEnumerator() { Enumerations++; return ((IEnumerable<RuntimeOpaquePayload>)items).GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void AuthoredReaderPreservesRecoveredDamageProfile()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);

        Rac3Class5821AuthoredState authored =
            Rac3Class5821Actor.ReadAuthored(source);

        Assert.Equal(new Rac3MobyRuntimeKey(5821, 430), authored.Key);
        Assert.Equal(0x30, authored.LifetimeRelativePointer);
        Assert.Equal(0x220, authored.TargetBlockRelativePointer);
        Assert.Equal(0x160, authored.DamageConfigRelativePointer);
        Assert.Equal(1f, authored.InitialLifetime);
        Assert.Equal(1, authored.AuthoredLifetime);
        Assert.Equal(0f, authored.DamageConfigVerticalThreshold);
        Assert.Equal(0, authored.DamageConfigByte49);
        Assert.Equal(0f, authored.DamageConfigSameClassMultiplier);
        Assert.Equal(1f, authored.DamageConfigOptionalMultiplier);
        Assert.Equal(1, authored.NativeOrdinaryAttackDamageByte);
        Assert.Equal(0, authored.NativeOrdinaryAttackFlagSelector);
        Assert.Equal(13, authored.PrimaryTargetSelectorIndex);
        Assert.Equal(16f, authored.PrimaryTargetSelectorRadius);
        Assert.Equal(18, authored.SecondaryTargetSelectorIndex);
        Assert.Equal(32f, authored.SecondaryTargetSelectorRadius);
        Assert.Equal(0, authored.InitialTargetSelectorMode);
        Assert.Equal(0, authored.InitialTargetSelectorAux);
        Assert.True(Rac3Class5821Actor.HasRecoveredTable1DamageProfile(authored));
        Assert.True(Rac3Class5821Actor.HasRecoveredTable1OrdinaryAttackProfile(authored));
        Assert.True(Rac3Class5821Actor.HasRecoveredTable1TargetSelectionProfile(authored));
    }

    [Fact]
    public void TargetSelectionRequestUsesPrimarySelectorForObservedSubtypeOne()
    {
        Rac3Class5821AuthoredState authored =
            Rac3Class5821Actor.ReadAuthored(
                Class5821(instanceIndex: 430, authoredLifetime: 1));

        Rac3Class5821TargetSelectionRequest request =
            Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype: Rac3Class5821Actor.NativeObservedSelectorSubtypePrimary,
                runtimeModeEnabled: false);

        Assert.Equal(13, request.NativeSelectorIndex);
        Assert.Equal(16f, request.Radius);
        Assert.Equal(Rac3Class5821Actor.NativeTargetSelectorCandidateTagOne, request.NativeCandidateTag);
        Assert.False(request.NativeAuxEnabled);
        Assert.Equal(Rac3Class5821Actor.TargetSelectorWorkspaceOffset, request.WorkspaceOffset);
        Assert.Equal(10f, request.NativeF13);
        Assert.Equal(1f, request.NativeF14);
        Assert.True(request.SeedRatchetBeforeCandidateReplacement);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void TargetSelectionRequestUsesSecondarySelectorForObservedResidentSubtypes(
        byte nativeSubtype)
    {
        Rac3Class5821AuthoredState authored =
            Rac3Class5821Actor.ReadAuthored(
                Class5821(instanceIndex: 430, authoredLifetime: 1));

        Rac3Class5821TargetSelectionRequest request =
            Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype,
                runtimeModeEnabled: true,
                runtimeAuxEnabled: true);

        Assert.Equal(18, request.NativeSelectorIndex);
        Assert.Equal(32f, request.Radius);
        Assert.Equal(Rac3Class5821Actor.NativeTargetSelectorCandidateTagTwo, request.NativeCandidateTag);
        Assert.False(request.NativeAuxEnabled);
        Assert.True(request.SeedRatchetBeforeCandidateReplacement);
    }

    [Fact]
    public void TargetSelectionModeOneCarriesRecoveredAuxBit()
    {
        Rac3Class5821AuthoredState authored =
            Rac3Class5821Actor.ReadAuthored(
                Class5821(instanceIndex: 430, authoredLifetime: 1));

        Rac3Class5821TargetSelectionRequest request =
            Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype: 0,
                runtimeModeEnabled: false,
                runtimeAuxEnabled: true);

        Assert.Equal(Rac3Class5821Actor.NativeTargetSelectorCandidateTagOne, request.NativeCandidateTag);
        Assert.True(request.NativeAuxEnabled);
    }

    [Fact]
    public void TargetSelectionFailsClosedForUnobservedSubtypeOrBadRelocation()
    {
        RuntimeDynamicObject source =
            Class5821(instanceIndex: 430, authoredLifetime: 1);
        Rac3Class5821AuthoredState authored =
            Rac3Class5821Actor.ReadAuthored(source);

        Assert.Throws<NotSupportedException>(() =>
            Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype: 9,
                runtimeModeEnabled: false));

        byte[] pvar = source.NativePayloads!
            .Single(payload =>
                payload.Format == Rac3MobyRuntimeSession.PVarPayloadFormat)
            .Data.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.TargetBlockRelativePointerOffset, sizeof(int)),
            0x224);
        source = source with
        {
            NativePayloads =
            [
                new RuntimeOpaquePayload(
                    Rac3MobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ],
        };
        authored = Rac3Class5821Actor.ReadAuthored(source);

        Assert.False(
            Rac3Class5821Actor.HasRecoveredTable1TargetSelectionProfile(authored));
        Assert.Throws<NotSupportedException>(() =>
            Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype: 0,
                runtimeModeEnabled: false));
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
            Rac3Class5821Actor.AdmitsLifetimeDamage(
                flags,
                damage,
                recordKind));
    }

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
        byte[] pvar = source.NativePayloads!
            .Single(payload =>
                payload.Format == Rac3MobyRuntimeSession.PVarPayloadFormat)
            .Data.ToArray();
        pvar[Rac3Class5821Actor.NativeOrdinaryAttackFlagSelectorOffset] = 1;
        source = source with
        {
            NativePayloads =
            [
                new RuntimeOpaquePayload(
                    Rac3MobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ],
        };

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
        byte[] pvar = source.NativePayloads!
            .Single(payload => payload.Format == Rac3MobyRuntimeSession.PVarPayloadFormat)
            .Data.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.LifetimeRelativePointerOffset, sizeof(int)),
            0x44);
        source = source with
        {
            NativePayloads =
            [
                new RuntimeOpaquePayload(
                    Rac3MobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ],
        };

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

    [SkippableFact]
    public void RetailTable1Class5821PopulationCarriesRecoveredDamageProfile()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld world = Rac3WorldImport.Build(reader, 1);
        var sources = world.DynamicObjects!
            .Where(source => source.NativeClassId == Rac3Class5821Actor.NativeClassId)
            .OrderBy(source => source.InstanceIndex)
            .ToArray();

        Assert.Equal(62, sources.Length);
        var admitted = sources.Select(source =>
        {
            Assert.True(Rac3Class5821Admission.TryRead(source, out var admission, out var rejection), rejection);
            Assert.Null(rejection);
            return admission!;
        }).ToArray();
        Assert.Equal(62, admitted.Length);
        Assert.All(admitted, entry => Assert.Equal(Rac3MobyPlacement.ReadAuthored(entry.Source), entry.Placement));
        Rac3Class5821AuthoredState[] authoredStates = sources
            .Select(Rac3Class5821Actor.ReadAuthored)
            .ToArray();

        Assert.All(authoredStates, authored =>
        {
            Assert.Equal(1, authored.AuthoredLifetime);
            Assert.True(Rac3Class5821Actor.HasRecoveredTable1DamageProfile(authored));
            Assert.True(
                Rac3Class5821Actor.HasRecoveredTable1OrdinaryAttackProfile(authored));
            Assert.True(
                Rac3Class5821Actor.HasRecoveredTable1TargetSelectionProfile(authored));
        });

        var primary = authoredStates
            .GroupBy(authored => authored.PrimaryTargetSelectorIndex)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(31, primary[-1]);
        Assert.Equal(12, primary[13]);
        Assert.Equal(7, primary[82]);
        Assert.Equal(4, primary[26]);
        Assert.Equal(3, primary[66]);
        Assert.Equal(3, primary[73]);
        Assert.Equal(2, primary[7]);

        var secondary = authoredStates
            .GroupBy(authored => authored.SecondaryTargetSelectorIndex)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(21, secondary[-1]);
        Assert.Equal(12, secondary[18]);
        Assert.Equal(7, secondary[82]);
        Assert.Equal(6, secondary[79]);
        Assert.Equal(4, secondary[24]);
        Assert.Equal(4, secondary[27]);
        Assert.Equal(3, secondary[66]);
        Assert.Equal(3, secondary[73]);
        Assert.Equal(2, secondary[7]);

        Assert.Equal(
            55,
            authoredStates.Count(authored => authored.InitialTargetSelectorMode == 0));
        Assert.Equal(
            7,
            authoredStates.Count(authored => authored.InitialTargetSelectorMode == 1));
    }

    [Fact]
    public void AuthoredReaderFailsClosedOnWrongClassOrPvarSize()
    {
        var source = Class5821(instanceIndex: 430, authoredLifetime: 1);
        Assert.Throws<ArgumentException>(() =>
            Rac3Class5821Actor.ReadAuthored(
                source with { NativeClassId = 6306 }));

        var payloads = source.NativePayloads!
            .Select(payload => payload.Format == Rac3MobyRuntimeSession.PVarPayloadFormat
                ? new RuntimeOpaquePayload(payload.Format, new byte[8])
                : payload)
            .ToArray();
        Assert.Throws<InvalidDataException>(() =>
            Rac3Class5821Actor.ReadAuthored(
                source with { NativePayloads = payloads }));
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

    private static RuntimeDynamicObject Class5821(
        int instanceIndex,
        short authoredLifetime)
    {
        byte[] pvar = new byte[Rac3Class5821Actor.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.LifetimeRelativePointerOffset, sizeof(int)),
            Rac3Class5821Actor.RuntimeLifetimeOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.TargetBlockRelativePointerOffset, sizeof(int)),
            Rac3Class5821Actor.RuntimeTargetBlockOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigRelativePointerOffset, sizeof(int)),
            Rac3Class5821Actor.DamageConfigOffset);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.RuntimeLifetimeOffset, sizeof(float)),
            1f);
        BinaryPrimitives.WriteInt16LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.AuthoredLifetimeOffset, sizeof(short)),
            authoredLifetime);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigVerticalThresholdOffset, sizeof(float)),
            0f);
        pvar[Rac3Class5821Actor.DamageConfigByte49Offset] = 0;
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigSameClassMultiplierOffset, sizeof(float)),
            0f);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.DamageConfigOptionalMultiplierOffset, sizeof(float)),
            1f);
        pvar[Rac3Class5821Actor.NativeOrdinaryAttackDamageByteOffset] = 1;
        pvar[Rac3Class5821Actor.NativeOrdinaryAttackFlagSelectorOffset] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.PrimaryTargetSelectorIndexOffset, sizeof(int)),
            13);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.PrimaryTargetSelectorRadiusOffset, sizeof(float)),
            16f);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.SecondaryTargetSelectorIndexOffset, sizeof(int)),
            18);
        BinaryPrimitives.WriteSingleLittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.SecondaryTargetSelectorRadiusOffset, sizeof(float)),
            32f);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.RuntimeTargetSelectorModeOffset, sizeof(int)),
            0);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(Rac3Class5821Actor.RuntimeTargetSelectorAuxOffset, sizeof(int)),
            0);

        return new RuntimeDynamicObject(
            ObpSourceGame.Rac3,
            Rac3Class5821Actor.NativeClassId,
            instanceIndex,
            null,
            $"moby:{Rac3Class5821Actor.NativeClassId}",
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
                    Rac3MobyRuntimeSession.PVarPayloadFormat,
                    pvar),
            ]);
    }
}

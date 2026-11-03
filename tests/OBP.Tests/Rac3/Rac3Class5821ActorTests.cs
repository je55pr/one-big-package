using OBP.Core;
using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using static OBP.Tests.Rac3Class5821Fixtures;

namespace OBP.Tests;

/// <summary>
/// Class-5821 admission, authored PVar state and target-selection request (including the retail table-1 population).
/// Native state-machine checks are in <see cref="Rac3Class5821NativeStateTests"/>; damage and lifetime checks are in <see cref="Rac3Class5821DamageTests"/>.
/// </summary>
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

        source = WithPvar(source, pvar =>
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac3Class5821Actor.TargetBlockRelativePointerOffset, sizeof(int)),
                0x224));
        authored = Rac3Class5821Actor.ReadAuthored(source);

        Assert.False(
            Rac3Class5821Actor.HasRecoveredTable1TargetSelectionProfile(authored));
        Assert.Throws<NotSupportedException>(() =>
            Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype: 0,
                runtimeModeEnabled: false));
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
}

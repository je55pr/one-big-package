using System.Buffers.Binary;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// TABLE1 retail-backed profile for UYA native class 5821.
///
/// The native update owns a one-unit lifetime scalar, consumes UYA damage records
/// through the common owned-record resolver, can acquire Ratchet through a PVar
/// target pointer, and carries population-backed state-10 plus class-family
/// state-25 damage emitters. Native state execution and the UYA player-life
/// consequence remain host-unimplemented.
/// </summary>
public static class UyaClass5821Actor
{
    public const int NativeClassId = 5821;
    public const int PVarSize = 0x5F0;

    public const int LifetimeRelativePointerOffset = 0x00;
    public const int TargetBlockRelativePointerOffset = 0x0C;
    public const int DamageConfigRelativePointerOffset = 0x10;
    public const int RuntimeLifetimeOffset = 0x30;
    public const int AuthoredLifetimeOffset = 0x34;
    public const int DamageConfigOffset = 0x160;
    public const int DamageConfigVerticalThresholdOffset = 0x1A0;
    public const int DamageConfigByte49Offset = 0x1A9;
    public const int DamageConfigSameClassMultiplierOffset = 0x208;
    public const int DamageConfigOptionalMultiplierOffset = 0x20C;
    public const int RuntimeTargetBlockOffset = 0x220;
    public const int RuntimeTargetPointerOffset = 0x230;
    public const int TargetSelectorWorkspaceOffset = 0x310;
    public const int PrimaryTargetSelectorIndexOffset = 0x2D0;
    public const int PrimaryTargetSelectorRadiusOffset = 0x2D4;
    public const int SecondaryTargetSelectorIndexOffset = 0x2D8;
    public const int SecondaryTargetSelectorRadiusOffset = 0x2DC;
    public const int RuntimeTargetSelectorModeOffset = 0x3E4;
    public const int RuntimeTargetSelectorAuxOffset = 0x5E4;
    public const float NativeTargetSelectorF13 = 10f;
    public const float NativeTargetSelectorF14 = 1f;
    public const int NativeTargetSelectorModeOne = 1;
    public const int NativeTargetSelectorModeTwo = 2;
    public const byte NativeObservedSelectorSubtypePrimary = 1;
    public const byte NativeObservedSelectorSubtypeSecondaryA = 0;
    public const byte NativeObservedSelectorSubtypeSecondaryB = 4;
    public const int NativeOrdinaryAttackDamageByteOffset = 0x44;
    public const int NativeOrdinaryAttackFlagSelectorOffset = 0x5F;

    public const uint QueriedDamageMask = 0x00010000;
    public const byte NoLifetimeSubtractRecordKind = 10;

    // Population-backed TABLE1 state-10 spatial damage emitter.
    public const byte NativeOrdinaryAttackState = 10;
    public const float NativeOrdinaryAttackRadius = 0.5f;
    public const float NativeOrdinaryAttackSpatialScalar = 0.75f;
    public const float NativeOrdinaryAttackProgressLowerExclusive = 6f;
    public const float NativeOrdinaryAttackProgressUpperExclusive = 10f;
    public const byte NativeOrdinaryAttackRequiredMobyByte42 = 12;
    public const uint NativeOrdinaryAttackFlagsWhenSelectorZero = 0x00000001;
    public const uint NativeOrdinaryAttackFlagsWhenSelectorNonzero = 0x00010000;
    public const byte NativeOrdinaryAttackRecordKind = 0;
    public const byte NativeOrdinaryAttackRecordByte29 = 1;

    // Direct TABLE1 state-8 -> state-10 attack transition.
    public const byte NativeOrdinaryAttackApproachState = 8;
    public const uint NativeState8BlockedPlayerGlobalState = 0x12;
    public const float NativeState8TargetSeparationUpperExclusive = 1f;
    public static readonly float NativeState8HeadingErrorUpperExclusive =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3EDF66F3));
    public const byte NativeState10SelectedAction = 12;

    // Class-family state-24 -> state-25 attack scheduler. The retained TABLE1
    // live population has not yet proved eligibility for this subtype path.
    public const byte NativeAttackWindupState = 24;
    public const byte NativeDamageEmitterState = 25;
    public const byte NativeAttackFollowThroughState = 26;
    public const int NativeAttackAccumulatorOffset = 0x300;
    public const int NativeAttackQueryVerticalThresholdOffset = 0x328;
    public const int NativeStateEntryFlagsOffset = 0xBE;
    public const byte NativeStateEntrySetupMask = 0x01;
    public const int NativeAttackWindupTicks = 33;
    public static readonly float NativeAttackWindupInitial =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3D888889));
    public static readonly float NativeAttackAccumulatorDelta =
        BitConverter.Int32BitsToSingle(unchecked((int)0xBB888889));
    public static readonly float NativeAttackWindupNormalizationSpan =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3E088889));
    public const float NativeState25AccumulatorFloor = -1f;
    public const float NativeState25QueryVerticalOffset = 0.5f;

    // TABLE1 state-25 outbound damage emitter at 0x00351D08.
    public const float NativeDamageEmitterRadius = 0.5f;
    public const float NativeDamageEmitterDamage = 1f;
    public const uint NativeDamageEmitterFlags = 0x02000001;
    public const byte NativeDamageEmitterRecordKind = 0;
    public const byte NativeDamageEmitterRecordByte29 = 1;

    public const byte NativeDormantState = 0x00;
    public const byte NativeObservedTerminalState = 0xFD;
    public const double NativeObservedTerminalLifetime = -1999d;

    public static UyaClass5821AuthoredState ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac3" || source.NativeClassId != NativeClassId)
            throw new ArgumentException(
                $"Source is not UYA native class {NativeClassId}.",
                nameof(source));

        byte[] pvar = source.NativePayloads?
            .SingleOrDefault(payload =>
                payload.Format == UyaMobyRuntimeSession.PVarPayloadFormat)?.Data
            ?? throw new InvalidDataException(
                $"UYA class {NativeClassId} instance {source.InstanceIndex} has no PVar payload.");

        if (pvar.Length != PVarSize)
            throw new InvalidDataException(
                $"UYA class {NativeClassId} instance {source.InstanceIndex} has " +
                $"PVar length {pvar.Length}, expected {PVarSize}.");

        return new UyaClass5821AuthoredState(
            new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(LifetimeRelativePointerOffset, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(TargetBlockRelativePointerOffset, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(DamageConfigRelativePointerOffset, sizeof(int))),
            BinaryPrimitives.ReadSingleLittleEndian(
                pvar.AsSpan(RuntimeLifetimeOffset, sizeof(float))),
            BinaryPrimitives.ReadInt16LittleEndian(
                pvar.AsSpan(AuthoredLifetimeOffset, sizeof(short))),
            BinaryPrimitives.ReadSingleLittleEndian(
                pvar.AsSpan(DamageConfigVerticalThresholdOffset, sizeof(float))),
            pvar[DamageConfigByte49Offset],
            BinaryPrimitives.ReadSingleLittleEndian(
                pvar.AsSpan(DamageConfigSameClassMultiplierOffset, sizeof(float))),
            BinaryPrimitives.ReadSingleLittleEndian(
                pvar.AsSpan(DamageConfigOptionalMultiplierOffset, sizeof(float))),
            pvar[NativeOrdinaryAttackDamageByteOffset],
            pvar[NativeOrdinaryAttackFlagSelectorOffset],
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(PrimaryTargetSelectorIndexOffset, sizeof(int))),
            BinaryPrimitives.ReadSingleLittleEndian(
                pvar.AsSpan(PrimaryTargetSelectorRadiusOffset, sizeof(float))),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(SecondaryTargetSelectorIndexOffset, sizeof(int))),
            BinaryPrimitives.ReadSingleLittleEndian(
                pvar.AsSpan(SecondaryTargetSelectorRadiusOffset, sizeof(float))),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(RuntimeTargetSelectorModeOffset, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(RuntimeTargetSelectorAuxOffset, sizeof(int))));
    }

    /// <summary>
    /// Exact TABLE1 profile under which the recovered common damage consumer has
    /// been traced. Other class-5821 variants remain fail-closed.
    /// </summary>
    public static bool HasRecoveredTable1DamageProfile(
        UyaClass5821AuthoredState authored) =>
        authored.LifetimeRelativePointer == RuntimeLifetimeOffset &&
        authored.DamageConfigRelativePointer == DamageConfigOffset &&
        authored.InitialLifetime == 1f &&
        authored.AuthoredLifetime == 1 &&
        authored.DamageConfigVerticalThreshold == 0f &&
        authored.DamageConfigByte49 == 0 &&
        authored.DamageConfigSameClassMultiplier == 0f &&
        authored.DamageConfigOptionalMultiplier == 1f;

    public static bool HasRecoveredTable1OrdinaryAttackProfile(
        UyaClass5821AuthoredState authored) =>
        authored.NativeOrdinaryAttackDamageByte == 1 &&
        authored.NativeOrdinaryAttackFlagSelector == 0;

    public static bool HasRecoveredTable1TargetSelectionProfile(
        UyaClass5821AuthoredState authored) =>
        authored.TargetBlockRelativePointer == RuntimeTargetBlockOffset &&
        authored.PrimaryTargetSelectorIndex >= -1 &&
        authored.PrimaryTargetSelectorRadius == 16f &&
        authored.SecondaryTargetSelectorIndex >= -1 &&
        authored.SecondaryTargetSelectorRadius == 32f &&
        authored.InitialTargetSelectorMode is 0 or 1 &&
        authored.InitialTargetSelectorAux == 0;

    /// <summary>
    /// Builds the exact TABLE1 request passed into shared selector 0x004539A8
    /// for observed resident subtypes. The shared selector seeds Ratchet into
    /// the target block first, then may replace Ratchet with the best candidate
    /// from the runtime target-group table. OBP does not currently reconstruct
    /// that dynamic group table, so this method describes the request only.
    /// </summary>
    public static UyaClass5821TargetSelectionRequest BuildTable1TargetSelectionRequest(
        UyaClass5821AuthoredState authored,
        byte nativeSubtype,
        bool runtimeModeEnabled,
        bool runtimeAuxEnabled = false)
    {
        if (!HasRecoveredTable1TargetSelectionProfile(authored))
            throw new NotSupportedException(
                "UYA class-5821 target selection is not the recovered TABLE1 profile.");

        bool primary = nativeSubtype switch
        {
            NativeObservedSelectorSubtypePrimary => true,
            NativeObservedSelectorSubtypeSecondaryA => false,
            NativeObservedSelectorSubtypeSecondaryB => false,
            _ => throw new NotSupportedException(
                $"UYA class-5821 subtype {nativeSubtype} has no recovered TABLE1 selector request."),
        };

        int mode = runtimeModeEnabled
            ? NativeTargetSelectorModeTwo
            : NativeTargetSelectorModeOne;
        bool aux = !runtimeModeEnabled && runtimeAuxEnabled;

        return new UyaClass5821TargetSelectionRequest(
            nativeSubtype,
            primary ? authored.PrimaryTargetSelectorIndex : authored.SecondaryTargetSelectorIndex,
            primary ? authored.PrimaryTargetSelectorRadius : authored.SecondaryTargetSelectorRadius,
            mode,
            aux,
            TargetSelectorWorkspaceOffset,
            NativeTargetSelectorF13,
            NativeTargetSelectorF14,
            SeedRatchetBeforeCandidateReplacement: true);
    }

    /// <summary>
    /// Exact direct state-8 gate recovered from TABLE1. The upstream selector
    /// request is recovered, but dynamic target-group candidate resolution is
    /// not. Callers must therefore supply an already-established current target;
    /// missing target facts fail closed rather than promoting the player.
    /// </summary>
    public static bool ShouldEnterNativeState10FromState8(
        UyaClass5821State8AttackFacts facts)
    {
        if (!float.IsFinite(facts.TargetSeparation) ||
            facts.TargetSeparation < 0f)
            throw new ArgumentOutOfRangeException(nameof(facts));
        if (!float.IsFinite(facts.ShortestHeadingErrorRadians) ||
            facts.ShortestHeadingErrorRadians < 0f)
            throw new ArgumentOutOfRangeException(nameof(facts));

        return facts.HasCurrentTarget &&
               facts.PlayerGlobalState != NativeState8BlockedPlayerGlobalState &&
               facts.TargetSeparation < NativeState8TargetSeparationUpperExclusive &&
               facts.ShortestHeadingErrorRadians < NativeState8HeadingErrorUpperExclusive;
    }

    public static bool IsNativeState10EmitterWindow(
        float actionProgress,
        byte mobyByte42)
    {
        if (!float.IsFinite(actionProgress))
            throw new ArgumentOutOfRangeException(nameof(actionProgress));

        return actionProgress > NativeOrdinaryAttackProgressLowerExclusive &&
               actionProgress < NativeOrdinaryAttackProgressUpperExclusive &&
               mobyByte42 == NativeOrdinaryAttackRequiredMobyByte42;
    }

    public static UyaClass5821AttackDescriptor NativeState10Attack(
        UyaClass5821AuthoredState authored)
    {
        if (!HasRecoveredTable1OrdinaryAttackProfile(authored))
            throw new NotSupportedException(
                "UYA class-5821 state-10 attack profile is not the recovered TABLE1 profile.");

        uint flags = authored.NativeOrdinaryAttackFlagSelector == 0
            ? NativeOrdinaryAttackFlagsWhenSelectorZero
            : NativeOrdinaryAttackFlagsWhenSelectorNonzero;

        return new UyaClass5821AttackDescriptor(
            NativeOrdinaryAttackState,
            NativeOrdinaryAttackRadius,
            authored.NativeOrdinaryAttackDamageByte,
            flags,
            NativeOrdinaryAttackRecordKind,
            NativeOrdinaryAttackRecordByte29,
            NativeOrdinaryAttackSpatialScalar);
    }

    public static UyaGameplayDamageEvent NativeState10RatchetDamage(
        UyaMobyRuntimeKey source,
        UyaClass5821AuthoredState authored)
    {
        UyaClass5821AttackDescriptor attack = NativeState10Attack(authored);
        return new UyaGameplayDamageEvent(
            UyaGameplayEntityRef.Moby(source),
            UyaGameplayEntityRef.Player,
            nativeDamage: attack.Damage,
            nativeDamageFlags: attack.Flags,
            nativeRecordKind: attack.RecordKind);
    }

    /// <summary>
    /// The class-5821 wrapper calls the common UYA resolver with mask 0x00010000.
    /// Record kind 10 follows the recovered no-lifetime-subtraction branch.
    /// This predicate intentionally retains only positive lifetime damage.
    /// </summary>
    public static bool AdmitsLifetimeDamage(
        uint nativeDamageFlags,
        double nativeDamage,
        byte nativeRecordKind) =>
        (nativeDamageFlags & QueriedDamageMask) != 0 &&
        nativeRecordKind != NoLifetimeSubtractRecordKind &&
        nativeDamage > 0d &&
        nativeDamage <= float.MaxValue;

    /// <summary>
    /// Replays one TABLE1 state-24 wind-up update from the class-owned
    /// PVar+0x300 accumulator. The native code advances this once per class
    /// update; no wall-clock cadence is inferred here.
    /// </summary>
    public static UyaClass5821WindupStep AdvanceNativeState24(
        float accumulator)
    {
        if (!float.IsFinite(accumulator))
            throw new ArgumentOutOfRangeException(nameof(accumulator));

        float updated = accumulator + NativeAttackAccumulatorDelta;
        float normalized =
            1f -
            ((updated + NativeAttackWindupInitial) /
             NativeAttackWindupNormalizationSpan);

        return new UyaClass5821WindupStep(
            updated,
            normalized,
            normalized >= 1f
                ? NativeDamageEmitterState
                : NativeAttackWindupState);
    }

    public static bool NeedsNativeStateEntrySetup(byte mobyFlagsBe) =>
        (mobyFlagsBe & NativeStateEntrySetupMask) == 0;

    public static bool EmitsPopulationBackedNativeDamage(byte nativeState) =>
        nativeState == NativeOrdinaryAttackState;

    public static bool EmitsState25FamilyDamage(byte nativeState) =>
        nativeState == NativeDamageEmitterState;

    /// <summary>
    /// State 25 compares the live Moby native Z coordinate with the scalar
    /// returned by its spatial query and stored at PVar+0x328.
    /// </summary>
    public static bool ShouldEnterNativeState26(
        float liveNativeZ,
        float queryVerticalThreshold)
    {
        if (!float.IsFinite(liveNativeZ))
            throw new ArgumentOutOfRangeException(nameof(liveNativeZ));
        if (!float.IsFinite(queryVerticalThreshold))
            throw new ArgumentOutOfRangeException(nameof(queryVerticalThreshold));

        return liveNativeZ <= queryVerticalThreshold;
    }

    /// <summary>
    /// Exact outbound descriptor constructed by the TABLE1 state-25 handler.
    /// Spatial query execution and player consequence remain host-unimplemented.
    /// </summary>
    public static UyaClass5821AttackDescriptor NativeState25Attack() =>
        new(
            NativeDamageEmitterState,
            NativeDamageEmitterRadius,
            NativeDamageEmitterDamage,
            NativeDamageEmitterFlags,
            NativeDamageEmitterRecordKind,
            NativeDamageEmitterRecordByte29,
            1f);

    public static UyaGameplayDamageEvent NativeState25RatchetDamage(
        UyaMobyRuntimeKey source) =>
        new(
            UyaGameplayEntityRef.Moby(source),
            UyaGameplayEntityRef.Player,
            nativeDamage: NativeDamageEmitterDamage,
            nativeDamageFlags: NativeDamageEmitterFlags,
            nativeRecordKind: NativeDamageEmitterRecordKind);

    public static UyaClass5821NativeObservation Observe(
        byte nativeState,
        double nativeLifetime,
        bool targetsRatchet)
    {
        if (!double.IsFinite(nativeLifetime))
            throw new ArgumentOutOfRangeException(nameof(nativeLifetime));

        return new UyaClass5821NativeObservation(
            nativeState,
            nativeLifetime,
            targetsRatchet);
    }

    /// <summary>
    /// Exact death-like terminal projection admitted by the retained TABLE1
    /// witness. State 0xFD alone is only generic Moby inactivity: other retained
    /// class-5821 instances are 0xFD while their lifetime remains positive.
    /// </summary>
    public static bool IsObservedTerminal(
        UyaClass5821NativeObservation observation) =>
        observation.NativeState == NativeObservedTerminalState &&
        observation.NativeLifetime <= 0d;

    internal static float ReadRuntimeLifetime(UyaMobyRuntimeInstance instance) =>
        BinaryPrimitives.ReadSingleLittleEndian(
            instance.MutablePVar.AsSpan(RuntimeLifetimeOffset, sizeof(float)));

    internal static void WriteRuntimeLifetime(
        UyaMobyRuntimeInstance instance,
        float lifetime) =>
        BinaryPrimitives.WriteSingleLittleEndian(
            instance.MutablePVar.AsSpan(RuntimeLifetimeOffset, sizeof(float)),
            lifetime);
}

public sealed record UyaClass5821AuthoredState(
    UyaMobyRuntimeKey Key,
    int LifetimeRelativePointer,
    int TargetBlockRelativePointer,
    int DamageConfigRelativePointer,
    float InitialLifetime,
    short AuthoredLifetime,
    float DamageConfigVerticalThreshold,
    byte DamageConfigByte49,
    float DamageConfigSameClassMultiplier,
    float DamageConfigOptionalMultiplier,
    byte NativeOrdinaryAttackDamageByte,
    byte NativeOrdinaryAttackFlagSelector,
    int PrimaryTargetSelectorIndex,
    float PrimaryTargetSelectorRadius,
    int SecondaryTargetSelectorIndex,
    float SecondaryTargetSelectorRadius,
    int InitialTargetSelectorMode,
    int InitialTargetSelectorAux);

public sealed record UyaClass5821NativeObservation(
    byte NativeState,
    double NativeLifetime,
    bool TargetsRatchet);

public sealed record UyaClass5821TargetSelectionRequest(
    byte NativeSubtype,
    int NativeSelectorIndex,
    float Radius,
    int NativeMode,
    bool NativeAuxEnabled,
    int WorkspaceOffset,
    float NativeF13,
    float NativeF14,
    bool SeedRatchetBeforeCandidateReplacement);

public sealed record UyaClass5821State8AttackFacts(
    bool HasCurrentTarget,
    uint PlayerGlobalState,
    float TargetSeparation,
    float ShortestHeadingErrorRadians);

public sealed record UyaClass5821WindupStep(
    float Accumulator,
    float NormalizedProgress,
    byte NextNativeState);

public sealed record UyaClass5821AttackDescriptor(
    byte NativeState,
    float Radius,
    float Damage,
    uint Flags,
    byte RecordKind,
    byte RecordByte29,
    float SpatialScalar);

public sealed record UyaClass5821DamageResult(
    UyaClass5821AuthoredState Authored,
    float LifetimeBefore,
    float NativeRecordDamage,
    float AppliedLifetimeDamage,
    float LifetimeAfter,
    byte NativeRecordKind,
    bool ReachedNonPositiveLifetime,
    RuntimeEntityState EntityState);

/// <summary>
/// Exact-class projection of the recovered TABLE1 class-5821 incoming lifetime
/// damage path. It mutates only the copied runtime PVar lifetime scalar. Native
/// hit/death reaction states remain owned by the unrecovered class update, so a
/// lethal hit is not immediately projected as neutral terminal presentation.
/// </summary>
public sealed class UyaClass5821DamageSession : IUyaMobyDamageConsumer
{
    private readonly UyaMobyRuntimeSession _runtime;

    public UyaClass5821DamageSession(UyaMobyRuntimeSession runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.RegisterDamageConsumer(this);
    }

    public int NativeClassId => UyaClass5821Actor.NativeClassId;

    public bool CanApplyDamage(
        RuntimeDynamicObject source,
        UyaGameplayDamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(damage);

        if (source.SourceGame != "rac3" ||
            source.NativeClassId != NativeClassId ||
            !damage.Target.MatchesMoby(
                new UyaMobyRuntimeKey(source.NativeClassId, source.InstanceIndex)) ||
            damage.NativeDamageFlags is not uint flags ||
            damage.NativeDamage is not double nativeDamage ||
            damage.NativeRecordKind is not byte recordKind ||
            !UyaClass5821Actor.AdmitsLifetimeDamage(
                flags,
                nativeDamage,
                recordKind))
            return false;

        UyaClass5821AuthoredState authored;
        try
        {
            authored = UyaClass5821Actor.ReadAuthored(source);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return false;
        }

        if (!UyaClass5821Actor.HasRecoveredTable1DamageProfile(authored))
            return false;

        var instance = _runtime.Require(source);
        float lifetime = UyaClass5821Actor.ReadRuntimeLifetime(instance);
        return float.IsFinite(lifetime) && lifetime > 0f;
    }

    public object ApplyDamage(
        RuntimeDynamicObject source,
        UyaGameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage))
            throw new NotSupportedException(
                "UYA class-5821 damage does not match the recovered TABLE1 lifetime path.");

        var instance = _runtime.Require(source);
        var authored = UyaClass5821Actor.ReadAuthored(source);
        float before = UyaClass5821Actor.ReadRuntimeLifetime(instance);
        float nativeRecordDamage = (float)damage.NativeDamage!.Value;
        byte recordKind = damage.NativeRecordKind!.Value;

        // Common consumer 0x004443B0 clamps positive sub-unit damage to one
        // when the class-owned lifetime capacity at PVar+0x34 is <= 1.
        float applied = nativeRecordDamage;
        if (authored.AuthoredLifetime <= 1 &&
            applied > 0f &&
            applied < 1f)
            applied = 1f;

        // The same consumer replaces applied damage with the current lifetime
        // below config+0x40. TABLE1 class 5821 authors that threshold as 0.
        double vertical = instance.EntityState.Presentation.Transform.Matrix[13];
        if (vertical < authored.DamageConfigVerticalThreshold)
            applied = before;

        float after = before - applied;
        UyaClass5821Actor.WriteRuntimeLifetime(instance, after);

        return new UyaClass5821DamageResult(
            authored,
            before,
            nativeRecordDamage,
            applied,
            after,
            recordKind,
            after <= 0f,
            instance.EntityState);
    }
}

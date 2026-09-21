namespace OBP.RAC3.Player;

/// <summary>
/// Retail-backed UYA Ratchet animation-state and sequence-selection contract.
/// Sequence IDs refer to the dedicated Ratchet sequence table, not ordinary
/// class-local Moby sequence tables.
/// </summary>
public static class UyaRatchetSequenceSelection
{
    public const int SequenceCount = 127;

    public const int IdleNativeState = 0;
    public const int OrdinaryLocomotionNativeState = 2;
    public const int LocomotionStopNativeState = 3;
    public const int OrdinaryFallNativeState = 6;
    public const int OrdinaryJumpNativeState = 7;

    public const int PrimaryCombatNativeState = 19;
    public const int SecondaryCombatNativeState = 20;
    public const int TertiaryCombatNativeState = 21;

    public const int DamageNativeStateA = 23;
    public const int DamageNativeStateB = 24;
    public const int DamageNativeStateC = 25;
    public const int DamageNativeStateD = 26;
    public const int FallDeathNativeState = 118;
    public const int LavaDeathNativeState = 123;
    public const int ElectricDeathNativeState = 127;

    public const int NeutralIdleSequenceId = 0;

    public const int LocomotionSequenceBaseId = 3;
    public const int LocomotionSubstate0SequenceId = 3;
    public const int LocomotionSubstate1SequenceId = 4;
    public const int LocomotionStopSequenceA = 5;
    public const int LocomotionStopSequenceB = 6;

    public const int OrdinaryFallSequenceA = 10;
    public const int OrdinaryFallSequenceB = 11;
    public const float OrdinaryFallMetricThreshold = 1.75f;

    public const int DamageSequenceA = 32;
    public const int DamageSequenceB = 33;
    public const int DamageSequenceC = 36;
    public const int DamageSequenceD = 37;

    public const int State19ComboSequenceBaseId = 23;
    public const int State20SequenceId = 40;
    public const int State21SequenceId = 26;
    public const int FallDeathSequenceId = 90;
    public const int LavaDeathSequenceId = 11;
    public const int ElectricDeathSequenceId = 17;

    private static readonly int[] locomotionFamily =
        [LocomotionSubstate0SequenceId, LocomotionSubstate1SequenceId];
    private static readonly int[] locomotionStopFamily =
        [LocomotionStopSequenceA, LocomotionStopSequenceB];
    private static readonly int[] damageFamily =
        [DamageSequenceA, DamageSequenceB, DamageSequenceC, DamageSequenceD];
    private static readonly int[] state19ComboFamily =
        [State19ComboSequenceBaseId, State19ComboSequenceBaseId + 1, State19ComboSequenceBaseId + 2];

    public static IReadOnlyList<int> LocomotionFamily => locomotionFamily;
    public static IReadOnlyList<int> LocomotionStopFamily => locomotionStopFamily;
    public static IReadOnlyList<int> DamageFamily => damageFamily;
    public static IReadOnlyList<int> State19ComboFamily => state19ComboFamily;

    /// <summary>
    /// Native state 2 indexes the ordinary movement sequence as 3 plus the
    /// player-global +0x25C8 locomotion substate. Retained ordinary paths only
    /// establish substate values 0 and 1.
    /// </summary>
    public static int SelectOrdinaryLocomotionSequence(int nativeLocomotionSubstate)
    {
        if ((uint)nativeLocomotionSubstate > 1)
            throw new ArgumentOutOfRangeException(nameof(nativeLocomotionSubstate));

        return LocomotionSequenceBaseId + nativeLocomotionSubstate;
    }

    /// <summary>
    /// Neutral-mode native fall state 6 selects 10 or 11 from the recovered
    /// +0x33C metric boundary. Equipment-mode alternatives are intentionally
    /// outside this ordinary contract.
    /// </summary>
    public static int SelectOrdinaryFallSequence(float nativeAirborneMetric) =>
        nativeAirborneMetric > OrdinaryFallMetricThreshold
            ? OrdinaryFallSequenceB
            : OrdinaryFallSequenceA;

    public static int? SelectDamageSequence(int nativeState) =>
        nativeState switch
        {
            DamageNativeStateA => DamageSequenceA,
            DamageNativeStateB => DamageSequenceB,
            DamageNativeStateC => DamageSequenceC,
            DamageNativeStateD => DamageSequenceD,
            _ => null,
        };

    public static int SelectState19ComboSequence(int comboStage)
    {
        if ((uint)comboStage > 2)
            throw new ArgumentOutOfRangeException(nameof(comboStage));
        return State19ComboSequenceBaseId + comboStage;
    }

    public static int? SelectRecoveredCombatSequence(int nativeState, int comboStage = 0) =>
        nativeState switch
        {
            PrimaryCombatNativeState => SelectState19ComboSequence(comboStage),
            SecondaryCombatNativeState => State20SequenceId,
            TertiaryCombatNativeState => State21SequenceId,
            _ => null,
        };

    public static int? SelectDeathSequence(int nativeState) =>
        nativeState switch
        {
            FallDeathNativeState => FallDeathSequenceId,
            LavaDeathNativeState => LavaDeathSequenceId,
            ElectricDeathNativeState => ElectricDeathSequenceId,
            _ => null,
        };

    /// <summary>
    /// The controlled L2 witness proves a distinct strafe-facing basis at the
    /// same ordinary planar speed, but it did not capture +0x25C8 or live Moby
    /// +0x43. A dedicated strafe sequence therefore remains unproven.
    /// </summary>
    public static bool HasRecoveredDedicatedStrafeSequence => false;

    /// <summary>
    /// A stationary jump witness establishes native state 7. The shared airborne
    /// handler contains recovered branches selecting sequence 7, but no retained
    /// live sequence-byte witness proves a universal state-7-to-sequence-7 map.
    /// </summary>
    public static bool HasRecoveredUniversalJumpSequence => false;

    /// <summary>
    /// Neutral state 0 reaches sequence 0 through the native idle selector.
    /// Other helper results are contextual and are not promoted as ordinary
    /// idle variants until their predicates are bounded.
    /// </summary>
    public static bool HasRecoveredContextualIdleCycle => false;

    /// <summary>
    /// Ratchet's 127 dedicated sequences are structurally decoded, but all 2,968
    /// retained frame references use UYA's special player-frame encoding. This
    /// selector contract therefore does not imply pose playback is decoded.
    /// </summary>
    public static bool HasDecodedRatchetPoseFrames => false;
}

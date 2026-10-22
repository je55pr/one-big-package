using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source-backed admission and select attack transitions for native Aridia
/// class 612 (LEVEL2:0x2d7748). This is deliberately not a translation of
/// every pathfinding, physics, emitter and damage branch of its 18-slot table.
/// </summary>
public static class Rac1AridiaConstructorbotNative
{
    public const int NativeClassId = Rac1AridiaEnemies.Constructorbot;
    public const int InitialModePvarOffset = 0x194;
    public const int EngagementRadiusPvarOffset = 0x180;
    public const int ActivationVolumePvarOffset = 0x18c;
    public const int WaypointRecordPvarOffset = 0x188;

    /// <summary>
    /// LEVEL2:0x2d7c98 and 0x2d7fac index source 0x80-byte native records,
    /// loaded by level_load.cpp from gameplay header +0x60 into 0x1600ec.
    /// Each record's +0x30 translation is already Rac1ClipVolume.Centre.
    /// It is NOT the path-list structure at gameplay header +0x70.
    /// </summary>
    public static int ReadWaypointRecordIndex(ReadOnlySpan<byte> pvar, int recordCount)
    {
        if (recordCount <= 0 || pvar.Length < WaypointRecordPvarOffset + sizeof(int))
            throw new InvalidDataException("Constructorbot waypoint records unavailable.");
        int index = BinaryPrimitives.ReadInt32LittleEndian(
            pvar.Slice(WaypointRecordPvarOffset, sizeof(int)));
        if (index < 0 || index >= recordCount)
            throw new InvalidDataException("Constructorbot waypoint index is out of native record range.");
        return index;
    }

    /// <summary>
    /// Source state8/9 mode -1 compares target's full XYZ distance to the
    /// native record +0x30 centre with PVar +0x184. Never substitute a
    /// spawn-based distance or the oriented point_in_box test.
    /// </summary>
    public static bool NativeWaypointActivation(
        System.Numerics.Vector3 targetNative, System.Numerics.Vector3 waypointNative,
        float triggerRadius)
    {
        if (!float.IsFinite(triggerRadius) || triggerRadius < 0 ||
            !float.IsFinite(targetNative.X) || !float.IsFinite(targetNative.Y) ||
            !float.IsFinite(targetNative.Z) || !float.IsFinite(waypointNative.X) ||
            !float.IsFinite(waypointNative.Y) || !float.IsFinite(waypointNative.Z))
            throw new ArgumentOutOfRangeException(nameof(triggerRadius));
        return System.Numerics.Vector3.Distance(targetNative, waypointNative) < triggerRadius;
    }

    public const int ActivationDistancePvarOffset = 0x184;
    public const float FollowupEngagementMargin = 1f;
    public const float FollowupHeightTolerance = 3f;

    public static int ReadActivationVolume(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < ActivationVolumePvarOffset + sizeof(int))
            throw new InvalidDataException("Constructorbot has no native activation volume selector.");
        int index = BinaryPrimitives.ReadInt32LittleEndian(
            pvar.Slice(ActivationVolumePvarOffset, sizeof(int)));
        if (index is < -1 or > 65535)
            throw new InvalidDataException("Constructorbot activation volume selector is invalid.");
        return index;
    }

    public static float ReadActivationDistance(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < ActivationDistancePvarOffset + sizeof(float))
            throw new InvalidDataException("Constructorbot has no native activation distance.");
        float value = BinaryPrimitives.ReadSingleLittleEndian(
            pvar.Slice(ActivationDistancePvarOffset, sizeof(float)));
        if (!float.IsFinite(value) || value < 0)
            throw new InvalidDataException("Constructorbot activation distance is invalid.");
        return value;
    }

    /// <summary>
    /// LEVEL2:0x2d7f8c..0x2d8080: authored modes 8/9 share the activation
    /// decision. If +0x18c is nonnegative it is a native clip-volume index;
    /// otherwise target distance from the native 0x80-byte clip record +0x30
    /// is compared against PVar +0x184. Null means that a required
    /// source check is unresolved: never treat it as a true activation.
    /// Mode8 uses source sequence4 and mode9 sequence16 on admission.
    /// </summary>
    public static Frame StepActivation(int nativeState, bool? triggerSatisfied)
    {
        if (nativeState is not (ParticleModeState or ParticleAlternateState))
            throw new ArgumentOutOfRangeException(nameof(nativeState));
        if (triggerSatisfied != true)
            return new(nativeState, nativeState == ParticleModeState ? 2 : 15);
        return new(2, nativeState == ParticleModeState ? 4 : 16);
    }

    /// <summary>
    /// LEVEL2:0x2d79a4: state2 retains its entry clip (4 from mode8,
    /// 16 from mode9) until source animation completion, then enters
    /// state3 and selects sequence4. No host time-based substitute.
    /// </summary>
    public static Frame StepActivatedAnimation(
        bool animationCompleted, int entrySequence)
    {
        if (entrySequence is not (4 or 16))
            throw new ArgumentOutOfRangeException(nameof(entrySequence));
        return animationCompleted ? new(3, 4) : new(2, entrySequence);
    }

    /// <summary>
    /// LEVEL2:0x2d82a0..0x2d83a8: source state12 turns toward target,
    /// waits for animation completion, then chooses state7/sequence0 if
    /// target planar distance is at least engagement radius + 1 OR if its
    /// vertical difference is at least 3. Otherwise stays in state12/seq9.
    /// </summary>
    public static Frame StepFollowup(bool animationCompleted, float planarDistance,
        float verticalDelta, float engagementRadius)
    {
        if (!animationCompleted)
            return new(FollowupState, 9);
        if (!float.IsFinite(planarDistance) || !float.IsFinite(verticalDelta) ||
            !float.IsFinite(engagementRadius) || planarDistance < 0 || engagementRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(planarDistance));
        return planarDistance >= engagementRadius + FollowupEngagementMargin ||
            MathF.Abs(verticalDelta) >= FollowupHeightTolerance
            ? new(RestState, 0)
            : new(FollowupState, 9);
    }

    /// <summary>
    /// LEVEL2:0x2d838c: source state13 calls enemy_physics and returns
    /// to state7/animation0 only when the native result has flag 0x40.
    /// The caller must supply an actual physics contact signal, not a timer.
    /// </summary>
    public static Frame StepRecovery(bool nativePhysicsContactFlag40, int currentSequence) =>
        nativePhysicsContactFlag40 ? new(RestState, 0) : new(ContactRecoveryState, currentSequence);

    public const float State7HeightTolerance = 4f;
    public const float State7WaypointTolerance = 1f;
    public const float State7FacingTolerance = MathF.PI / 36f; // 5 degrees

    public static float ReadEngagementRadius(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < EngagementRadiusPvarOffset + sizeof(float))
            throw new InvalidDataException("Constructorbot has no native engagement radius.");
        float value = BinaryPrimitives.ReadSingleLittleEndian(
            pvar.Slice(EngagementRadiusPvarOffset, sizeof(float)));
        if (!float.IsFinite(value) || value is <= 0 or > 1000f)
            throw new InvalidDataException("Constructorbot engagement radius is invalid.");
        return value;
    }

    /// <summary>
    /// Source state7 branch L2:0x2d7c98..0x2d7dd4:
    /// target within native PVar+0x180 radius of waypoint, vertical delta
    /// less than 4, Moby within one unit of waypoint, facing error below
    /// about five degrees. Original waypoint is the decoded header +0x60
    /// native record at PVar +0x188, with centre at record offset +0x30.
    /// </summary>
    public static bool State7AdmitsJointContact(
        float targetToWaypointDistance, float targetVerticalDelta,
        float mobyToWaypointDistance, float facingErrorRadians, float engagementRadius)
    {
        return float.IsFinite(targetToWaypointDistance) &&
            float.IsFinite(targetVerticalDelta) &&
            float.IsFinite(mobyToWaypointDistance) &&
            float.IsFinite(facingErrorRadians) &&
            float.IsFinite(engagementRadius) &&
            targetToWaypointDistance >= 0 &&
            MathF.Abs(targetVerticalDelta) < State7HeightTolerance &&
            mobyToWaypointDistance >= 0 &&
            targetToWaypointDistance < engagementRadius &&
            mobyToWaypointDistance < State7WaypointTolerance &&
            MathF.Abs(facingErrorRadians) < State7FacingTolerance;
    }

    public const int RestState = 7;
    public const int ParticleModeState = 8;
    public const int ParticleAlternateState = 9;
    public const int AimState = 10;
    public const int JointContactState = 11;
    public const int FollowupState = 12;
    public const int ContactRecoveryState = 13;

    public readonly record struct Frame(int NativeState, int Sequence, bool JointContactQuery = false);
    public const int SourceEmitterParticleId = 15;
    public const int NativeEmitterCadenceMask = 3;

    /// <summary>
    /// LEVEL2:0x2d7ecc..0x2d7f8c. Only state 8 invokes source particle
    /// program 15, when the original global frame counter low two bits are
    /// zero and Moby+0x31 is nonzero. The emission position is semantic joint
    /// zero; native particle parameters remain in level globals and are not
    /// invented here. State 9 shares the following detection code but does
    /// NOT perform the particle spawn.
    /// </summary>
    public static bool ShouldEmitSourceParticle(
        int nativeState, uint sourceFrameCounter, bool mobyActive) =>
        nativeState == ParticleModeState && mobyActive &&
        (sourceFrameCounter & NativeEmitterCadenceMask) == 0;

    public static int ReadInitialMode(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < InitialModePvarOffset + sizeof(int))
            throw new InvalidDataException("Aridia Constructorbot has no initial variant PVar.");
        int mode = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(InitialModePvarOffset, sizeof(int)));
        if (mode is < 0 or > 3)
            throw new InvalidDataException("Unknown retail Constructorbot startup mode.");
        return mode;
    }

    // L2:0x2d7808..0x2d7998:
    // +0x194==0 -> state 7, sequence 0; ==1 -> state 10, sequence 0;
    // ==2 -> state 8, sequence 2; ==3 -> state 9, sequence 15.
    // This corresponds to four DIFFERENT authored populations, not a
    // universal stationary flamethrower on Aridia level load.
    public static Frame Initialize(ReadOnlySpan<byte> pvar) => ReadInitialMode(pvar) switch
    {
        0 => new(RestState, 0),
        1 => new(AimState, 0),
        2 => new(ParticleModeState, 2),
        3 => new(ParticleAlternateState, 15),
        _ => throw new InvalidDataException("Unknown Constructorbot startup mode.")
    };

    /// <summary>
    /// L2:0x2d8088 moves native state 10 into 12/sequence 9 only after
    /// the original animation-completion bit at Moby+0x70 &amp; 2.
    /// </summary>
    public static Frame StepAim(bool animationCompleted) =>
        animationCompleted ? new(FollowupState, 9) : new(AimState, 0);

    /// <summary>
    /// L2:0x2d812c (the jump table's STATE 11, not state 12) queries a
    /// transformed joint-0 contact volume EVERY update. Completion changes
    /// to state 13 and randomly selects sequence 8 or 10; the actual
    /// source damage solver and host contact result remain external.
    /// </summary>
    public static Frame StepJointContact(bool animationCompleted, bool chooseSequenceTen) =>
        animationCompleted
            ? new(ContactRecoveryState, chooseSequenceTen ? 10 : 8, JointContactQuery: true)
            : new(JointContactState, 8, JointContactQuery: true);
}

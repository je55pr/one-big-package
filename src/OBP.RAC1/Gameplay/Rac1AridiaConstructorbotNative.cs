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
    /// about five degrees. Original waypoint comes from native path nodes;
    /// a host may supply an explicitly provisional authored-spawn fallback.
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
